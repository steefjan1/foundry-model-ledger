using FoundryModelExplorer.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FoundryModelExplorer.Services;

/// <summary>
/// Reads the model catalog of one region from ARM
/// (GET /subscriptions/{sub}/providers/Microsoft.CognitiveServices/locations/{region}/models)
/// and enriches every model/version with a deprecation level and matched retail prices.
/// </summary>
public sealed class CatalogService
{
    private const string ApiVersion = "2024-10-01";
    private readonly ArmGateway _arm;
    private readonly PricingService _pricing;
    private readonly ExplorerOptions _options;
    private readonly IMemoryCache _cache;
    private readonly SampleData _sample;
    private readonly ILogger<CatalogService> _log;

    public CatalogService(ArmGateway arm, PricingService pricing, ExplorerOptions options, IMemoryCache cache, SampleData sample, ILogger<CatalogService> log)
    {
        _arm = arm;
        _pricing = pricing;
        _options = options;
        _cache = cache;
        _sample = sample;
        _log = log;
    }

    public Task<CatalogResponse> GetCatalogAsync(string region, CancellationToken ct) => GetCatalogAsync(region, _options.Currency, ct);

    public async Task<CatalogResponse> GetCatalogAsync(string region, string currency, CancellationToken ct)
    {
        region = region.ToLowerInvariant();
        currency = currency.ToUpperInvariant();
        var key = $"catalog:{region}:{currency}";
        var cached = await _cache.GetOrCreateAsync(key, async entry =>
        {
            var built = await BuildAsync(region, currency, ct);
            // A catalog whose prices failed (Retail Prices 429, typically) is kept only briefly so the next
            // request retries instead of serving a priceless catalog for a full hour.
            entry.AbsoluteExpirationRelativeToNow = built.Warnings.Count == 0
                ? TimeSpan.FromMinutes(_options.CatalogCacheMinutes)
                : TimeSpan.FromMinutes(2);
            return built;
        });
        return cached!;
    }

    /// <summary>
    /// The set of "name:version" ids a region carries, without prices or enrichment. Cheap enough to fan out over
    /// every region for the availability check; cached per region for the catalog cache duration.
    /// Returns null when the region has no Cognitive Services provider.
    /// </summary>
    public async Task<HashSet<string>?> GetModelIdsAsync(string region, CancellationToken ct)
    {
        region = region.ToLowerInvariant();
        return await _cache.GetOrCreateAsync($"catalog-ids:{region}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.CatalogCacheMinutes);
            if (_options.UseSample)
                return _sample.Catalog(region).Select(e => $"{e.Model.Name}:{e.Model.Version}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            try
            {
                var url = $"subscriptions/{_options.SubscriptionId}/providers/Microsoft.CognitiveServices/locations/{region}/models?api-version={ApiVersion}";
                var raw = await _arm.GetAllPagesAsync<ArmAccountModel>(url, ct);
                return raw.Select(e => $"{e.Model.Name}:{e.Model.Version}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (ArmException ex) when (ex.Body.Contains("NoRegisteredProviderFound", StringComparison.OrdinalIgnoreCase)
                                       || ex.Body.Contains("LocationNotAvailableForResourceType", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        });
    }

    public async Task<ModelSummary?> GetModelAsync(string region, string currency, string name, string version, CancellationToken ct)
    {
        var catalog = await GetCatalogAsync(region, currency, ct);
        return catalog.Models.FirstOrDefault(m =>
            string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.Version, version, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<CatalogResponse> BuildAsync(string region, string currency, CancellationToken ct)
    {
        var response = new CatalogResponse
        {
            Region = region,
            Source = _options.UseSample ? "sample" : "azure",
            Currency = currency,
            RetrievedAt = DateTimeOffset.UtcNow,
        };

        List<ArmAccountModel> raw;
        if (_options.UseSample)
        {
            raw = _sample.Catalog(region);
        }
        else
        {
            var url = $"subscriptions/{_options.SubscriptionId}/providers/Microsoft.CognitiveServices/locations/{region}/models?api-version={ApiVersion}";
            try
            {
                raw = await _arm.GetAllPagesAsync<ArmAccountModel>(url, ct);
            }
            catch (ArmException ex) when (ex.Body.Contains("NoRegisteredProviderFound", StringComparison.OrdinalIgnoreCase)
                                       || ex.Body.Contains("LocationNotAvailableForResourceType", StringComparison.OrdinalIgnoreCase))
            {
                // Not an error from the user's point of view: this region simply has no Azure AI services.
                _log.LogInformation("No Cognitive Services provider in {Region}", region);
                response.Note = $"{region} does not host Azure AI services (Microsoft.CognitiveServices), so there is no model catalog to show. Pick a region without the 'no AI services' marker.";
                response.Stats = Stats(response.Models);
                return response;
            }
        }
        _log.LogInformation("Catalog: {Count} model versions in {Region}", raw.Count, region);

        List<RetailPrice> meters;
        try
        {
            meters = await _pricing.GetMetersAsync(region, currency, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Retail Prices unavailable for {Region}", region);
            meters = new List<RetailPrice>();
            response.Warnings.Add("Retail Prices API could not be reached, so prices are omitted for now; this catalog is re-tried automatically after two minutes (or press Refresh data). " + ex.Message);
        }
        response.MeterCount = meters.Count;

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in raw)
        {
            var m = entry.Model;
            var summary = new ModelSummary
            {
                Id = $"{m.Name}:{m.Version}",
                Name = m.Name,
                Version = m.Version,
                Format = m.Format ?? "",
                Publisher = string.IsNullOrWhiteSpace(m.Publisher) ? PublisherOf(m.Format) : m.Publisher!,
                Kind = entry.Kind ?? "",
                Family = FamilyOf(m.Name),
                LifecycleStatus = m.LifecycleStatus ?? "Unknown",
                IsDefaultVersion = m.IsDefaultVersion ?? false,
                CreatedAt = m.SystemData?.CreatedAt,
                CapabilityDetails = m.CapabilityStrings(),
                Capabilities = CapabilityFlags(m.CapabilityStrings()),
                DeprecationInference = m.Deprecation?.Inference,
                DeprecationFineTune = m.Deprecation?.FineTune,
                MaxCapacity = m.MaxCapacity,
                Skus = (m.Skus ?? new()).Select(s => new SkuSummary
                {
                    Name = s.Name,
                    UsageName = s.UsageName,
                    CapacityMin = s.Capacity?.Minimum,
                    CapacityMax = s.Capacity?.Maximum,
                    CapacityDefault = s.Capacity?.Default,
                    CapacityStep = s.Capacity?.Step,
                    DeprecationDate = s.DeprecationDate,
                }).ToList(),
            };
            summary.DeploymentTypes = summary.Skus.Select(s => s.Name).Distinct().OrderBy(s => s).ToList();
            summary.Modality = ModalityOf(summary);

            if (summary.DeprecationInference is { } dep)
            {
                var days = (int)Math.Floor((dep - now).TotalDays);
                summary.DaysUntilDeprecation = days;
                summary.DeprecationLevel = LevelOf(days);
            }
            if (string.Equals(summary.LifecycleStatus, "Deprecated", StringComparison.OrdinalIgnoreCase))
                summary.DeprecationLevel = "deprecated";

            summary.Price = PriceMatcher.Match(m.Name, m.Version, m.Format, currency, meters);
            response.Models.Add(summary);
        }

        // ARM lists the same model/version once per account kind that can host it (e.g. kind "OpenAI" and
        // kind "AIServices"), each with its own SKU list. Show one row per model/version and merge the rest.
        response.Models = response.Models
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                if (g.Count() == 1) return first;
                first.Kind = string.Join(", ", g.Select(x => x.Kind).Where(k => !string.IsNullOrEmpty(k)).Distinct());
                first.Skus = g.SelectMany(x => x.Skus).GroupBy(k => k.Name).Select(k => k.First()).OrderBy(k => k.Name).ToList();
                first.DeploymentTypes = first.Skus.Select(k => k.Name).Distinct().OrderBy(k => k).ToList();
                first.IsDefaultVersion = g.Any(x => x.IsDefaultVersion);
                first.MaxCapacity = g.Max(x => x.MaxCapacity);
                foreach (var other in g.Skip(1))
                    foreach (var kv in other.CapabilityDetails)
                        first.CapabilityDetails.TryAdd(kv.Key, kv.Value);
                first.Capabilities = CapabilityFlags(first.CapabilityDetails);
                return first;
            })
            .OrderBy(x => x.Publisher).ThenBy(x => x.Name).ThenByDescending(x => x.Version)
            .ToList();
        response.Stats = Stats(response.Models);
        return response;
    }

    public static string LevelOf(int daysUntil) => daysUntil switch
    {
        < 0 => "deprecated",
        < 90 => "serious",
        < 365 => "warning",
        _ => "ok"
    };

    private static CatalogStats Stats(List<ModelSummary> models) => new()
    {
        Versions = models.Count,
        Models = models.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        Publishers = models.Select(m => m.Publisher).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        GenerallyAvailable = models.Count(m => m.LifecycleStatus.Equals("GenerallyAvailable", StringComparison.OrdinalIgnoreCase) || m.LifecycleStatus.Equals("Stable", StringComparison.OrdinalIgnoreCase)),
        Preview = models.Count(m => m.LifecycleStatus.Equals("Preview", StringComparison.OrdinalIgnoreCase)),
        Deprecating = models.Count(m => m.LifecycleStatus.Equals("Deprecating", StringComparison.OrdinalIgnoreCase)),
        DeprecatingWithin90Days = models.Count(m => m.DaysUntilDeprecation is >= 0 and < 90),
        Deprecated = models.Count(m => m.DeprecationLevel == "deprecated"),
        WithPrice = models.Count(m => m.Price.InputPer1M is not null || m.Price.OutputPer1M is not null),
    };

    /// <summary>Capabilities ARM reports as "true" strings, e.g. chatCompletion, embeddings, fineTune, imageGenerations.</summary>
    private static List<string> CapabilityFlags(Dictionary<string, string>? caps)
    {
        if (caps is null) return new List<string>();
        return caps
            .Where(kv => string.Equals(kv.Value, "true", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .OrderBy(k => k)
            .ToList();
    }

    private static string ModalityOf(ModelSummary m)
    {
        var caps = m.Capabilities;
        var n = m.Name.ToLowerInvariant();
        if (caps.Contains("embeddings")) return "embedding";
        if (caps.Contains("imageGenerations") || n.Contains("image") || n.Contains("dall-e") || n.Contains("flux")) return "image";
        if (n.Contains("sora") || caps.Contains("videoGenerations")) return "video";
        if (n.Contains("whisper") || n.Contains("transcribe") || n.Contains("tts") || caps.Contains("audio")) return "audio";
        if (n.Contains("realtime")) return "realtime";
        if (n.Contains("router")) return "router";
        if (caps.Contains("chatCompletion") || caps.Contains("completion") || caps.Contains("responses")) return "chat";
        return "other";
    }

    private static string FamilyOf(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.StartsWith("gpt-5") || n.StartsWith("gpt-chat")) return "GPT-5";
        if (n.StartsWith("gpt-4.1")) return "GPT-4.1";
        if (n.StartsWith("gpt-4o") || n.StartsWith("gpt-4")) return "GPT-4";
        if (n.StartsWith("gpt-3")) return "GPT-3.5";
        if (n.StartsWith("gpt-image") || n.StartsWith("dall-e")) return "Image";
        if (n.StartsWith("gpt-realtime") || n.StartsWith("gpt-audio")) return "Realtime / audio";
        if (n.StartsWith("o1") || n.StartsWith("o3") || n.StartsWith("o4")) return "o-series";
        if (n.StartsWith("text-embedding")) return "Embeddings";
        if (n.StartsWith("whisper") || n.StartsWith("tts") || n.Contains("transcribe")) return "Speech";
        if (n.StartsWith("sora")) return "Video";
        if (n.StartsWith("model-router")) return "Router";
        if (n.StartsWith("claude")) return "Claude";
        if (n.StartsWith("grok")) return "Grok";
        if (n.StartsWith("llama")) return "Llama";
        if (n.StartsWith("mistral") || n.StartsWith("ministral") || n.StartsWith("codestral")) return "Mistral";
        if (n.StartsWith("deepseek")) return "DeepSeek";
        if (n.StartsWith("phi")) return "Phi";
        if (n.StartsWith("cohere") || n.StartsWith("command") || n.StartsWith("embed-")) return "Cohere";
        if (n.StartsWith("mai")) return "MAI";
        if (n.StartsWith("kimi")) return "Kimi";
        return "Other";
    }

    private static string PublisherOf(string? format) => (format ?? "").ToLowerInvariant() switch
    {
        "openai" => "OpenAI",
        "xai" => "xAI",
        "meta" => "Meta",
        "mistral ai" or "mistral" => "Mistral AI",
        "deepseek" => "DeepSeek",
        "anthropic" => "Anthropic",
        "cohere" => "Cohere",
        "microsoft" => "Microsoft",
        "" => "Unknown",
        var f => f
    };
}
