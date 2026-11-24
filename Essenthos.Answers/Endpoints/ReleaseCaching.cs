namespace Essenthos.Core.Endpoints;

/// <summary>
/// Answers only a new corpus release changes — the books, the texts, the canons, the datasets, the
/// kings' timeline — which every page asks for. A browser keeps them for five minutes and, for a day
/// after that, shows the copy it has at once while it asks again behind it; so a reader who opens a
/// second page from a search result does not download them again, and the first page after a release
/// that changed them is the only one that shows the old ones.
/// </summary>
internal static class ReleaseCaching
{
    public const string CacheControl = "public, max-age=300, stale-while-revalidate=86400";

    /// <summary>Marks a successful answer of this endpoint as kept by the browser; a refusal or an error is not.</summary>
    public static RouteHandlerBuilder KeptUntilRelease(this RouteHandlerBuilder endpoint) =>
        endpoint.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            if (result is not IStatusCodeHttpResult { StatusCode: >= 300 })
            {
                context.HttpContext.Response.Headers.CacheControl = CacheControl;
            }

            return result;
        });
}
