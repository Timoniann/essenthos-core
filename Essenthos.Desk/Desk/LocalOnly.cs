using System.Net;

namespace Essenthos.Core.Desk;

/// <summary>
/// The console answers its owner's browser and nothing else. It listens on the loopback only, so
/// nothing off this machine reaches it; a request whose Host is not the loopback at this port is
/// refused, so a web page cannot reach it through a name rebound to 127.0.0.1; and a request
/// carrying an Origin other than the console's own is refused, so a page open in another tab
/// cannot answer a question or start a run in the owner's name.
/// </summary>
internal sealed class LocalOnly(IReadOnlyCollection<string> origins)
{
    private static readonly string[] LoopbackNames = ["localhost", "127.0.0.1", "[::1]"];

    private const string CrossSite = "cross-site";

    /// <summary>
    /// Refuses to start on any address but the loopback. A console that answers questions and starts
    /// loads in the owner's name must never be reachable from the network, whatever somebody typed.
    /// </summary>
    public static void RequireLoopback(IConfiguration configuration)
    {
        var urls = configuration["Urls"];
        if (string.IsNullOrWhiteSpace(urls))
        {
            return;
        }

        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsLoopback(uri.Host))
            {
                throw new InvalidOperationException(
                    $"The console was asked to listen on {url}, which is not this machine's loopback. It only ever " +
                    "listens on 127.0.0.1 or localhost; set Urls to http://127.0.0.1:<port>.");
            }
        }
    }

    public async Task Guard(HttpContext context, RequestDelegate next)
    {
        var refusal = Refusal(context.Request, context.Connection.LocalPort);
        if (refusal is not null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(refusal);
            return;
        }

        await next(context);
    }

    /// <summary>Why a request is refused, or null where it is the console's own.</summary>
    internal string? Refusal(HttpRequest request, int port)
    {
        var host = request.Host;
        if (!host.HasValue || !IsLoopback(host.Host) || (host.Port ?? 80) != port)
        {
            return $"The console answers only requests addressed to this machine's loopback at port {port}. " +
                   "Open it at http://127.0.0.1:" + port + " or through its own web console.";
        }

        if (string.Equals(request.Headers["Sec-Fetch-Site"], CrossSite, StringComparison.OrdinalIgnoreCase))
        {
            return "The console does not answer another site's page. Open the console itself.";
        }

        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !IsOwnOrigin(origin, host.ToString()))
        {
            return $"The console does not answer a page from {origin}. Open the console itself, or add that " +
                   "address to Desk:Origins if it is the console's own web page on another port.";
        }

        return null;
    }

    private bool IsOwnOrigin(string origin, string host) =>
        origins.Contains(origin, StringComparer.OrdinalIgnoreCase)
        || string.Equals(origin, $"http://{host}", StringComparison.OrdinalIgnoreCase);

    private static bool IsLoopback(string host) =>
        LoopbackNames.Contains(host, StringComparer.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
