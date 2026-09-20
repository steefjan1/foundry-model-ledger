using System.Reflection;
using System.Text.Json;
using FoundryModelExplorer.Models;

namespace FoundryModelExplorer.Services;

/// <summary>
/// Offline mode (CATALOG_SOURCE=sample): serves the JSON snapshots embedded from /samples so the UI can be
/// exercised without a subscription. The snapshots were exported with scripts/export-snapshot.ps1.
/// </summary>
public sealed class SampleData
{
    private readonly Lazy<JsonDocument> _doc = new(() =>
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("samples.sample-catalog.json", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonDocument.Parse(stream);
    });

    public List<ArmAccountModel> Catalog(string region) =>
        _doc.Value.RootElement.GetProperty("catalog").Deserialize<List<ArmAccountModel>>(ArmGateway.Json) ?? new();

    public List<RetailPrice> Meters(string region) =>
        _doc.Value.RootElement.GetProperty("meters").Deserialize<List<RetailPrice>>(ArmGateway.Json) ?? new();

    public List<RegionInfo> Regions() =>
        _doc.Value.RootElement.GetProperty("regions").Deserialize<List<RegionInfo>>(ArmGateway.Json) ?? new();

    public List<DeploymentInfo> Deployments() =>
        _doc.Value.RootElement.GetProperty("deployments").Deserialize<List<DeploymentInfo>>(ArmGateway.Json) ?? new();
}
