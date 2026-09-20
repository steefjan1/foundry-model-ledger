using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoundryModelExplorer.Models;

// Shapes of the ARM responses we read. Only the fields the explorer uses are typed;
// everything else is kept as raw JSON so the detail panel can still show it.

public sealed class ArmModelListResult
{
    [JsonPropertyName("value")] public List<ArmAccountModel> Value { get; set; } = new();
    [JsonPropertyName("nextLink")] public string? NextLink { get; set; }
}

/// <summary>One entry of Microsoft.CognitiveServices/locations/{region}/models.</summary>
public sealed class ArmAccountModel
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("skuName")] public string? SkuName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("model")] public ArmModel Model { get; set; } = new();
}

public sealed class ArmModel
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("format")] public string? Format { get; set; }
    [JsonPropertyName("publisher")] public string? Publisher { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("maxCapacity")] public int? MaxCapacity { get; set; }
    [JsonPropertyName("isDefaultVersion")] public bool? IsDefaultVersion { get; set; }
    [JsonPropertyName("lifecycleStatus")] public string? LifecycleStatus { get; set; }
    // Values are usually strings ("true", "128000") but kept as JsonElement so a numeric value never breaks parsing.
    [JsonPropertyName("capabilities")] public Dictionary<string, JsonElement>? Capabilities { get; set; }
    [JsonPropertyName("finetuneCapabilities")] public Dictionary<string, JsonElement>? FinetuneCapabilities { get; set; }

    public Dictionary<string, string> CapabilityStrings() =>
        (Capabilities ?? new()).ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.GetRawText());
    [JsonPropertyName("deprecation")] public ArmDeprecation? Deprecation { get; set; }
    [JsonPropertyName("skus")] public List<ArmModelSku>? Skus { get; set; }
    [JsonPropertyName("systemData")] public ArmSystemData? SystemData { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ArmDeprecation
{
    [JsonPropertyName("inference")] public DateTimeOffset? Inference { get; set; }
    [JsonPropertyName("fineTune")] public DateTimeOffset? FineTune { get; set; }
}

public sealed class ArmModelSku
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("usageName")] public string? UsageName { get; set; }
    [JsonPropertyName("deprecationDate")] public DateTimeOffset? DeprecationDate { get; set; }
    [JsonPropertyName("capacity")] public ArmSkuCapacity? Capacity { get; set; }
    [JsonPropertyName("rateLimits")] public List<JsonElement>? RateLimits { get; set; }
}

public sealed class ArmSkuCapacity
{
    [JsonPropertyName("minimum")] public int? Minimum { get; set; }
    [JsonPropertyName("maximum")] public int? Maximum { get; set; }
    [JsonPropertyName("step")] public int? Step { get; set; }
    [JsonPropertyName("default")] public int? Default { get; set; }
}

public sealed class ArmSystemData
{
    [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("lastModifiedAt")] public DateTimeOffset? LastModifiedAt { get; set; }
}

/// <summary>One record of the Azure Retail Prices API.</summary>
public sealed class RetailPrice
{
    [JsonPropertyName("currencyCode")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("retailPrice")] public decimal Retail { get; set; }  // named Retail: a member cannot share its enclosing type's name
    [JsonPropertyName("unitPrice")] public decimal UnitPrice { get; set; }
    [JsonPropertyName("armRegionName")] public string? ArmRegionName { get; set; }
    [JsonPropertyName("meterId")] public string? MeterId { get; set; }
    [JsonPropertyName("meterName")] public string? MeterName { get; set; }
    [JsonPropertyName("skuName")] public string? SkuName { get; set; }
    [JsonPropertyName("productName")] public string? ProductName { get; set; }
    [JsonPropertyName("serviceName")] public string? ServiceName { get; set; }
    [JsonPropertyName("unitOfMeasure")] public string? UnitOfMeasure { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("effectiveStartDate")] public DateTimeOffset? EffectiveStartDate { get; set; }
}

public sealed class RetailPriceListResult
{
    [JsonPropertyName("Items")] public List<RetailPrice> Items { get; set; } = new();
    [JsonPropertyName("NextPageLink")] public string? NextPageLink { get; set; }
    [JsonPropertyName("Count")] public int Count { get; set; }
}
