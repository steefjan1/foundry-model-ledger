<#
.SYNOPSIS
  Exports a real snapshot (catalog, price meters, regions, deployments) to samples/sample-catalog.json.

.DESCRIPTION
  Uses the same three sources as the Function App, through az CLI and the public Retail Prices API,
  so the UI can be run offline (CATALOG_SOURCE=sample) or the snapshot can be diffed over time.

  Works in Windows PowerShell 5.1. Requires az login.

.PARAMETER Region
  Region to export. Default swedencentral.

.PARAMETER Currency
  Retail Prices currency code. Default USD.

.PARAMETER OutFile
  Output path. Default samples/sample-catalog.json.
#>
[CmdletBinding()]
param(
    [string] $Region = "swedencentral",
    [string] $Currency = "USD",
    [string] $OutFile = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($OutFile)) { $OutFile = Join-Path $PSScriptRoot "..\samples\sample-catalog.json" }

$sub = az account show --query id -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sub)) { throw "Run 'az login' first." }

# 1. Model catalog (ARM, paged)
Write-Host "Catalog for $Region..." -ForegroundColor Cyan
$catalog = @()
$url = "https://management.azure.com/subscriptions/$sub/providers/Microsoft.CognitiveServices/locations/$Region/models?api-version=2024-10-01"
while ($url) {
    $page = az rest --method get --url $url -o json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "az rest failed for $url" }
    $catalog += $page.value
    $url = $page.nextLink
}
Write-Host "  $($catalog.Count) model versions"

# 2. Retail price meters (public, paged)
Write-Host "Retail prices for $Region ($Currency)..." -ForegroundColor Cyan
$meters = @()
$filter = [uri]::EscapeDataString("serviceName eq 'Foundry Models' and armRegionName eq '$Region'")
$url = "https://prices.azure.com/api/retail/prices?api-version=2023-01-01-preview&currencyCode='$Currency'&`$filter=$filter"
while ($url) {
    $page = Invoke-RestMethod -Uri $url -Method Get
    $meters += $page.Items
    $url = $page.NextPageLink
}
Write-Host "  $($meters.Count) meters"

# 3. Regions that host Cognitive Services accounts
Write-Host "Regions..." -ForegroundColor Cyan
$locations = az account list-locations -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "az account list-locations failed" }
$provider = az provider show --namespace Microsoft.CognitiveServices -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "az provider show failed" }
$accountLocations = ($provider.resourceTypes | Where-Object { $_.resourceType -eq "accounts" }).locations
$regions = @()
foreach ($display in $accountLocations) {
    $loc = $locations | Where-Object { $_.displayName -eq $display } | Select-Object -First 1
    if ($loc) {
        $geo = $null
        if ($loc.metadata) { $geo = $loc.metadata.geographyGroup }
        $regions += [pscustomobject]@{ name = $loc.name; displayName = $display; geography = $geo }
    }
}
$regions = $regions | Sort-Object geography, displayName
Write-Host "  $($regions.Count) regions"

# 4. Deployments (Resource Graph)
Write-Host "Deployments..." -ForegroundColor Cyan
$query = "resources | where type =~ 'microsoft.cognitiveservices/accounts/deployments' | extend accountName = tostring(split(id, '/')[8]) | project id, name, location, resourceGroup, accountName, modelName = tostring(properties.model.name), modelVersion = tostring(properties.model.version), modelFormat = tostring(properties.model.format), skuName = tostring(sku.name), capacity = toint(sku.capacity), provisioningState = tostring(properties.provisioningState), versionUpgradeOption = tostring(properties.versionUpgradeOption)"
$graph = az graph query -q $query --subscriptions $sub -o json 2>$null | ConvertFrom-Json
$deployments = @()
if ($LASTEXITCODE -eq 0 -and $graph) {
    foreach ($d in $graph.data) {
        $deployments += [pscustomobject]@{
            id = $d.id; name = $d.name; accountName = $d.accountName; resourceGroup = $d.resourceGroup
            region = $d.location; modelName = $d.modelName; modelVersion = $d.modelVersion; modelFormat = $d.modelFormat
            skuName = $d.skuName; capacity = $d.capacity; provisioningState = $d.provisioningState; versionUpgradeOption = $d.versionUpgradeOption
        }
    }
    Write-Host "  $($deployments.Count) deployments"
} else {
    Write-Warning "Resource Graph query skipped (install with: az extension add --name resource-graph)."
}

$snapshot = [pscustomobject]@{
    _note = "Exported by scripts/export-snapshot.ps1 on $(Get-Date -Format s) for $Region ($Currency)."
    region = $Region
    catalog = $catalog
    meters = $meters
    regions = $regions
    deployments = $deployments
}
$snapshot | ConvertTo-Json -Depth 20 | Set-Content $OutFile -Encoding UTF8
Write-Host "Wrote $OutFile" -ForegroundColor Green
