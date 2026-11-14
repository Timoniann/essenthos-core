using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    private readonly Lock _lock = new();

    private Task<string>? _release;

    /// <summary>The validator of every page written now from this shell.</summary>
    public async Task<PageValidity> For(ShellReading shell, CancellationToken cancellationToken)
    {
        var release = await Release().WaitAsync(cancellationToken);
        var settingsAt = settings.WrittenAt;
        var downloadsAt = downloads.WrittenAt;
        var state = string.Join('|', release, Build.Commit ?? Build.BuiltAt?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "",
            shell.Version, settingsAt.Ticks.ToString(CultureInfo.InvariantCulture), downloadsAt.Ticks.ToString(CultureInfo.InvariantCulture));
        var tag = $"\"p{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)))[..20]}\"";
        var modified = new[] { Started, Seconds(shell.ReadAt), Seconds(settingsAt), Seconds(downloadsAt) }.Max();
        return new PageValidity(tag, modified);
    }

    /// <summary>The release being served, read once: it cannot change under a running process. A failed read is tried again.</summary>
    private Task<string> Release()
    {
        lock (_lock)
        {
            if (_release is null || _release.IsFaulted || _release.IsCanceled)
            {
                _release = ReadRelease();
            }

            return _release;
        }
    }

    private async Task<string> ReadRelease()
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
        var matches = request.Headers.IfNoneMatch;
        if (matches.Count > 0)
        {
            return matches
                .SelectMany(value => (value ?? "").Split(','))
                .Select(tag => tag.Trim())
                .Any(tag => tag == "*" || (tag.StartsWith("W/", StringComparison.Ordinal) ? tag[2..] : tag) == ETag);
        }

        return request.Headers.IfModifiedSince.Count > 0
               && HeaderUtilities.TryParseDate(request.Headers.IfModifiedSince.ToString(), out var since)
               && LastModified <= since;
    }

    public void WriteTo(HttpResponse response)
    {
        response.Headers.ETag = ETag;
        response.Headers.LastModified = HeaderUtilities.FormatDate(LastModified);
    }
}
