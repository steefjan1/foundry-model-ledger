using System.Text.Json;
using FoundryModelExplorer.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FoundryModelExplorer.Services;

/// <summary>
/// Lists every model deployment in the subscription and joins each one to the catalog of its region, so a
/// developer sees which of *their* deployments run on a version that is deprecating.
///
/// Resource Graph does not index Microsoft.CognitiveServices/accounts/deployments (a query returns zero rows even
/// with deployments present), so this goes through ARM: list the accounts, then list the deployments per account.
/// Reader on the subscription covers both.
/// </summary>
public sealed class DeploymentService
{
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

        response.AccountCount = _options.UseSample
            ? rows.Select(r => r.AccountName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            : _cache.TryGetValue("deployments:accounts", out int n) ? n : 0;
        response.Deployments = response.Deployments
            .OrderBy(d => d.DaysUntilDeprecation ?? int.MaxValue).ThenBy(d => d.AccountName).ThenBy(d => d.Name)
            .ToList();
        return response;
    }

    private async Task<List<DeploymentInfo>> LoadAsync(CancellationToken ct)
    {
        const string apiVersion = "2024-10-01";
        var sub = _options.SubscriptionId;

        var accounts = await _arm.GetAllPagesAsync<JsonElement>(
            $"subscriptions/{sub}/providers/Microsoft.CognitiveServices/accounts?api-version={apiVersion}", ct);
        _log.LogInformation("ARM: {Count} Cognitive Services accounts", accounts.Count);
        _cache.Set("deployments:accounts", accounts.Count, TimeSpan.FromMinutes(5));

        // One deployments call per account, a few at a time.
        var list = new List<DeploymentInfo>();
        var gate = new SemaphoreSlim(6);
        var tasks = accounts.Select(async account =>
        {
            var id = account.GetProperty("id").GetString() ?? "";
            var accountName = account.GetProperty("name").GetString() ?? "";
            var location = account.TryGetProperty("location", out var l) ? (l.GetString() ?? "").ToLowerInvariant() : "";
            var resourceGroup = ResourceGroupOf(id);

            await gate.WaitAsync(ct);
            try
            {
                var deployments = await _arm.GetAllPagesAsync<JsonElement>($"{id.TrimStart('/')}/deployments?api-version={apiVersion}", ct);
                var rows = new List<DeploymentInfo>();
                foreach (var d in deployments)
                {
                    var props = d.TryGetProperty("properties", out var pr) ? pr : default;
                    var model = props.ValueKind == JsonValueKind.Object && props.TryGetProperty("model", out var mo) ? mo : default;
                    var sku = d.TryGetProperty("sku", out var sk) ? sk : default;

                    string S(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    int? I(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

                    rows.Add(new DeploymentInfo
                    {
                        Id = S(d, "id"),
                        Name = S(d, "name"),
                        Region = location,
                        ResourceGroup = resourceGroup,
                        AccountName = accountName,
                        ModelName = S(model, "name"),
                        ModelVersion = S(model, "version"),
                        ModelFormat = S(model, "format"),
                        SkuName = S(sku, "name"),
                        Capacity = I(sku, "capacity"),
                        ProvisioningState = S(props, "provisioningState"),
                        VersionUpgradeOption = S(props, "versionUpgradeOption"),
                    });
                }
                return rows;
            }
            catch (ArmException ex)
            {
                _log.LogWarning(ex, "Deployments of {Account} could not be listed", accountName);
                return new List<DeploymentInfo>();
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        foreach (var rows in await Task.WhenAll(tasks)) list.AddRange(rows);
        _log.LogInformation("ARM: {Count} model deployments across {Accounts} accounts", list.Count, accounts.Count);
        return list;
    }

    private static string ResourceGroupOf(string resourceId)
    {
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var i = Array.FindIndex(parts, p => p.Equals("resourceGroups", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < parts.Length ? parts[i + 1] : "";
    }
}
