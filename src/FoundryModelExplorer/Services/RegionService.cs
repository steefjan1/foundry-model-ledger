using System.Text.Json;
using FoundryModelExplorer.Models;
using Microsoft.Extensions.Caching.Memory;

namespace FoundryModelExplorer.Services;

/// <summary>
/// Every physical Azure region the subscription can see, flagged with whether Microsoft.CognitiveServices/accounts
/// is offered there (the provider manifest lists display names such as "Sweden Central").
/// </summary>
public sealed class RegionService
{
    private readonly ArmGateway _arm;
    private readonly ExplorerOptions _options;
    private readonly IMemoryCache _cache;
    private readonly SampleData _sample;

    public RegionService(ArmGateway arm, ExplorerOptions options, IMemoryCache cache, SampleData sample)
    {
        _arm = arm;
        _options = options;
        _cache = cache;
        _sample = sample;
    }

    public async Task<RegionsResponse> GetRegionsAsync(CancellationToken ct)
    {
        if (_options.UseSample)
            return new RegionsResponse { Default = _options.DefaultRegion, Regions = _sample.Regions() };

        var regions = await _cache.GetOrCreateAsync("regions", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            return await LoadAsync(ct);
        });

        return new RegionsResponse { Default = _options.DefaultRegion, Regions = regions! };
    }

    private async Task<List<RegionInfo>> LoadAsync(CancellationToken ct)
    {
        var sub = _options.SubscriptionId;

        // 1. Every physical region the subscription can see (logical/EUAP ones are skipped).
        var locations = await _arm.GetAllPagesAsync<JsonElement>(
            $"subscriptions/{sub}/locations?api-version=2022-12-01", ct);
        var regions = new List<RegionInfo>();
        foreach (var loc in locations)
        {
            var type = loc.TryGetProperty("metadata", out var md) && md.TryGetProperty("regionType", out var rt) ? rt.GetString() : "Physical";
            if (!string.Equals(type, "Physical", StringComparison.OrdinalIgnoreCase)) continue;
            var name = loc.GetProperty("name").GetString() ?? "";
            var display = loc.TryGetProperty("displayName", out var d) ? d.GetString() ?? name : name;
            string? geo = null; double? lat = null, lon = null;
            if (loc.TryGetProperty("metadata", out var md2))
            {
                if (md2.TryGetProperty("geographyGroup", out var g)) geo = g.GetString();
                // ARM returns latitude/longitude as strings.
                if (md2.TryGetProperty("latitude", out var la) && double.TryParse(la.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lav)) lat = lav;
                if (md2.TryGetProperty("longitude", out var lo) && double.TryParse(lo.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lov)) lon = lov;
            }
            regions.Add(new RegionInfo { Name = name, DisplayName = display, Geography = geo, Latitude = lat, Longitude = lon });
        }

        // 2. Flag the regions where the CognitiveServices "accounts" resource type is offered.
        var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Note: /providers/{namespace} is served by the Resources RP, so it takes a Resources api-version,
        // not the Cognitive Services one (2024-10-01 is rejected with InvalidApiVersionParameter).
        var provider = await _arm.GetAsync<JsonElement>(
            $"subscriptions/{sub}/providers/Microsoft.CognitiveServices?api-version=2022-12-01", ct);
        foreach (var rtype in provider.GetProperty("resourceTypes").EnumerateArray())
        {
            if (!string.Equals(rtype.GetProperty("resourceType").GetString(), "accounts", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var l in rtype.GetProperty("locations").EnumerateArray()) offered.Add(l.GetString() ?? "");
        }
        foreach (var r in regions) r.HostsFoundry = offered.Contains(r.DisplayName);

        return regions.OrderBy(r => r.Geography).ThenBy(r => r.DisplayName).ToList();
    }
}
