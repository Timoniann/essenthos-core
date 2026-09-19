namespace Essenthos.Core;

/// <summary>
/// Which origins the browser may call this API from. Hardcoding one meant the API only worked
/// against the machine it was written on; the default below is the web project's dev server, and a
/// deployment overrides it with <c>Cors:AllowedOrigins</c>.
/// </summary>
internal static class CorsOrigins
{
    public const string ConfigurationKey = "Cors:AllowedOrigins";

    /// <summary>
    /// The essenthos-web dev server, and the block of ports a worktree preview takes.
    ///
    /// 5278 is the client; the frozen API holds 5277 and this one 5279, so all three run at once.
    /// The rest are for previewing a branch beside the running client, which several agents do at a
    /// time — and a preview that cannot read the corpus is a branch nobody looks at before merging
    /// it. That is not hypothetical: the Go To work shipped with its dropdown never once opened in a
    /// browser, because the only origin allowed was the one it was not served from.
    ///
    /// A range rather than every localhost origin, because a deployment that forgets to configure
    /// this should fall back to something bounded rather than to a policy.
    /// </summary>
    private static readonly string[] DevelopmentDefaults =
    [
        "http://localhost:5278",
        .. Enumerable.Range(5280, 10).Select(port => $"http://localhost:{port}"),
    ];

    public static string[] Read(IConfiguration configuration)
    {
        var configured = configuration.GetSection(ConfigurationKey).Get<string[]>();
        return configured is { Length: > 0 } ? configured : DevelopmentDefaults;
    }
}
