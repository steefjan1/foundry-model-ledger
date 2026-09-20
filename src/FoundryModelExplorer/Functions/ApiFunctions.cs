using FoundryModelExplorer.Models;
using FoundryModelExplorer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FoundryModelExplorer.Functions;

/// <summary>
/// JSON API behind the explorer UI.
///   GET api/health
///   GET api/regions
///   GET api/models?region=swedencentral
///   GET api/models/{name}/{version}?region=swedencentral
///   GET api/meters?region=swedencentral&amp;q=gpt
///   GET api/deployments
/// </summary>
public sealed class ApiFunctions
{
    private readonly RegionService _regions;
    private readonly CatalogService _catalog;
    private readonly PricingService _pricing;
    private readonly DeploymentService _deployments;
    private readonly ExplorerOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ApiFunctions> _log;

    public ApiFunctions(RegionService regions, CatalogService catalog, PricingService pricing, DeploymentService deployments, ExplorerOptions options, IMemoryCache cache, ILogger<ApiFunctions> log)
    {
        _cache = cache;
        _regions = regions;
        _catalog = catalog;
        _pricing = pricing;
        _deployments = deployments;
        _options = options;
        _log = log;
    }

    [Function("Health")]
    public IActionResult Health([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/health")] HttpRequest req)
        => new OkObjectResult(new
        {
            status = "ok",
            source = _options.UseSample ? "sample" : "azure",
            subscriptionConfigured = !string.IsNullOrEmpty(_options.SubscriptionId),
            defaultRegion = _options.DefaultRegion,
            currency = _options.Currency,
            utc = DateTimeOffset.UtcNow,
        });

    /// <summary>Drops every cached catalog, meter list, region list and deployment list on this instance.</summary>
    [Function("CacheClear")]
    public IActionResult CacheClear([HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", Route = "api/cache/clear")] HttpRequest req)
    {
        if (_cache is MemoryCache mc) mc.Compact(1.0);
        _log.LogInformation("Cache cleared on request");
        return new OkObjectResult(new { status = "cleared", utc = DateTimeOffset.UtcNow, note = "Flex Consumption may run several instances; each clears on its own first request after this." });
    }

    [Function("Regions")]
    public async Task<IActionResult> Regions([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/regions")] HttpRequest req, CancellationToken ct)
        => await Guard(async () => new OkObjectResult(await _regions.GetRegionsAsync(ct)));

    [Function("Models")]
    public async Task<IActionResult> Models([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/models")] HttpRequest req, CancellationToken ct)
        => await Guard(async () =>
        {
            var catalog = await _catalog.GetCatalogAsync(RegionOf(req), CurrencyOf(req), ct);
            return new OkObjectResult(catalog);
        });

    [Function("Model")]
    public async Task<IActionResult> Model([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/models/{name}/{version}")] HttpRequest req, string name, string version, CancellationToken ct)
        => await Guard(async () =>
        {
            var model = await _catalog.GetModelAsync(RegionOf(req), CurrencyOf(req), name, version, ct);
            return model is null ? new NotFoundObjectResult(new { error = $"{name} {version} not found in {RegionOf(req)}" }) : new OkObjectResult(model);
        });

    [Function("Meters")]
    public async Task<IActionResult> Meters([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/meters")] HttpRequest req, CancellationToken ct)
        => await Guard(async () =>
        {
            var region = RegionOf(req);
            var currency = CurrencyOf(req);
            var q = (req.Query["q"].FirstOrDefault() ?? "").Trim();
            var meters = await _pricing.GetMetersAsync(region, currency, ct);
            var items = string.IsNullOrEmpty(q)
                ? meters
                : meters.Where(m => (m.MeterName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || (m.SkuName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || (m.ProductName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            return new OkObjectResult(new MetersResponse
            {
                Region = region,
                Currency = currency,
                Total = meters.Count,
                Items = items.OrderBy(m => m.ProductName).ThenBy(m => m.MeterName).Take(2000).ToList(),
            });
        });

    [Function("Deployments")]
    public async Task<IActionResult> Deployments([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/deployments")] HttpRequest req, CancellationToken ct)
        => await Guard(async () => new OkObjectResult(await _deployments.GetDeploymentsAsync(ct)));

    /// <summary>ISO 4217 code from ?currency=, validated to three letters; falls back to PRICE_CURRENCY.</summary>
    private string CurrencyOf(HttpRequest req)
    {
        var c = (req.Query["currency"].FirstOrDefault() ?? "").Trim().ToUpperInvariant();
        return c.Length == 3 && c.All(char.IsLetter) ? c : _options.Currency;
    }

    private string RegionOf(HttpRequest req)
    {
        var region = req.Query["region"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(region) ? _options.DefaultRegion : region.Trim().ToLowerInvariant();
    }

    private async Task<IActionResult> Guard(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ArmException ex)
        {
            _log.LogError(ex, "ARM failure");
            var hint = ex.StatusCode switch
            {
                401 or 403 => "The identity running this app needs the Reader role on the subscription (see README).",
                404 => "Check AZURE_SUBSCRIPTION_ID and the region name.",
                _ => "See the Function log for the ARM response body.",
            };
            return new ObjectResult(new { error = ex.Message, status = ex.StatusCode, hint, detail = Truncate(ex.Body) }) { StatusCode = 502 };
        }
        catch (Azure.Identity.CredentialUnavailableException ex)
        {
            _log.LogError(ex, "No Azure credential");
            return new ObjectResult(new { error = "No Azure credential available.", hint = "Run 'az login' (or 'azd auth login') locally, or assign a managed identity in Azure.", detail = ex.Message }) { StatusCode = 502 };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled");
            return new ObjectResult(new { error = ex.Message, detail = ex.GetType().Name }) { StatusCode = 500 };
        }
    }

    private static string Truncate(string s) => s.Length <= 1000 ? s : s[..1000] + "…";
}
