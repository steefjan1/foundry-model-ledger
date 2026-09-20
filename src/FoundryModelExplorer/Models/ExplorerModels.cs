using System.Text.Json.Serialization;

namespace FoundryModelExplorer.Models;

// What the UI consumes. Deliberately flat so the table can render without reshaping.

public sealed class CatalogResponse
{
    public string Region { get; set; } = "";
    public string Source { get; set; } = "azure";
    public string Currency { get; set; } = "USD";
    public DateTimeOffset RetrievedAt { get; set; }
    public int MeterCount { get; set; }
    public CatalogStats Stats { get; set; } = new();
    public List<ModelSummary> Models { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    /// <summary>Set when the region has no model catalog at all (no Cognitive Services provider there).</summary>
    public string? Note { get; set; }
}

public sealed class CatalogStats
{
    public int Models { get; set; }
    public int Versions { get; set; }
    public int Publishers { get; set; }
    public int GenerallyAvailable { get; set; }
    public int Preview { get; set; }
    public int Deprecating { get; set; }
    public int DeprecatingWithin90Days { get; set; }
    public int Deprecated { get; set; }
    public int WithPrice { get; set; }
}

public sealed class ModelSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Format { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Family { get; set; } = "";
    public string Modality { get; set; } = "";
    public string LifecycleStatus { get; set; } = "";
    public bool IsDefaultVersion { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>What the model can do, as ARM reports it (chatCompletion, embeddings, fineTune, ...).</summary>
    public List<string> Capabilities { get; set; } = new();
    public Dictionary<string, string> CapabilityDetails { get; set; } = new();

    public DateTimeOffset? DeprecationInference { get; set; }
    public DateTimeOffset? DeprecationFineTune { get; set; }
    public int? DaysUntilDeprecation { get; set; }
    /// <summary>ok | warning | serious | deprecated | unknown</summary>
    public string DeprecationLevel { get; set; } = "unknown";

    public List<SkuSummary> Skus { get; set; } = new();
    public List<string> DeploymentTypes { get; set; } = new();
    public int? MaxCapacity { get; set; }

    public PriceSummary Price { get; set; } = new();
}

public sealed class SkuSummary
{
    public string Name { get; set; } = "";
    public string? UsageName { get; set; }
    public int? CapacityMin { get; set; }
    public int? CapacityMax { get; set; }
    public int? CapacityDefault { get; set; }
    public int? CapacityStep { get; set; }
    public DateTimeOffset? DeprecationDate { get; set; }
}

public sealed class PriceSummary
{
    /// <summary>exact | name | none — how confident the meter match is.</summary>
    public string Confidence { get; set; } = "none";
    public string Currency { get; set; } = "USD";
    /// <summary>Headline: Global Standard (or best available) input/output per 1M tokens.</summary>
    public decimal? InputPer1M { get; set; }
    public decimal? OutputPer1M { get; set; }
    public decimal? CachedInputPer1M { get; set; }
    public string? HeadlineDeployment { get; set; }
    public string? HeadlineTier { get; set; }
    /// <summary>All matched meters, classified, so the developer can see the raw evidence.</summary>
    public List<PriceLine> Lines { get; set; } = new();
    public int MeterCount => Lines.Count;
}

public sealed class PriceLine
{
    public string MeterName { get; set; } = "";
    public string SkuName { get; set; } = "";
    public string ProductName { get; set; } = "";
    /// <summary>input | output | cachedInput | cacheWrite | other</summary>
    public string Direction { get; set; } = "other";
    /// <summary>Global | DataZone | Regional | Unspecified</summary>
    public string Deployment { get; set; } = "Unspecified";
    /// <summary>Standard | Batch | Priority | Flex</summary>
    public string Tier { get; set; } = "Standard";
    /// <summary>short | long | any</summary>
    public string Context { get; set; } = "any";
    public decimal RetailPrice { get; set; }
    public string UnitOfMeasure { get; set; } = "";
    /// <summary>Price normalised to 1M tokens when the unit is token based, otherwise null.</summary>
    public decimal? PricePer1M { get; set; }
    public bool DateMatched { get; set; }
    public string? MeterId { get; set; }
}

public sealed class RegionsResponse
{
    public string Default { get; set; } = "";
    public List<RegionInfo> Regions { get; set; } = new();
}

public sealed class RegionInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Geography { get; set; }
    /// <summary>True when Microsoft.CognitiveServices/accounts is offered in this region, i.e. a model catalog exists.</summary>
    public bool HostsFoundry { get; set; }
}

public sealed class DeploymentsResponse
{
    public DateTimeOffset RetrievedAt { get; set; }
    public string Source { get; set; } = "azure";
    public List<DeploymentInfo> Deployments { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int AccountCount { get; set; }
}

public sealed class DeploymentInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string ResourceGroup { get; set; } = "";
    public string Region { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public string ModelFormat { get; set; } = "";
    public string SkuName { get; set; } = "";
    public int? Capacity { get; set; }
    public string? ProvisioningState { get; set; }
    public string? VersionUpgradeOption { get; set; }

    // Joined from the catalog of the deployment's region.
    public string? LifecycleStatus { get; set; }
    public DateTimeOffset? DeprecationInference { get; set; }
    public int? DaysUntilDeprecation { get; set; }
    public string DeprecationLevel { get; set; } = "unknown";
    public bool InCatalog { get; set; }
}

public sealed class MetersResponse
{
    public string Region { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public int Total { get; set; }
    public List<RetailPrice> Items { get; set; } = new();
}

public sealed class AvailabilityResponse
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTimeOffset RetrievedAt { get; set; }
    public int RegionsChecked { get; set; }
    public int AvailableIn { get; set; }
    public List<RegionAvailability> Regions { get; set; } = new();
}

public sealed class RegionAvailability
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Geography { get; set; }
    /// <summary>available | absent | noCatalog | error</summary>
    public string Status { get; set; } = "absent";
}
