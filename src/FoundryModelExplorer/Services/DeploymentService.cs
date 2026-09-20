using System.Text.Json;
using FoundryModelExplorer.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FoundryModelExplorer.Services;

/// <summary>
/// Lists every Microsoft.CognitiveServices/accounts/deployments in the subscription through Azure Resource Graph
/// and joins each one to the catalog of its region, so a developer sees which of *their* deployments run on a
/// version that is deprecating.
/// </summary>
public sealed class DeploymentService
{
    private const string Query = """
        resources
        | where type =~ 'microsoft.cognitiveservices/accounts/deployments'
        | extend accountName = tostring(split(id, '/')[8])
        | project id, name, location, resourceGroup, accountName,
                  modelName = tostring(properties.model.name),
                  modelVersion = tostring(properties.model.version),
                  modelFormat = tostring(properties.model.format),
                  skuName = tostring(sku.name),
                  capacity = toint(sku.capacity),
                  provisioningState = tostring(properties.provisioningState),
                  versionUpgradeOption = tostring(properties.versionUpgradeOption)
        | order by accountName asc, name asc
        """;

    private readonly ArmGateway _arm;
    private readonly CatalogService _catalog;
    private readonly ExplorerOptions _options;
    private readonly IMemoryCache _cache;
    private readonly SampleData _sample;
    private readonly ILogger<DeploymentService> _log;

    public DeploymentService(ArmGateway arm, CatalogService catalog, ExplorerOptions options, IMemoryCache cache, SampleData sample, ILogger<DeploymentService> log)
    {
        _arm = arm;
        _catalog = catalog;
        _options = options;
        _cache = cache;
        _sample = sample;
        _log = log;
    }

    public async Task<DeploymentsResponse> GetDeploymentsAsync(CancellationToken ct)
    {
        var response = new DeploymentsResponse
        {
            RetrievedAt = DateTimeOffset.UtcNow,
            Source = _options.UseSample ? "sample" : "azure",
        };

        var rows = _options.UseSample
            ? _sample.Deployments()
            : await _cache.GetOrCreateAsync("deployments", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return await LoadAsync(ct);
            }) ?? new List<DeploymentInfo>();

        // Join to the catalog per region (each region's catalog is cached by CatalogService).
        var now = DateTimeOffset.UtcNow;
        foreach (var group in rows.GroupBy(r => r.Region, StringComparer.OrdinalIgnoreCase))
        {
            CatalogResponse? catalog = null;
            try { catalog = await _catalog.GetCatalogAsync(group.Key, ct); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Catalog lookup failed for {Region}", group.Key);
                response.Warnings.Add($"Catalog for {group.Key} unavailable: {ex.Message}");
            }

            foreach (var d in group)
            {
                var match = catalog?.Models.FirstOrDefault(m =>
                    m.Name.Equals(d.ModelName, StringComparison.OrdinalIgnoreCase) &&
                    m.Version.Equals(d.ModelVersion, StringComparison.OrdinalIgnoreCase));
                if (match is null) { response.Deployments.Add(d); continue; }

                d.InCatalog = true;
                d.LifecycleStatus = match.LifecycleStatus;
                d.DeprecationInference = match.DeprecationInference;
                d.DaysUntilDeprecation = match.DaysUntilDeprecation;
                d.DeprecationLevel = match.DeprecationLevel;
                response.Deployments.Add(d);
            }
        }

        response.Deployments = response.Deployments
            .OrderBy(d => d.DaysUntilDeprecation ?? int.MaxValue).ThenBy(d => d.AccountName).ThenBy(d => d.Name)
            .ToList();
        return response;
    }

    private async Task<List<DeploymentInfo>> LoadAsync(CancellationToken ct)
    {
        var body = new
        {
            subscriptions = new[] { _options.SubscriptionId },
            query = Query,
            options = new { resultFormat = "objectArray", top = 1000 },
        };
        var result = await _arm.PostAsync<JsonElement>("providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01", body, ct);

        var list = new List<DeploymentInfo>();
        if (!result.TryGetProperty("data", out var data)) return list;
        foreach (var row in data.EnumerateArray())
        {
            string S(string p) => row.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            int? I(string p) => row.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

            list.Add(new DeploymentInfo
            {
                Id = S("id"),
                Name = S("name"),
                Region = S("location").ToLowerInvariant(),
                ResourceGroup = S("resourceGroup"),
                AccountName = S("accountName"),
                ModelName = S("modelName"),
                ModelVersion = S("modelVersion"),
                ModelFormat = S("modelFormat"),
                SkuName = S("skuName"),
                Capacity = I("capacity"),
                ProvisioningState = S("provisioningState"),
                VersionUpgradeOption = S("versionUpgradeOption"),
            });
        }
        _log.LogInformation("Resource Graph: {Count} deployments", list.Count);
        return list;
    }
}
