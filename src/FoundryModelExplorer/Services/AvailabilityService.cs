using FoundryModelExplorer.Models;
using Microsoft.Extensions.Logging;

namespace FoundryModelExplorer.Services;

/// <summary>
/// "Where else is this model available?" Fans the lightweight catalog read out over every region that hosts
/// Cognitive Services (a few at a time), and reports the regions that carry the exact name and version.
/// </summary>
public sealed class AvailabilityService
{
    private readonly RegionService _regions;
    private readonly CatalogService _catalog;
    private readonly ILogger<AvailabilityService> _log;

    public AvailabilityService(RegionService regions, CatalogService catalog, ILogger<AvailabilityService> log)
    {
        _regions = regions;
        _catalog = catalog;
        _log = log;
    }

    public async Task<AvailabilityResponse> GetAsync(string name, string version, CancellationToken ct)
    {
        var id = $"{name}:{version}";
        var regions = (await _regions.GetRegionsAsync(ct)).Regions.Where(r => r.HostsFoundry).ToList();
        var response = new AvailabilityResponse { Name = name, Version = version, RetrievedAt = DateTimeOffset.UtcNow, RegionsChecked = regions.Count };

        var gate = new SemaphoreSlim(8);
        var tasks = regions.Select(async r =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var ids = await _catalog.GetModelIdsAsync(r.Name, ct);
                return (region: r, status: ids is null ? "noCatalog" : ids.Contains(id) ? "available" : "absent");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Availability check failed for {Region}", r.Name);
                return (region: r, status: "error");
            }
            finally { gate.Release(); }
        });

        foreach (var (region, status) in await Task.WhenAll(tasks))
        {
            response.Regions.Add(new RegionAvailability { Name = region.Name, DisplayName = region.DisplayName, Geography = region.Geography, Status = status });
        }
        response.Regions = response.Regions.OrderBy(r => r.Geography).ThenBy(r => r.DisplayName).ToList();
        response.AvailableIn = response.Regions.Count(r => r.Status == "available");
        return response;
    }
}
