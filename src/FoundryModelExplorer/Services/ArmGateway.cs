using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace FoundryModelExplorer.Services;

/// <summary>
/// Thin wrapper over Azure Resource Manager: gets a bearer token from the TokenCredential and
/// follows nextLink paging. Kept as raw HTTP + System.Text.Json on purpose: the request/response
/// shapes are visible in one place and there is no SDK version to chase.
/// </summary>
public sealed class ArmGateway
{
    private const string ArmScope = "https://management.azure.com/.default";
    private readonly IHttpClientFactory _http;
    private readonly TokenCredential _credential;
    private readonly ILogger<ArmGateway> _log;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    public ArmGateway(IHttpClientFactory http, TokenCredential credential, ILogger<ArmGateway> log)
    {
        _http = http;
        _credential = credential;
        _log = log;
    }

    public async Task<T> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        using var client = await CreateClientAsync(ct);
        using var response = await client.GetAsync(relativeUrl, ct);
        await EnsureSuccess(response, relativeUrl, ct);
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    public async Task<T> PostAsync<T>(string relativeUrl, object body, CancellationToken ct)
    {
        using var client = await CreateClientAsync(ct);
        using var response = await client.PostAsJsonAsync(relativeUrl, body, Json, ct);
        await EnsureSuccess(response, relativeUrl, ct);
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    /// <summary>GET a paged ARM list ("value" + "nextLink") and concatenate every page.</summary>
    public async Task<List<TItem>> GetAllPagesAsync<TItem>(string relativeUrl, CancellationToken ct)
    {
        var items = new List<TItem>();
        string? url = relativeUrl;
        using var client = await CreateClientAsync(ct);
        var pages = 0;
        while (url is not null && pages++ < 50)
        {
            using var response = await client.GetAsync(url, ct);
            await EnsureSuccess(response, url, ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("value", out var value))
            {
                foreach (var el in value.EnumerateArray())
                    items.Add(el.Deserialize<TItem>(Json)!);
            }
            url = doc.RootElement.TryGetProperty("nextLink", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
        return items;
    }

    private async Task<HttpClient> CreateClientAsync(CancellationToken ct)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { ArmScope }), ct);
        var client = _http.CreateClient("arm");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    private async Task EnsureSuccess(HttpResponseMessage response, string url, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        _log.LogError("ARM call failed {Status} {Url}: {Body}", (int)response.StatusCode, url, body);
        throw new ArmException((int)response.StatusCode, url, body);
    }
}

public sealed class ArmException : Exception
{
    public int StatusCode { get; }
    public string Url { get; }
    public string Body { get; }

    public ArmException(int statusCode, string url, string body)
        : base($"ARM returned {statusCode} for {url}")
    {
        StatusCode = statusCode;
        Url = url;
        Body = body;
    }
}
