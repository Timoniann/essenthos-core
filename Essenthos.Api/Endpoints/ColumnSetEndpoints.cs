using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The reader's own sets of columns, kept with the account so each of their devices offers the same
/// ones beside the reader's three modes.
///
///     GET /v1/me/column-sets    { recent, saved }
///     PUT /v1/me/column-sets    { recent?, saved? } — each list given replaces the kept one
///
/// A set is the texts of the columns, left to right, by slug. <c>recent</c> holds the sets read in
/// last, newest first, at most <see cref="Limits.RecentColumnSets"/>; <c>saved</c> the sets the reader
/// named, in their order, at most <see cref="Limits.SavedColumnSets"/>. A text named twice in one set
/// is kept once, where it was first named; a set listed twice in <c>recent</c> is kept once, where it
/// was first listed. Slugs are kept as sent and compared without regard to case. They are not checked
/// against the corpus: a text a later release lacks is passed over by the reader, not refused here,
/// so a set saved today never makes the whole list unsendable.
/// </summary>
internal static partial class ColumnSetEndpoints
{
    public static void MapColumnSets(this IEndpointRouteBuilder routes)
    {
        var sets = routes.MapGroup("/me/column-sets").RequireAuthorization();

        sets.MapGet("", async (HttpContext context, AccountsDbContext db) =>
        {
            var account = context.User.AccountId();
            var kept = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == account)
                .Select(a => new { a.RecentColumnSets, a.SavedColumnSets })
                .FirstOrDefaultAsync(context.RequestAborted);
            return kept is null ? Results.Unauthorized() : Results.Ok(Describe(kept.RecentColumnSets, kept.SavedColumnSets));
        });

        sets.MapPut("", async (HttpContext context, AccountsDbContext db, ColumnSetsPut put) =>
        {
            var recent = ReadRecent(put.Recent);
            if (recent is { Error: { } recentError })
            {
                return Results.BadRequest(new ProblemResponse(recentError));
            }

            var saved = ReadSaved(put.Saved);
            if (saved is { Error: { } savedError })
            {
                return Results.BadRequest(new ProblemResponse(savedError));
            }

            var account = context.User.AccountId();
            if (await db.Accounts.FirstOrDefaultAsync(a => a.Id == account, context.RequestAborted) is not { } kept)
            {
                return Results.Unauthorized();
            }

            if (recent?.Sets is { } recentSets && Stored(recentSets) is var recentJson && recentJson != Stored(Recent(kept.RecentColumnSets)))
            {
                kept.RecentColumnSets = recentJson;
            }

            if (saved?.Sets is { } savedSets && Stored(savedSets) is var savedJson && savedJson != Stored(Saved(kept.SavedColumnSets)))
            {
                kept.SavedColumnSets = savedJson;
            }

            await db.SaveChangesAsync(context.RequestAborted);
            return Results.Ok(Describe(kept.RecentColumnSets, kept.SavedColumnSets));
        });
    }

    /// <summary>
    /// The recent sets as the account keeps them — each set's texts once, each set once, newest first —
    /// or what is wrong with them; null for a list not sent.
    /// </summary>
    internal static (List<List<string>>? Sets, string? Error)? ReadRecent(IReadOnlyList<IReadOnlyList<string?>?>? sent)
    {
        if (sent is null)
        {
            return null;
        }

        var kept = new List<List<string>>();
        foreach (var set in sent)
        {
            if (Texts(set) is not { } texts)
            {
                return (null, TextsRule);
            }

            if (!kept.Any(other => Same(other, texts)))
            {
                kept.Add(texts);
            }
        }

        return kept.Count > Limits.RecentColumnSets
            ? (null, $"An account remembers at most {Limits.RecentColumnSets} recent sets of columns.")
            : (kept, null);
    }

    /// <summary>The saved sets as the account keeps them, in the order sent, or what is wrong with them; null for a list not sent.</summary>
    internal static (List<ColumnSet>? Sets, string? Error)? ReadSaved(IReadOnlyList<ColumnSetSent?>? sent)
    {
        if (sent is null)
        {
            return null;
        }

        var kept = new List<ColumnSet>();
        foreach (var set in sent)
        {
            var name = set?.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > Limits.ColumnSetName)
            {
                return (null, $"A set of columns is named in 1 to {Limits.ColumnSetName} characters.");
            }

            if (Texts(set!.Texts) is not { } texts)
            {
                return (null, TextsRule);
            }

            kept.Add(new ColumnSet(name, texts));
        }

        return kept.Count > Limits.SavedColumnSets
            ? (null, $"An account keeps at most {Limits.SavedColumnSets} sets of columns. Remove some to make room.")
            : (kept, null);
    }

    /// <summary>The texts of one set, each once in the order first named, or null when they cannot be a set.</summary>
    private static List<string>? Texts(IReadOnlyList<string?>? sent)
    {
        if (sent is null)
        {
            return null;
        }

        var texts = new List<string>();
        foreach (var text in sent)
        {
            var slug = text?.Trim();
            if (slug is null || slug.Length > Limits.TextSlug || !Slug().IsMatch(slug))
            {
                return null;
            }

            if (!texts.Contains(slug, StringComparer.OrdinalIgnoreCase))
            {
                texts.Add(slug);
            }
        }

        return texts.Count is > 0 and <= Limits.ChapterBookmarkTexts ? texts : null;
    }

    private static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));

    /// <summary>A list as the database keeps it; written the same way each time, so an unchanged list moves no revision.</summary>
    private static string Stored(List<List<string>> sets) => JsonSerializer.Serialize(sets, AppJsonSerializerContext.Default.ListListString);

    private static string Stored(List<ColumnSet> sets) => JsonSerializer.Serialize(sets, AppJsonSerializerContext.Default.ListColumnSet);

    private static List<List<string>> Recent(string json) =>
        Parse(json, AppJsonSerializerContext.Default.ListListString);

    private static List<ColumnSet> Saved(string json) =>
        Parse(json, AppJsonSerializerContext.Default.ListColumnSet);

    private static List<T> Parse<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<List<T>> type)
    {
        try
        {
            return JsonSerializer.Deserialize(json, type) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static ColumnSetsResponse Describe(string recent, string saved) => new(Recent(recent), Saved(saved));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Slug();

    private static readonly string TextsRule =
        $"A set of columns is 1 to {Limits.ChapterBookmarkTexts} texts, each named by its slug (KJV, NESTLE1904).";
}

internal record ColumnSetSent(string? Name, IReadOnlyList<string?>? Texts);

internal record ColumnSetsPut(IReadOnlyList<IReadOnlyList<string?>?>? Recent, IReadOnlyList<ColumnSetSent?>? Saved);

internal record ColumnSet(string Name, IReadOnlyList<string> Texts);

internal record ColumnSetsResponse(IReadOnlyList<IReadOnlyList<string>> Recent, IReadOnlyList<ColumnSet> Saved);
