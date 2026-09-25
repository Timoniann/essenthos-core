using System.Text.Json;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Where a browser reports what the site's Content-Security-Policy blocked, or would have blocked while
/// the policy is only reported. Each violation becomes one log line saying which directive it was and
/// what it would have stopped, and nothing else: not the page, not the reader's address, not the
/// browser, and not the query of the address that was blocked, which is where somebody's search or
/// name would be.
///
/// Browsers send two shapes. The older <c>report-uri</c> posts one <c>{"csp-report": {...}}</c> as
/// <c>application/csp-report</c>; the Reporting API that <c>report-to</c> names posts an array of
/// <c>{"type": "csp-violation", "body": {...}}</c> as <c>application/reports+json</c>. Both are read,
/// since the policy names both and each browser uses the one it knows.
/// </summary>
internal static class CspReportEndpoints
{
    public const string Route = "/csp-reports";

    /// <summary>A report is a few hundred bytes; the Reporting API batches a handful into one post.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>Beyond this a batch is logged by its first entries: the rest say the same.</summary>
    public const int MaxViolationsPerPost = 20;

    private const int MaxDirectiveLength = 64;

    private const int MaxBlockedLength = 200;

    private static readonly string[] MediaTypes = ["application/csp-report", "application/reports+json", "application/json"];

    public static void MapCspReports(this IEndpointRouteBuilder routes)
    {
        routes.MapPost(Route, async (HttpContext context, ILoggerFactory loggers) =>
        {
            if (!MediaTypes.Contains(context.Request.ContentType?.Split(';')[0].Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }

            if (context.Request.ContentLength > MaxBodyBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            var body = new byte[MaxBodyBytes + 1];
            var length = 0;
            int read;
            while (length < body.Length &&
                   (read = await context.Request.Body.ReadAsync(body.AsMemory(length), context.RequestAborted)) > 0)
            {
                length += read;
            }

            if (length > MaxBodyBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            IReadOnlyList<CspViolation> violations;
            try
            {
                violations = Read(body.AsSpan(0, length));
            }
            catch (JsonException)
            {
                return Results.BadRequest();
            }

            var logger = loggers.CreateLogger(typeof(CspReportEndpoints));
            foreach (var violation in violations)
            {
                logger.LogWarning("Content-Security-Policy {Disposition} {Directive}: {Blocked}",
                    violation.Disposition, violation.Directive, violation.Blocked);
            }

            return Results.NoContent();
        }).RequireRateLimiting(RateLimits.Reports);
    }

    /// <summary>The violations in a post of either shape, reduced to what is logged.</summary>
    public static IReadOnlyList<CspViolation> Read(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var violations = new List<CspViolation>();

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("csp-report", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
        {
            violations.Add(Violation(
                String(legacy, "effective-directive") ?? String(legacy, "violated-directive"),
                String(legacy, "blocked-uri"),
                String(legacy, "disposition")));
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var report in root.EnumerateArray())
            {
                if (violations.Count == MaxViolationsPerPost)
                {
                    break;
                }

                if (report.ValueKind == JsonValueKind.Object &&
                    String(report, "type") == "csp-violation" &&
                    report.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
                {
                    violations.Add(Violation(
                        String(body, "effectiveDirective"),
                        String(body, "blockedURL"),
                        String(body, "disposition")));
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// What was blocked, as an address without its query, fragment or credentials, or as the keyword a
    /// browser writes for what has no address (<c>inline</c>, <c>eval</c>, <c>data</c>, <c>blob</c>).
    /// </summary>
    public static string Blocked(string? blocked)
    {
        if (string.IsNullOrWhiteSpace(blocked))
        {
            return "(none)";
        }

        if (Uri.TryCreate(blocked, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "ws" or "wss")
        {
            return Clip(uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped),
                MaxBlockedLength);
        }

        var keyword = blocked.Split(':')[0].Trim().ToLowerInvariant();
        return Plain(keyword) ? Clip(keyword, MaxDirectiveLength) : "(unreadable)";
    }

    private static CspViolation Violation(string? directive, string? blocked, string? disposition) =>
        new(Keyword(disposition) ?? "report", Keyword(directive) ?? "(none)", Blocked(blocked));

    /// <summary>A directive or disposition, which is lower-case letters and hyphens, or null for anything else.</summary>
    private static string? Keyword(string? value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return trimmed is { Length: > 0 and <= MaxDirectiveLength } && Plain(trimmed) ? trimmed : null;
    }

    private static bool Plain(string value) =>
        value.Length > 0 && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '+' or '.');

    private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>One violation as it is logged: nothing in it names the reader or the page.</summary>
internal sealed record CspViolation(string Disposition, string Directive, string Blocked);
