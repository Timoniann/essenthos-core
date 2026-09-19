namespace Essenthos.Core.Publishing;

/// <summary>
/// Somewhere a corpus release can be published: a Postgres in a container, on this machine or behind
/// ssh, and the names the corpus goes by there.
///
/// Read from <c>Publish:Targets:&lt;name&gt;</c>. Everything here is an address rather than a
/// secret, and is tracked; the one secret, the password the gate connects with, is read from user
/// secrets or the environment like every other.
/// </summary>
/// <param name="Name">What <c>--to</c> takes: <c>dev</c>, <c>prod</c>, <c>rehearsal</c>.</param>
/// <param name="Local">
/// A target on this machine — the rehearsal of the server — which is how the whole publication is run
/// end to end without one. Declared rather than inferred from an empty <see cref="Ssh"/>, so that a
/// server whose address was never configured is refused instead of quietly published to locally.
/// </param>
/// <param name="Ssh"><c>user@host</c> for a remote target.</param>
/// <param name="DataRoot">
/// The directory on the target that holds its persistent state. Releases are uploaded into its
/// <c>releases</c> folder, which the database container mounts at <c>/releases</c>.
/// </param>
/// <param name="Container">The database container, by name.</param>
/// <param name="Database">
/// The name the API reads. The previous release is kept beside it as <c>&lt;name&gt;_previous</c> and
/// an incoming one is restored as <c>&lt;name&gt;_incoming</c>, so nothing ever reads a corpus that is
/// still arriving.
/// </param>
/// <param name="Reader">The API's role, which is granted <c>SELECT</c> and nothing else.</param>
/// <param name="Superuser">The role restores and swaps run as, over the container's local socket.</param>
/// <param name="DatabasePort">
/// The port the database listens on at the target's loopback, which the gate reaches through an ssh
/// tunnel, or directly for a target on this machine.
/// </param>
/// <param name="ApiContainer">
/// Restarted after a swap, because the API keeps some of the corpus in memory — the canonical frame
/// among it — and a process holding the old release's would serve a mixture.
/// </param>
/// <param name="After">
/// A target this one only accepts releases from once they have been published there. Production names
/// dev, so production receives the identical file dev accepted, or dev proved nothing.
/// </param>
/// <param name="AppDatabase">
/// The environment's accounts database, whose bookmarks must all still find their verse in a release
/// before it is swapped in. Unset for a target that has none.
/// </param>
internal sealed record ReleaseTarget(
    string Name,
    bool Local,
    string Ssh,
    string DataRoot,
    string Container,
    string Database,
    string Reader,
    string Superuser,
    int DatabasePort,
    string ApiContainer,
    string? After,
    string? Password,
    string? AppDatabase = null)
{
    public bool IsRemote => !Local;

    public string Incoming => $"{Database}_incoming";

    public string Previous => $"{Database}_previous";

    /// <summary>Where a release waits while a swap it lost to is undone. Never read.</summary>
    public string Retired => $"{Database}_retired";

    /// <summary>
    /// A local target's superuser password, read from the same untracked .env its compose file was
    /// started with, so the rehearsal has one copy of each secret rather than two that can disagree.
    /// </summary>
    private static string? FromEnvFile(string? file, string repository)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(Path.Combine(repository, file)))
        {
            return null;
        }

        return File.ReadLines(Path.Combine(repository, file))
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Trim() == "POSTGRES_PASSWORD")
            .Select(pair => pair[1].Trim().Trim('\'', '"'))
            .FirstOrDefault();
    }

    /// <param name="repository">The checkout root, which a local target's relative paths are read against.</param>
    public static ReleaseTarget Read(IConfiguration configuration, string name, string repository)
    {
        var section = configuration.GetSection($"Publish:Targets:{name}");
        if (!section.Exists())
        {
            var known = configuration.GetSection("Publish:Targets").GetChildren().Select(c => c.Key).ToList();
            throw new InvalidOperationException(
                $"No publish target \"{name}\". Known: {(known.Count == 0 ? "none" : string.Join(", ", known))}. " +
                "Targets are declared under Publish:Targets in Essenthos.Forge/appsettings.json.");
        }

        string Required(string key) => section[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Publish:Targets:{name}:{key} is not set.");

        var local = bool.TryParse(section["Local"], out var isLocal) && isLocal;
        var ssh = section["Ssh"] ?? string.Empty;
        if (!local && ssh.Length == 0)
        {
            throw new InvalidOperationException(
                $"Publish target \"{name}\" has no address. Set it with " +
                $"`dotnet user-secrets set \"Publish:Targets:{name}:Ssh\" \"user@host\" --project Essenthos.Forge`.");
        }

        var dataRoot = Required("DataRoot");
        if (local && !Path.IsPathRooted(dataRoot))
        {
            dataRoot = Path.GetFullPath(Path.Combine(repository, dataRoot));
        }

        return new ReleaseTarget(
            name,
            local,
            ssh,
            dataRoot,
            Required("Container"),
            Required("Database"),
            Required("Reader"),
            section["Superuser"] is { Length: > 0 } superuser ? superuser : "postgres",
            int.Parse(Required("DatabasePort"), System.Globalization.CultureInfo.InvariantCulture),
            Required("ApiContainer"),
            section["After"] is { Length: > 0 } after ? after : null,
            section["Password"] is { Length: > 0 } password ? password : FromEnvFile(section["EnvFile"], repository),
            section["AppDatabase"] is { Length: > 0 } app ? app : null);
    }
}
