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
/// <c>releases</c> folder, which the database container mounts at <c>/releases</c> and from which a
/// publication removes the dumps no longer needed (<see cref="ReleaseUploads"/>), and the pictures
/// into <c>images/&lt;database&gt;</c>, which the API container mounts at <c>/images</c>.
/// </param>
/// <param name="Container">The database container, by name.</param>
/// <param name="Database">
/// The name the API reads. An incoming release is restored as <c>&lt;name&gt;_incoming</c>, so nothing
/// ever reads a corpus that is still arriving, and on a target that keeps three copies the previous one
/// stays beside it as <c>&lt;name&gt;_previous</c>.
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
/// A target this one only accepts releases from once they have been published there, so it receives the
/// identical file that target accepted. Production names the rehearsal, the server's stack on the
/// workstation. A release this target has itself served before is accepted without it.
/// </param>
/// <param name="AppDatabase">
/// The environment's accounts database, whose bookmarks must all still find their verse in a release
/// before it is swapped in. Unset for a target that has none.
/// </param>
/// <param name="Downloads">
/// The folder under the data root that holds the files <c>forge export</c> wrote, which the target's
/// proxy serves read-only at <c>/downloads</c> and its API lists from. Unset, it is <c>downloads</c>, or
/// <c>downloads_dev</c> for a corpus whose database name ends <c>_dev</c>, beside the pictures.
/// </param>
/// <param name="Copies">
/// The most corpus databases the target ever holds. 3, the default: the live one, the previous one
/// kept for <c>forge rollback</c>, and an incoming one restored beside them. 2: live and incoming, and
/// the replaced one is dropped after the swap. 1: the live one is dropped and the incoming one restored
/// in its place while the site shows its maintenance page. Below 3 a rollback is a publication of the
/// earlier release from this machine.
/// </param>
/// <param name="RestoreJobs">
/// How many jobs <c>pg_restore</c> runs at once, each building an index with up to the target's
/// <c>maintenance_work_mem</c>. 4 by default; a server whose Postgres has a small memory limit takes
/// fewer, because a restore killed for memory on a one-copy target leaves it with no corpus.
/// </param>
/// <param name="Maintenance">
/// The file under the data root whose presence makes the target's proxy answer with the maintenance
/// page, which a one-copy publication puts there while the target has no corpus.
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
    string? AppDatabase = null,
    string? Downloads = null,
    int Copies = ReleaseTarget.MostCopies,
    string Maintenance = ReleaseTarget.DefaultMaintenance,
    int RestoreJobs = ReleaseTarget.DefaultRestoreJobs)
{
    public const int MostCopies = 3;

    public const string DefaultMaintenance = "maintenance/on";

    public const int DefaultRestoreJobs = 4;

    /// <summary>Whether the target keeps the release before the live one, so a rollback is a rename.</summary>
    public bool KeepsPrevious => Copies >= MostCopies;

    public string DownloadsName => Downloads is { Length: > 0 }
        ? Downloads
        : Database.EndsWith("_dev", StringComparison.Ordinal) ? "downloads_dev" : "downloads";

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
            section["AppDatabase"] is { Length: > 0 } app ? app : null,
            section["Downloads"] is { Length: > 0 } downloads ? downloads : null,
            ReadCopies(section, name),
            section["Maintenance"] is { Length: > 0 } maintenance ? maintenance.Trim('/', '\\') : DefaultMaintenance,
            ReadRestoreJobs(section, name));
    }

    private static int ReadRestoreJobs(IConfigurationSection section, string name) =>
        section["RestoreJobs"] is not { Length: > 0 } jobs
            ? DefaultRestoreJobs
            : int.TryParse(jobs, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count)
              && count is >= 1 and <= 16
                ? count
                : throw new InvalidOperationException(
                    $"Publish:Targets:{name}:RestoreJobs is \"{jobs}\"; it is how many jobs pg_restore runs at once, 1 to 16.");

    private static int ReadCopies(IConfigurationSection section, string name)
    {
        if (section["Copies"] is not { Length: > 0 } copies)
        {
            return MostCopies;
        }

        return int.TryParse(copies, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count)
               && count is >= 1 and <= MostCopies
            ? count
            : throw new InvalidOperationException(
                $"Publish:Targets:{name}:Copies is \"{copies}\"; it takes 1 (the live corpus only, replaced while the " +
                "site shows its maintenance page), 2 (live and incoming) or 3 (live, previous and incoming).");
    }
}
