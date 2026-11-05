using System.Text.RegularExpressions;

namespace Essenthos.Core.Publishing;

/// <summary>
/// Which release dumps a target's <c>releases</c> folder keeps once a publication is live, and which go.
///
/// Every publication uploads a dump of a gigabyte or more and the restore no longer needs it once the
/// release is in place, so without this the folder grows by one dump per publication until the disk is
/// full. The rule, in order:
/// <list type="number">
/// <item>Nothing is removed unless the target's live corpus says it is the release just published, the
/// dump of that release is in the folder, and this machine's ledger names the release the target served
/// before it. Where the target keeps that release as a copy of its own, the copy must say the same.
/// Otherwise every file stays and the reason is given.</item>
/// <item>The dump of the live release stays, and so does the dump of the one served before it, which a
/// rollback publishes again without uploading it a second time.</item>
/// <item>A dump named after both stays: an upload a later publication has not finished, which it resumes.</item>
/// <item>Every other release dump goes.</item>
/// <item>A file not named like a release dump (<c>yyyymmdd</c>, a letter, <c>.dump</c>) is never touched.</item>
/// </list>
/// </summary>
internal static partial class ReleaseUploads
{
    public const string Extension = ".dump";

    /// <summary>What <c>forge release</c> names a release: the day it was made and a letter.</summary>
    [GeneratedRegex("^[0-9]{8}[a-z]$")]
    private static partial Regex ReleaseName();

    public static bool IsReleaseName(string? name) => name is not null && ReleaseName().IsMatch(name);

    /// <summary>The release a dump in the folder is of, or null for a file that is not a release dump.</summary>
    public static string? ReleaseOf(string file) =>
        file.EndsWith(Extension, StringComparison.Ordinal) && file[..^Extension.Length] is var name && IsReleaseName(name)
            ? name
            : null;

    /// <summary>
    /// The release <paramref name="target"/> served before <paramref name="live"/>: the latest one this
    /// machine published to it that is not <paramref name="live"/>. Null when there is none.
    /// </summary>
    public static string? Previous(IEnumerable<(string Release, DateTimeOffset At)> ledger, string live) =>
        ledger.OrderBy(entry => entry.At).Select(entry => entry.Release).LastOrDefault(release => release != live);

    /// <param name="files">The file names in the target's <c>releases</c> folder, without a path.</param>
    /// <param name="published">The release this publication put in place.</param>
    /// <param name="serving">What the target's live corpus says it is, read back after the publication.</param>
    /// <param name="previous">The release the target served before, from this machine's ledger.</param>
    /// <param name="keepsPrevious">Whether the target holds the release before the live one as a copy.</param>
    /// <param name="previousOnTarget">What that copy says it is, where the target keeps one.</param>
    public static Plan Choose(
        IReadOnlyCollection<string> files, string published, string? serving, string? previous,
        bool keepsPrevious = false, string? previousOnTarget = null)
    {
        if (!IsReleaseName(published))
        {
            return Plan.Refused(files, $"\"{published}\" is not a release name");
        }

        if (serving != published)
        {
            return Plan.Refused(files,
                $"the live corpus says it is {(string.IsNullOrEmpty(serving) ? "no release" : $"release {serving}")}, not {published}, so which release is live cannot be told");
        }

        if (!files.Contains(published + Extension))
        {
            return Plan.Refused(files, $"{published}{Extension} is not among the files listed, so the listing may not be of the folder the release was uploaded to");
        }

        if (previous is null)
        {
            return Plan.Refused(files, $"this machine's ledger names no release served before {published}, so which dump a rollback needs cannot be told");
        }

        if (!IsReleaseName(previous))
        {
            return Plan.Refused(files, $"the ledger's release before {published} is \"{previous}\", which is not a release name");
        }

        if (keepsPrevious && previousOnTarget != previous)
        {
            return Plan.Refused(files,
                $"the ledger says {previous} was served before {published}, but the previous copy on the target says {(string.IsNullOrEmpty(previousOnTarget) ? "nothing" : previousOnTarget)}");
        }

        var newest = string.CompareOrdinal(published, previous) > 0 ? published : previous;
        var keep = new List<Kept>();
        var remove = new List<string>();
        var untouched = new List<string>();
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            var release = ReleaseOf(file);
            if (release is null)
            {
                untouched.Add(file);
            }
            else if (release == published)
            {
                keep.Add(new Kept(file, "live"));
            }
            else if (release == previous)
            {
                keep.Add(new Kept(file, "served before, for a rollback"));
            }
            else if (string.CompareOrdinal(release, newest) > 0)
            {
                keep.Add(new Kept(file, "newer than the live release, not yet published"));
            }
            else
            {
                remove.Add(file);
            }
        }

        return new Plan(keep, remove, untouched, previous, Refusal: null);
    }

    public sealed record Kept(string File, string Why);

    /// <summary>
    /// What stays and what goes. A refused plan removes nothing and says why; <see cref="Untouched"/> are
    /// the files that are not release dumps, which no plan removes.
    /// </summary>
    public sealed record Plan(
        IReadOnlyList<Kept> Keep, IReadOnlyList<string> Remove, IReadOnlyList<string> Untouched, string? Previous, string? Refusal)
    {
        public static Plan Refused(IReadOnlyCollection<string> files, string why) =>
            new(files.Order(StringComparer.Ordinal).Select(file => new Kept(file, "nothing is removed")).ToList(), [], [], null, why);
    }
}
