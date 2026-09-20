<#
.SYNOPSIS
  Runs the Foundry Model Ledger locally with Azure Functions Core Tools.

.DESCRIPTION
  Creates src/FoundryModelExplorer/local.settings.json from the template (once), fills in the subscription
  id from the current az login, and starts the Function host. Open http://localhost:7071/ afterwards.

  Works in Windows PowerShell 5.1.

.PARAMETER SubscriptionId
  Subscription whose catalog and deployments to read. Defaults to the current az account.

.PARAMETER Region
  Region the UI opens on. Default swedencentral.

.PARAMETER Sample
  Use the embedded sample snapshot instead of Azure (no login needed).
#>
[CmdletBinding()]
param(
    [string] $SubscriptionId = "",
    [string] $Region = "swedencentral",
    [switch] $Sample
)

$ErrorActionPreference = "Stop"
$projectDir = Join-Path $PSScriptRoot "..\src\FoundryModelExplorer"
$settingsPath = Join-Path $projectDir "local.settings.json"
$templatePath = Join-Path $projectDir "local.settings.json.template"

if (-not (Get-Command func -ErrorAction SilentlyContinue)) {
    throw "Azure Functions Core Tools (func) not found. Install with: npm i -g azure-functions-core-tools@4"
}

if (-not $Sample) {
    if ([string]::IsNullOrWhiteSpace($SubscriptionId)) {
        $SubscriptionId = (az account show --query id -o tsv)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($SubscriptionId)) {
            throw "Not logged in. Run 'az login' first, or pass -SubscriptionId."
        }
    }
    Write-Host "Using subscription $SubscriptionId, region $Region" -ForegroundColor Cyan
}

$settings = Get-Content $templatePath -Raw | ConvertFrom-Json
$settings.Values.AZURE_SUBSCRIPTION_ID = $SubscriptionId
$settings.Values.DEFAULT_REGION = $Region
if ($Sample) { $settings.Values.CATALOG_SOURCE = "sample" } else { $settings.Values.CATALOG_SOURCE = "azure" }
$settings | ConvertTo-Json -Depth 5 | Set-Content $settingsPath -Encoding UTF8
Write-Host "Wrote $settingsPath" -ForegroundColor DarkGray

Push-Location $projectDir
try {
    func start
    if ($LASTEXITCODE -ne 0) { throw "func start exited with $LASTEXITCODE" }
}
finally {
    Pop-Location
}
