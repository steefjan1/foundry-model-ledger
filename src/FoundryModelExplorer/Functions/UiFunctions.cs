using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace FoundryModelExplorer.Functions;

/// <summary>
/// Serves the single-page UI from the embedded wwwroot folder. host.json sets routePrefix to "" so the console
/// lives at the root of the Function App and the JSON API under /api.
/// </summary>
public sealed class UiFunctions
{
    private static readonly Assembly Asm = typeof(UiFunctions).Assembly;

    [Function("Ui")]
    public IActionResult Ui([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{*path}")] HttpRequest req, string? path)
    {
        var file = string.IsNullOrWhiteSpace(path) || path == "/" ? "index.html" : path.TrimStart('/');
        if (file.StartsWith("api/", StringComparison.OrdinalIgnoreCase)) return new NotFoundResult();

        var resourceName = Asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("wwwroot." + file.Replace('/', '.'), StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return new NotFoundResult();

        var stream = Asm.GetManifestResourceStream(resourceName)!;
        return new FileStreamResult(stream, ContentTypeOf(file));
    }

    private static string ContentTypeOf(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream",
    };
}
