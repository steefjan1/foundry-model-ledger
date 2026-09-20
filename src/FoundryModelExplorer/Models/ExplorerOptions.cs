namespace FoundryModelExplorer.Services;

/// <summary>Settings read once from app settings / environment.</summary>
public sealed class ExplorerOptions
{
    public string SubscriptionId { get; init; } = "";
    public string DefaultRegion { get; init; } = "swedencentral";

    /// <summary>"azure" (live ARM + Retail Prices) or "sample" (embedded snapshot, no Azure calls).</summary>
    public string CatalogSource { get; init; } = "azure";
    public string Currency { get; init; } = "USD";
    public int CatalogCacheMinutes { get; init; } = 60;
    public int PricesCacheMinutes { get; init; } = 360;

    public bool UseSample => string.Equals(CatalogSource, "sample", StringComparison.OrdinalIgnoreCase);

    public static ExplorerOptions FromEnvironment()
    {
        static string Get(string name, string fallback) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

        return new ExplorerOptions
        {
            SubscriptionId = Get("AZURE_SUBSCRIPTION_ID", ""),
            DefaultRegion = Get("DEFAULT_REGION", "swedencentral").ToLowerInvariant(),
            CatalogSource = Get("CATALOG_SOURCE", "azure"),
            Currency = Get("PRICE_CURRENCY", "USD").ToUpperInvariant(),
            CatalogCacheMinutes = int.TryParse(Get("CATALOG_CACHE_MINUTES", "60"), out var c) ? c : 60,
            PricesCacheMinutes = int.TryParse(Get("PRICES_CACHE_MINUTES", "360"), out var p) ? p : 360,
        };
    }
}
