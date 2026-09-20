using System.Net.Http.Json;
using FoundryModelExplorer.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FoundryModelExplorer.Services;

/// <summary>
/// Reads the public Azure Retail Prices API (no auth) for serviceName 'Foundry Models' in one region,
/// following NextPageLink. Result is cached per region + currency.
/// </summary>
public sealed class PricingService
{
    private const string ServiceName = "Foundry Models";
    private readonly IHttpClientFactory _http;
    private readonly ExplorerOptions _options;
    private readonly IMemoryCache _cache;
    private readonly SampleData _sample;
    private readonly ILogger<PricingService> _log;

    public PricingService(IHttpClientFactory http, ExplorerOptions options, IMemoryCache cache, SampleData sample, ILogger<PricingService> log)
    {
        _http = http;
        _options = options;
        _cache = cache;
        _sample = sample;
        _log = log;
    }

    public Task<List<RetailPrice>> GetMetersAsync(string region, CancellationToken ct) => GetMetersAsync(region, _options.Currency, ct);

    public async Task<List<RetailPrice>> GetMetersAsync(string region, string currency, CancellationToken ct)
    {
        if (_options.UseSample) return _sample.Meters(region);

        var key = $"prices:{region}:{currency}";
        var meters = await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.PricesCacheMinutes);
            return await LoadAsync(region, currency, ct);
        });
        return meters ?? new List<RetailPrice>();
    }

    private async Task<List<RetailPrice>> LoadAsync(string region, string currency, CancellationToken ct)
    {
        var client = _http.CreateClient("prices");
        var filter = Uri.EscapeDataString($"serviceName eq '{ServiceName}' and armRegionName eq '{region}'");
        string? url = $"api/retail/prices?api-version=2023-01-01-preview&currencyCode='{currency}'&$filter={filter}";

        var items = new List<RetailPrice>();
        var pages = 0;
        while (url is not null && pages++ < 30)
        {
            var page = await GetPageWithRetryAsync(client, url, ct);
            if (page is null) break;
            items.AddRange(page.Items);
            url = page.NextPageLink;
        }

        _log.LogInformation("Retail Prices: {Count} meters for {Service} in {Region} ({Currency})",
            items.Count, ServiceName, region, currency);
        return items;
    }

    /// <summary>
    /// The Retail Prices API is public and rate-limited per caller (HTTP 429). Retry a few times, honouring
    /// Retry-After when it is sent and backing off exponentially otherwise, before giving up.
    /// </summary>
    private async Task<RetailPriceListResult?> GetPageWithRetryAsync(HttpClient client, string url, CancellationToken ct)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            using var response = await client.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<RetailPriceListResult>(ArmGateway.Json, ct);

            var status = (int)response.StatusCode;
            var retryable = status == 429 || status >= 500;
            if (!retryable || attempt >= maxAttempts)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"Retail Prices API returned {status} after {attempt} attempt(s). {Truncate(body)}");
            }

            TimeSpan delay;
            if (response.Headers.RetryAfter?.Delta is { } delta) delay = delta;
            else if (response.Headers.RetryAfter?.Date is { } at) delay = at - DateTimeOffset.UtcNow;
            else delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);
            if (delay > TimeSpan.FromSeconds(30)) delay = TimeSpan.FromSeconds(30);
            _log.LogWarning("Retail Prices {Status} on attempt {Attempt}; waiting {Delay}s", status, attempt, delay.TotalSeconds);
            await Task.Delay(delay, ct);
        }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
