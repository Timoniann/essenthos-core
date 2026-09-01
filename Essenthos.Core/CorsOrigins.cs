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
    /// The essenthos-web dev server. It stays on 5278 while the frozen API holds 5277 and this
    /// one holds 5279, so all three run at once.
    /// </summary>
    private static readonly string[] DevelopmentDefaults = ["http://localhost:5278"];

    public static string[] Read(IConfiguration configuration)
    {
        var configured = configuration.GetSection(ConfigurationKey).Get<string[]>();
        return configured is { Length: > 0 } ? configured : DevelopmentDefaults;
    }
}
