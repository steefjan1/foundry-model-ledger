using System.Globalization;
using System.Text.RegularExpressions;
using FoundryModelExplorer.Models;

namespace FoundryModelExplorer.Services;

/// <summary>
/// Matches Retail Prices meters to catalog models.
///
/// Retail meter names are written for invoices, not for machines: "gpt 4.1 nano cached Inp glbl Tokens",
/// "5.6 sol ShortCo Cd Inp Std DZ 1M Tokens", "gpt-4o-mini-0718-Outp-regnl Tokens", "4.6 Inp DZ Tokens"
/// (that last one is grok-4.6). So the matcher
///   1. narrows meters to the model's publisher family via productName,
///   2. tokenises both sides the same way and requires every model token (minus the family prefix) to
///      appear in the meter, in order, with no *other* variant token (mini/nano/pro/...) present,
///   3. uses a MMDD or MMDDYYYY token in the meter to pin the version when the meter carries one,
///   4. classifies the meter (direction, deployment type, tier, context length) from its abbreviations,
///   5. normalises the unit to a price per 1M tokens.
/// Every matched meter is returned so the UI can show the evidence next to the headline number.
/// </summary>
public static class PriceMatcher
{
    private static readonly Regex TokenRx = new(@"[a-z]+|\d+(?:\.\d+)?", RegexOptions.Compiled);

    // Tokens that distinguish one model from its siblings. If a meter has one of these and the model
    // name does not, it is a different model.
    private static readonly HashSet<string> Variants = new(StringComparer.Ordinal)
    {
        "mini", "nano", "pro", "chat", "codex", "max", "sol", "luna", "terra", "astra", "latest",
        "realtime", "rt", "transcribe", "tts", "hd", "preview",
        "instruct", "reasoning", "vision", "search", "live", "diarize", "small", "large", "medium",
        "turbo", "flash", "lite", "fast", "haiku", "sonnet", "opus", "maverick", "scout", "sunburst", "flare"
    };

    // Leading tokens that meters routinely omit ("5.6 sol" instead of "gpt 5.6 sol", "4.6" instead of "grok 4.6").
    private static readonly HashSet<string> FamilyPrefixes = new(StringComparer.Ordinal)
    {
        "gpt", "grok", "llama", "mistral", "deepseek", "claude", "phi", "cohere", "command", "kimi", "mai", "flux", "text", "qwen"
    };

    public static PriceSummary Match(string modelName, string modelVersion, string? format, string currency, IReadOnlyList<RetailPrice> meters)
    {
        var family = FamilyOfFormat(format);
        var modelTokens = Tokens(modelName);
        var modelVariants = modelTokens.Where(Variants.Contains).ToHashSet();
        var identity = modelTokens.Where(t => !FamilyPrefixes.Contains(t)).ToList();
        if (identity.Count == 0) identity = modelTokens;

        // Strict pass: the whole name. Loose pass: the first few meaningful tokens, for catalog names such as
        // "Llama-4-Maverick-17B-128E-Instruct-FP8" whose meter is just "Llama 4 Maverick 17B Inp regnl".
        var strict = MatchWith(identity, modelVariants, modelVersion, family, currency, meters);
        if (strict.Lines.Count > 0 || identity.Count <= 3) return strict;

        var loose = identity.Where(t => t is not ("instruct" or "fp" or "e" or "b")).Take(3).ToList();
        var result = MatchWith(loose, modelVariants, modelVersion, family, currency, meters);
        if (result.Lines.Count > 0) result.Confidence = "loose";
        return result;
    }

    private static PriceSummary MatchWith(List<string> identity, HashSet<string> modelVariants, string modelVersion,
        string family, string currency, IReadOnlyList<RetailPrice> meters)
    {
        var summary = new PriceSummary { Currency = currency };
        var versionMmdd = VersionMmdd(modelVersion);

        foreach (var meter in meters)
        {
            if (!string.Equals(FamilyOfProduct(meter.ProductName), family, StringComparison.Ordinal)) continue;
            var text = string.IsNullOrWhiteSpace(meter.SkuName) ? meter.MeterName : meter.SkuName;
            var meterTokens = Tokens(text ?? "");
            if (meterTokens.Count == 0) continue;

            // Every identity token must appear, in order.
            if (!ContainsInOrder(meterTokens, identity)) continue;

            // A meter carrying a variant the model does not have belongs to a sibling.
            if (meterTokens.Any(t => Variants.Contains(t) && !modelVariants.Contains(t))) continue;

            // Version pinning: a meter with a date token must match the model version.
            var meterDate = meterTokens.FirstOrDefault(IsDateToken);
            var dateMatched = false;
            if (meterDate is not null)
            {
                if (versionMmdd is null || !meterDate.StartsWith(versionMmdd, StringComparison.Ordinal)) continue;
                dateMatched = true;
            }

            var line = Classify(meter, meterTokens);
            line.DateMatched = dateMatched;
            summary.Lines.Add(line);
        }

        if (summary.Lines.Count == 0) return summary;

        summary.Confidence = summary.Lines.Any(l => l.DateMatched) ? "exact" : "name";
        PickHeadline(summary);
        return summary;
    }

    private static void PickHeadline(PriceSummary s)
    {
        // Preference: Global > DataZone > Regional > Unspecified; Standard tier; short context; not cached.
        static int DeploymentRank(string d) => d switch { "Global" => 0, "DataZone" => 1, "Regional" => 2, _ => 3 };
        static int TierRank(string t) => t switch { "Standard" => 0, "Flex" => 1, "Priority" => 2, "Batch" => 3, "FineTune" => 9, _ => 4 };
        static int ContextRank(string c) => c switch { "short" => 0, "any" => 1, _ => 2 };

        PriceLine? Best(string direction) => s.Lines
            .Where(l => l.Direction == direction && l.PricePer1M is not null && l.Tier != "FineTune")
            .OrderBy(l => DeploymentRank(l.Deployment)).ThenBy(l => TierRank(l.Tier)).ThenBy(l => ContextRank(l.Context))
            .FirstOrDefault();

        var input = Best("input");
        var output = Best("output");
        var cached = Best("cachedInput");

        s.InputPer1M = input?.PricePer1M;
        s.OutputPer1M = output?.PricePer1M;
        s.CachedInputPer1M = cached?.PricePer1M;
        var head = input ?? output ?? cached;
        s.HeadlineDeployment = head?.Deployment;
        s.HeadlineTier = head is null ? null : head.Tier + (head.Context == "short" ? " · short context" : head.Context == "long" ? " · long context" : "");
    }

    private static PriceLine Classify(RetailPrice meter, List<string> tokens)
    {
        var set = tokens.ToHashSet(StringComparer.Ordinal);
        var isCached = set.Contains("cd") || set.Contains("cached") || set.Contains("cache");
        var isWrite = set.Contains("wr") || set.Contains("write");
        var isInput = set.Overlaps(new[] { "inp", "inpt", "in", "input" });
        var isOutput = set.Overlaps(new[] { "outp", "opt", "outpt", "out", "output" });

        var direction = (isCached, isWrite, isInput, isOutput) switch
        {
            (true, true, _, _) => "cacheWrite",
            (true, false, _, _) => "cachedInput",
            (false, _, true, false) => "input",
            (false, _, false, true) => "output",
            _ => "other"
        };
        // A token meter with no direction word (embeddings, router) is charged on input.
        var per1M = NormalisePer1M(meter.Retail, meter.UnitOfMeasure, meter.MeterName);
        if (direction == "other" && per1M is not null) direction = "input";

        var deployment =
            set.Overlaps(new[] { "gl", "glbl", "global" }) ? "Global" :
            set.Overlaps(new[] { "dz", "dzone" }) || (set.Contains("data") && set.Contains("zone")) ? "DataZone" :
            set.Overlaps(new[] { "regnl", "regional", "reg" }) ? "Regional" : "Unspecified";

        var tier =
            set.Contains("ft") || set.Contains("finetune") || (set.Contains("fine") && set.Contains("tune")) ? "FineTune" :
            set.Contains("batch") ? "Batch" :
            set.Contains("pp") || set.Contains("priority") ? "Priority" :
            set.Contains("fl") || set.Contains("flex") ? "Flex" : "Standard";

        var context =
            set.Contains("shortco") || set.Contains("shco") ? "short" :
            set.Contains("longco") || set.Contains("lgco") ? "long" : "any";

        return new PriceLine
        {
            MeterName = meter.MeterName ?? "",
            SkuName = meter.SkuName ?? "",
            ProductName = meter.ProductName ?? "",
            MeterId = meter.MeterId,
            Direction = direction,
            Deployment = deployment,
            Tier = tier,
            Context = context,
            RetailPrice = meter.Retail,
            UnitOfMeasure = meter.UnitOfMeasure ?? "",
            PricePer1M = per1M,
        };
    }

    /// <summary>Token meters come as "1K", "1M", "1K Tokens", "1M Tokens" or occasionally per token ("1").</summary>
    public static decimal? NormalisePer1M(decimal price, string? unit, string? meterName)
    {
        var u = (unit ?? "").Trim().ToUpperInvariant();
        // Only meters that say so are token meters. "Kontext Pro glbl Images" is also billed per 1K, but per 1K images.
        var isTokenMeter = (meterName ?? "").Contains("token", StringComparison.OrdinalIgnoreCase) || u.Contains("TOKEN");
        if (!isTokenMeter) return null;

        var m = Regex.Match(u, @"^(\d+(?:\.\d+)?)\s*([KM]?)");
        if (!m.Success) return null;
        var qty = decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var mult = m.Groups[2].Value switch { "K" => 1_000m, "M" => 1_000_000m, _ => 1m };
        var perUnit = qty * mult;
        if (perUnit <= 0) return null;
        return Math.Round(price * (1_000_000m / perUnit), 6);
    }

    public static List<string> Tokens(string text)
    {
        var lower = text.ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
        return TokenRx.Matches(lower).Select(m => Alias(m.Value)).ToList();
    }

    // Meter shorthand -> catalog spelling, so "gpt img 1.5" meets "gpt-image-1.5".
    private static string Alias(string t) => t switch
    {
        "img" => "image",
        "rt" => "realtime",
        "aud" => "audio",
        _ => t
    };

    private static bool ContainsInOrder(List<string> haystack, List<string> needles)
    {
        var idx = 0;
        foreach (var n in needles)
        {
            var found = -1;
            for (var i = idx; i < haystack.Count; i++)
            {
                if (haystack[i] == n) { found = i; break; }
            }
            if (found < 0) return false;
            idx = found + 1;
        }
        return true;
    }

    private static bool IsDateToken(string t) => t.Length is 4 or 8 && t.All(char.IsDigit)
        && int.TryParse(t[..2], out var mm) && mm is >= 1 and <= 12
        && int.TryParse(t[2..4], out var dd) && dd is >= 1 and <= 31;

    private static string? VersionMmdd(string version)
    {
        // "2026-07-09" -> "0709"; "0709" or "1" -> null (no date to pin).
        if (DateTime.TryParseExact(version, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("MMdd", CultureInfo.InvariantCulture);
        return null;
    }

    public static string FamilyOfFormat(string? format) => (format ?? "").ToLowerInvariant() switch
    {
        "openai" => "openai",
        "xai" => "grok",
        "meta" => "llama",
        "mistral ai" or "mistral" => "mistral",
        "deepseek" => "deepseek",
        "anthropic" => "claude",
        "cohere" => "cohere",
        "microsoft" => "microsoft",
        "black forest labs" or "bfl" => "flux",
        "moonshot ai" or "moonshot" => "kimi",
        "alibaba" => "qwen",
        var other => other
    };

    public static string FamilyOfProduct(string? product)
    {
        var p = (product ?? "").ToLowerInvariant();
        if (p.Contains("openai")) return "openai";
        if (p.Contains("grok")) return "grok";
        if (p.Contains("llama")) return "llama";
        if (p.Contains("mistral")) return "mistral";
        if (p.Contains("deepseek")) return "deepseek";
        if (p.Contains("anthropic") || p.Contains("claude")) return "claude";
        if (p.Contains("cohere")) return "cohere";
        if (p.Contains("phi") || p.Contains("mai")) return "microsoft";
        if (p.Contains("flux") || p.Contains("bfl")) return "flux";
        if (p.Contains("kimi")) return "kimi";
        if (p.Contains("qwen")) return "qwen";
        return p;
    }
}
