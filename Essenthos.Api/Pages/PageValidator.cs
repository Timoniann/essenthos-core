using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Essenthos.Core.Pages;

/// <summary>
/// What a written page is a function of, as one validator: the corpus release, this API's build, the
/// reader's shell and wording, and the owner's switches and the downloads list, which change without a
/// restart. Every page of one such state carries the same <c>ETag</c> and <c>Last-Modified</c>, and a
/// crawler that asks again with them is answered 304 before anything is read from the database.
///
/// <para>
/// Nothing is kept: the validator only tells the truth about when a page last could have changed. A
/// working copy has no release row, so there the process's own start stands for it; a new corpus is
/// always a restart. <c>Last-Modified</c> is never earlier than the process's start, so a date given by
/// an older build can never be taken for this one's.
/// </para>
/// </summary>
internal sealed class PageValidator(IServiceScopeFactory scopes, SiteSettingsFile settings, DownloadsCatalogue downloads)
{
    private static readonly DateTimeOffset Started = Seconds(DateTimeOffset.UtcNow);

    private readonly PerRelease<string> _release = new(_ => ReadRelease(scopes));

    /// <summary>
    /// The validator of every page written now from this shell; a record's page also names its place's picture, which is drawn
    /// without a release, so a page whose picture was drawn or redrawn is not taken for the one a crawler holds.
    /// </summary>
    public async Task<PageValidity> For(ShellReading shell, PlacePicture? picture, CancellationToken cancellationToken)
    {
        var release = await _release.Get(cancellationToken);
        var settingsAt = settings.WrittenAt;
        var downloadsAt = downloads.WrittenAt;
        string[] state = [release, Built, shell.Version, settingsAt.Ticks.ToString(CultureInfo.InvariantCulture),
            downloadsAt.Ticks.ToString(CultureInfo.InvariantCulture)];
        var tag = Tag('p', picture is null ? state : [.. state, picture.Tag]);
        var modified = new[]
        {
            Started, Seconds(shell.ReadAt), Seconds(settingsAt), Seconds(downloadsAt), Seconds(picture?.WrittenAt ?? DateTime.MinValue),
        }.Max();
        return new PageValidity(tag, modified);
    }

    /// <summary>
    /// The validator of an API answer that is a function of the corpus release, this API's build and the owner's switches, and
    /// of whatever else the caller names that changes without a release (the address asked for, a place's picture): a quoted
    /// <c>ETag</c>, the same for every answer of one such state. Only the first call of a process reads the database.
    /// </summary>
    public async Task<string> AnswerTag(char kind, IEnumerable<string?> parts, CancellationToken cancellationToken)
    {
        var release = await _release.Get(cancellationToken);
        return Tag(kind, [release, Built, settings.WrittenAt.Ticks.ToString(CultureInfo.InvariantCulture), .. parts.Select(part => part is null ? "-" : "=" + part)]);
    }

    private static string Built => Build.Commit ?? Build.BuiltAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "";

    private static string Tag(char kind, params string[] state) =>
        $"\"{kind}{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', state))))[..20]}\"";

    /// <summary>The release being served, read once: it cannot change under a running process.</summary>
    private static async Task<string> ReadRelease(IServiceScopeFactory scopes)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var release = await db.CorpusReleases.AsNoTracking()
            .OrderByDescending(r => r.BuiltAt)
            .Select(r => new { r.Name, r.ManifestSha })
            .FirstOrDefaultAsync(CancellationToken.None);
        return release is null ? $"working:{Started.UtcTicks}" : $"{release.Name}:{release.ManifestSha}";
    }

    private static DateTimeOffset Seconds(DateTimeOffset at) =>
        new(at.UtcTicks - (at.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    private static DateTimeOffset Seconds(DateTime utc) =>
        utc == DateTime.MinValue ? DateTimeOffset.MinValue : Seconds(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
}

/// <param name="ETag">Quoted, as the header carries it.</param>
internal sealed record PageValidity(string ETag, DateTimeOffset LastModified)
{
    /// <summary>
    /// Whether the client already holds this page as it would be written now: an <c>If-None-Match</c>
    /// naming this tag (compared weakly, since a proxy that compresses the page marks its tag weak), or,
    /// with none, an <c>If-Modified-Since</c> no earlier than the last change.
    /// </summary>
    public bool HeldBy(HttpRequest request)
    {
        if (request.Headers.IfNoneMatch.Count > 0)
        {
            return Names(request, ETag, star: true);
        }

        return request.Headers.IfModifiedSince.Count > 0
               && HeaderUtilities.TryParseDate(request.Headers.IfModifiedSince.ToString(), out var since)
               && LastModified <= since;
    }

    /// <summary>
    /// Whether the request's <c>If-None-Match</c> names this tag, compared weakly, since a proxy that compresses an answer
    /// marks its tag weak; with <paramref name="star"/>, also where it asks for any (<c>*</c>), which only an address known
    /// to exist may answer.
    /// </summary>
    public static bool Names(HttpRequest request, string tag, bool star = false) =>
        request.Headers.IfNoneMatch
            .SelectMany(value => (value ?? "").Split(','))
            .Select(held => held.Trim())
            .Any(held => (star && held == "*") || (held.StartsWith("W/", StringComparison.Ordinal) ? held[2..] : held) == tag);

    public void WriteTo(HttpResponse response)
    {
        response.Headers.ETag = ETag;
        response.Headers.LastModified = HeaderUtilities.FormatDate(LastModified);
    }
}
