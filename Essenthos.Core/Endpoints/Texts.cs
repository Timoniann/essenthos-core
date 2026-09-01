using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The texts the corpus holds, and the reading of one chapter of one of them.
///
/// The old contract calls a text a corpus and divides them into originals and translations. There
/// is no such division in the model any more — a text's role belongs to its relations — so the
/// mapping here is one-way and lossy on purpose: it exists to keep the old shape answering while
/// the client moves, and it is the one place that still says "original".
/// </summary>
internal static class Texts
{
    private const string OriginalKind = "original";
    private const string TranslationKind = "translation";

    /// <summary>
    /// A word of BHSA carries these under its own names; the old contract has a field for each.
    /// Everything else the annotation holds stays in the database and is reached by the new shapes.
    /// </summary>
    private const string PartOfSpeech = "pos";
    private const string Language = "language";
    private const string Phono = "phono";
    private const string PhonoTrailer = "phonoTrailer";

    public static string KindOf(TextKind kind) =>
        kind == TextKind.Translation ? TranslationKind : OriginalKind;

    public static CorpusResponse Corpus(Text text, CoverageResponse coverage, bool hasWordMapping) => new(
        text.Slug,
        text.Name,
        KindOf(text.Kind),
        text.Language,
        text.Direction == TextDirection.RightToLeft ? "rtl" : "ltr",
        hasWordMapping,
        text.Licence,
        text.TextualFamily,
        text.Versification.ToString(),
        text.PublishedYear,
        coverage);

    /// <summary>
    /// Reads one chapter as this text numbers it. The words come back in one query projected into
    /// the shape returned, because a chapter is a thousand words and a query per word is a thousand
    /// round trips.
    /// </summary>
    public static async Task<IList<TextVerseResponse>> ReadChapter(
        AppDbContext db,
        int textId,
        int bookOrdinal,
        int chapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.Words
            .Where(w => w.TextId == textId
                        && w.Verse!.Book!.CanonicalOrdinal == bookOrdinal
                        && w.Verse.ChapterNumber == chapter)
            .OrderBy(w => w.Verse!.Number).ThenBy(w => w.Position)
            .Select(w => new WordRow(
                w.Verse!.Number, w.Id, w.Surface, w.Trailer, w.Gloss, w.Lemma, w.StrongNumber, w.Morphology))
            .ToListAsync(cancellationToken);

        return Group(rows);
    }

    /// <summary>Reads the verses of one text that sit at the given canonical addresses.</summary>
    public static async Task<Dictionary<int, List<TextWordResponse>>> ReadByCanonicalVerse(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary
                        && r.Verse!.TextId == textId
                        && r.CanonicalBook == canonicalBook
                        && r.CanonicalChapter == canonicalChapter)
            .SelectMany(r => r.Verse!.Words.Select(w => new CanonicalWordRow(
                r.CanonicalVerse, r.Verse.Number, w.Position, w.Id, w.Surface, w.Trailer, w.Gloss, w.Lemma,
                w.StrongNumber, w.Morphology)))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.CanonicalVerse)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(r => r.VerseNumber).ThenBy(r => r.Position)
                    .Select(r => Word(r.Id, r.Text, r.Trailer, r.Gloss, r.Lemma, r.StrongNumber, r.Morphology))
                    .ToList());
    }

    private static IList<TextVerseResponse> Group(List<WordRow> rows) =>
        rows.GroupBy(r => r.VerseNumber)
            .Select(group => new TextVerseResponse(
                group.Key,
                group.Select(r => Word(r.Id, r.Text, r.Trailer, r.Gloss, r.Lemma, r.StrongNumber, r.Morphology))
                    .ToList()))
            .ToList();

    private static TextWordResponse Word(
        long id,
        string text,
        string trailer,
        string? gloss,
        string? lemma,
        string? strongNumber,
        JsonDocument? morphology)
    {
        var features = Features(morphology);
        return new TextWordResponse(
            id,
            text,
            trailer,
            gloss,
            lemma,
            strongNumber,
            [],
            null,
            null,
            Morphology(features),
            Feature(features, Phono),
            Feature(features, PhonoTrailer),
            Feature(features, Language));
    }

    private static JsonElement? Features(JsonDocument? morphology) => morphology?.RootElement;

    private static string? Feature(JsonElement? features, string name) =>
        features is { } element && element.TryGetProperty(name, out var value) ? value.GetString() : null;

    /// <summary>
    /// The annotation, as much of it as the old contract has a field for. A word with none — every
    /// word of every translation — answers null rather than an object of nulls.
    /// </summary>
    private static MorphologyResponse? Morphology(JsonElement? features)
    {
        if (features is null || Feature(features, PartOfSpeech) is null)
        {
            return null;
        }

        return new MorphologyResponse(
            Feature(features, PartOfSpeech),
            Feature(features, "gender"),
            Feature(features, "number"),
            Feature(features, "person"),
            Feature(features, "state"),
            Feature(features, "stem"),
            Feature(features, "tense"),
            Feature(features, "lexicalSet"),
            Feature(features, "phrasePos"),
            Feature(features, "suffixGender"),
            Feature(features, "suffixNumber"),
            Feature(features, "suffixPerson"),
            Feature(features, "nameType")?.Split(','));
    }

    private sealed record WordRow(
        int VerseNumber, long Id, string Text, string Trailer, string? Gloss, string? Lemma, string? StrongNumber,
        JsonDocument? Morphology);

    private sealed record CanonicalWordRow(
        int CanonicalVerse, int VerseNumber, int Position, long Id, string Text, string Trailer, string? Gloss,
        string? Lemma, string? StrongNumber, JsonDocument? Morphology);
}
