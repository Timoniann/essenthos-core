using Essenthos.Core.Endpoints;

namespace Essenthos.Core.Accounts;

/// <summary>
/// A change carried by the session cookie must also carry a header only a script can set, and only
/// a script on an origin CORS lets through: the site's own pages. SameSite=Lax withholds the cookie
/// from another site's form, but every subdomain of the site is the same site, so a page on one of
/// them could still post to the routes that take no body — signing out, signing a device out. A
/// form cannot set a header, and a script on another origin that sets one is stopped at the preflight.
///
/// A request signed with <c>Authorization: Bearer</c> is not asked: no browser attaches that on its
/// own. Nor is a browser's Content-Security-Policy report, which is sent by the browser and never
/// changes an account.
/// </summary>
internal static class ChangeHeader
{
    public const string Name = "X-Requested-With";

    private const string CspReports = "/v1" + CspReportEndpoints.Route;

    private const string RefusalMessage =
        "A change sent with the session cookie must carry the X-Requested-With header, as the site's own " +
        "pages send it. A program should send its token as Authorization: Bearer instead.";

    public static bool Refuses(HttpRequest request) =>
        !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
          HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method))
        && request.Cookies.ContainsKey(SessionTokens.CookieName)
        && !request.Headers.ContainsKey(Name)
        && !request.Path.StartsWithSegments(CspReports);

    public static IApplicationBuilder UseChangeHeader(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (Refuses(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(RefusalMessage, context.RequestAborted);
                return;
            }

            await next(context);
        });
}
