using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The 613 commandments as Maimonides counted them, whole, and the ones a chapter gives.
///
/// The count is one of several, so every answer carries whose it is by carrying his numbers: a
/// reader is told <em>positive 18</em> in Maimonides' count, never <em>commandment 18</em>.
/// </summary>
internal static class CommandmentEndpoints
{
    public static void MapCommandments(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/commandments", async (AppDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await All(db, cancellationToken)));
    }

    /// <summary>Every commandment, the positive first, each with the runs of verses it rests on.</summary>
    internal static async Task<List<CommandmentResponse>> All(AppDbContext db, CancellationToken cancellationToken)
    {
        var commandments = await db.Commandments
            .OrderBy(c => c.Kind == CommandmentKinds.Negative)
            .ThenBy(c => c.Number)
            .Select(c => new
            {
                c.Kind,
                c.Number,
                c.MishnehTorahNumber,
                c.Title,
                c.Note,
                References = c.References
                    .OrderBy(r => r.Id)
                    .Select(r => new { r.CanonicalBook, r.CanonicalChapter, r.FirstVerse, r.LastVerse })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return commandments.Select(c => new CommandmentResponse(
            c.Kind,
            c.Number,
            c.MishnehTorahNumber,
            c.Title,
            c.Note,
            [
                .. c.References.Select(r => new CommandmentReferenceResponse(
                    new BookRefResponse(r.CanonicalBook, BookReferences.Name(r.CanonicalBook),
                        BookReferences.Slug(r.CanonicalBook)),
                    r.CanonicalChapter,
                    r.FirstVerse,
                    r.LastVerse)),
            ])).ToList();
    }

    /// <summary>
    /// The commandments resting on a verse of this chapter, in the order the chapter gives them:
    /// by the first of its verses they rest on, then positive before negative, then by number.
    /// </summary>
    internal static async Task<IList<ContextCommandmentResponse>> InChapter(
        AppDbContext db,
        int book,
        int chapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.CommandmentReferences
            .Where(r => r.CanonicalBook == book && r.CanonicalChapter == chapter)
            .Select(r => new { r.Commandment!.Kind, r.Commandment.Number, r.Commandment.Title, r.FirstVerse, r.LastVerse })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.GroupBy(r => (r.Kind, r.Number, r.Title))
                .Select(commandment => new ContextCommandmentResponse(
                    commandment.Key.Kind,
                    commandment.Key.Number,
                    commandment.Key.Title,
                    [
                        .. commandment
                            .SelectMany(r => Enumerable.Range(r.FirstVerse, r.LastVerse - r.FirstVerse + 1))
                            .Distinct()
                            .Order(),
                    ]))
                .OrderBy(c => c.Verses[0])
                .ThenBy(c => c.Kind == CommandmentKinds.Negative)
                .ThenBy(c => c.Number),
        ];
    }
}

/// <param name="Kind"><c>positive</c> or <c>negative</c>.</param>
/// <param name="Number">Its number among the commandments of its kind in the Sefer HaMitzvot.</param>
/// <param name="MishnehTorahNumber">The number the Mishneh Torah's own list gives it, the same but for a few.</param>
/// <param name="Title">What it commands, in Moses Hyamson's English; English only.</param>
/// <param name="Note">
/// What this project did to the verses the source prints — moved to the English numbering, or
/// supplied, and why. Null where they are as printed.
/// </param>
/// <param name="References">The runs of verses it rests on, in the shared numbering.</param>
internal record CommandmentResponse(
    string Kind,
    int Number,
    int MishnehTorahNumber,
    string Title,
    string? Note,
    IList<CommandmentReferenceResponse> References);

internal record CommandmentReferenceResponse(BookRefResponse Book, int Chapter, int FirstVerse, int LastVerse);

/// <param name="Kind"><c>positive</c> or <c>negative</c>.</param>
/// <param name="Number">Its number in Maimonides' count.</param>
/// <param name="Title">What it commands, in Moses Hyamson's English; English only.</param>
/// <param name="Verses">The verses of this chapter it rests on.</param>
internal record ContextCommandmentResponse(string Kind, int Number, string Title, IList<int> Verses);
