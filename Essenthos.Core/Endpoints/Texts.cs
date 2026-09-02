using System.Globalization;
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
        coverage)
    {
        RightsHolder = text.RightsHolder,
        LicenseUrl = text.LicenceUrl,
        Citation = text.Citation,
        SourceUrl = text.SourceUrl,
        Redistribution = EnumSpelling.Of(text.Redistribution),
    };

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
            .OrderBy(w => w.Verse!.Number).ThenBy(w => w.Verse!.Label).ThenBy(w => w.Position)
            .Select(w => new WordRow(
                w.Verse!.Number, w.Verse!.Label, w.Id, w.Surface, w.Trailer, w.Gloss, w.Lemma, w.StrongNumber,
                w.Morphology, w.Elided))
            .ToListAsync(cancellationToken);

        return Group(rows, await Counterparts(db, rows.Select(r => r.Id), cancellationToken));
    }

    /// <summary>
    /// The words of the ancient witnesses each of these words reaches, in one query rather than one
    /// per word.
    ///
    /// The client treats this set as opaque and highlights where two words' sets intersect, so what
    /// the set holds decides what can light up together. Holding link ids would only ever join two
    /// texts that are **directly** linked — the Ukrainian and the Synodal each link to BHSA and
    /// never to each other, so hovering one would leave the other dark although both name the same
    /// Hebrew word.
    ///
    /// Holding the witness's word ids instead makes the join happen through the witness, which is
    /// what the whole model is shaped for: five texts meet at the word they all render, and a sixth
    /// joins them by being linked to that same word rather than to any of them. A word of a witness
    /// carries its own id, so it meets the translations that reach it.
    /// </summary>
    private static async Task<Reached> Counterparts(
        AppDbContext db,
        IEnumerable<long> wordIds,
        CancellationToken cancellationToken)
    {
        var ids = wordIds.Distinct().ToList();

        var own = await db.Words
            .Where(w => ids.Contains(w.Id) && w.Text!.Kind != TextKind.Translation)
            .Select(w => new { WordId = w.Id, Reached = w.Id })
            .ToListAsync(cancellationToken);

        var reached = await db.LinkWords
            .Where(side => ids.Contains(side.WordId))
            .SelectMany(side => db.LinkWords
                .Where(other => other.LinkId == side.LinkId
                                && other.Side != side.Side
                                && other.Word!.Text!.Kind != TextKind.Translation)
                .Select(other => new { side.WordId, Reached = other.WordId }))
            .ToListAsync(cancellationToken);

        // Only the links that reach a witness, because those are the ones the highlighting is built
        // from. A translation is now linked to other translations too — the Synodal to the King
        // James, which is how it reaches the Hebrew at all — and letting one of those describe the
        // word would report the confidence of a step the reader never sees.
        var evidence = await db.LinkWords
            .Where(side => ids.Contains(side.WordId)
                           && (side.Link!.Relation == LinkRelation.Expands
                               || db.LinkWords.Any(other => other.LinkId == side.LinkId
                                                            && other.Side != side.Side
                                                            && other.Word!.Text!.Kind != TextKind.Translation)))
            .Select(side => new { side.WordId, side.Link!.Method, side.Link!.Confidence, side.Link.Relation })
            .ToListAsync(cancellationToken);

        var strongest = evidence
            .GroupBy(row => row.WordId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.Confidence is null)
                    .ThenByDescending(row => row.Confidence)
                    .Select(row => Provenance(row.Method, row.Confidence))
                    .First());

        // A word whose absence is stated is not a word nothing was found for, and a reader has to
        // be able to tell them apart — that difference is what the schema was built to hold and
        // what 22,155 King James italics say outright.
        var absent = evidence
            .Where(row => row.Relation is LinkRelation.Expands or LinkRelation.Omits)
            .GroupBy(row => row.WordId)
            .ToDictionary(group => group.Key, group => EnumSpelling.Of(group.First().Relation));

        return new Reached(
            own.Concat(reached).ToLookup(row => row.WordId, row => row.Reached), strongest, absent);
    }

    /// <summary>
    /// What established this word's link, in one string the client can show without reading the
    /// schema: the method, and the confidence where there is one. A link with no confidence is one
    /// somebody asserted, and it is the only kind that carries no number — which is the point, and
    /// why the number is not defaulted to 1.
    /// </summary>
    private static string Provenance(LinkMethod method, double? confidence) =>
        confidence is { } value
            ? $"{EnumSpelling.Of(method)}:{value.ToString("0.##", CultureInfo.InvariantCulture)}"
            : EnumSpelling.Of(method);

    /// <param name="Witnesses">The witness words each word reaches, which is what the client intersects.</param>
    /// <param name="Provenance">What established the strongest link on each word, where it has one.</param>
    /// <param name="Absent">
    /// Where a link records an absence rather than a correspondence: <c>expands</c> for a word this
    /// text supplies and the other does not have, <c>omits</c> for the reverse.
    /// </param>
    private sealed record Reached(
        ILookup<long, long> Witnesses,
        Dictionary<long, string> Provenance,
        Dictionary<long, string> Absent);

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
                r.CanonicalVerse, r.Verse.Number, r.Verse.Label, w.Position, w.Id, w.Surface, w.Trailer, w.Gloss,
                w.Lemma, w.StrongNumber, w.Morphology, w.Elided)))
            .ToListAsync(cancellationToken);

        var counterparts = await Counterparts(db, rows.Select(r => r.Id), cancellationToken);

        return rows
            .GroupBy(r => r.CanonicalVerse)
            .ToDictionary(
                group => group.Key,
                // The letter orders too. Two verses of this text can sit at one canonical address
                // — the Septuagint's 50 and 50a both answer to Genesis 31:50 — and ordering by
                // position alone shuffles their words together.
                group => group
                    .OrderBy(r => r.VerseNumber).ThenBy(r => r.Label).ThenBy(r => r.Position)
                    .Select(r => Word(r.Id, r.Text, r.Trailer, r.Gloss, r.Lemma, r.StrongNumber, r.Morphology,
                        r.Elided, counterparts))
                    .ToList());
    }

    /// <summary>
    /// Grouped by the number **and** the letter, because the Septuagint prints Genesis 31 as 49,
    /// 50, 50a, 52 and two verses numbered 50 are two verses. Grouping by the number alone put
    /// both their words in one list ordered by position, which interleaved them word by word.
    /// </summary>
    private static IList<TextVerseResponse> Group(List<WordRow> rows, Reached counterparts) =>
        rows.GroupBy(r => (r.VerseNumber, r.Label))
            .OrderBy(group => group.Key.VerseNumber).ThenBy(group => group.Key.Label)
            .Select(group => new TextVerseResponse(
                group.Key.VerseNumber,
                group.Select(r => Word(r.Id, r.Text, r.Trailer, r.Gloss, r.Lemma, r.StrongNumber, r.Morphology,
                        r.Elided, counterparts))
                    .ToList(),
                group.Key.Label))
            .ToList();

    private static TextWordResponse Word(
        long id,
        string text,
        string trailer,
        string? gloss,
        string? lemma,
        string? strongNumber,
        JsonDocument? morphology,
        bool elided,
        Reached counterparts)
    {
        var features = Features(morphology);
        return new TextWordResponse(
            id,
            text,
            trailer,
            gloss,
            lemma,
            strongNumber,
            [.. counterparts.Witnesses[id].Distinct()],
            counterparts.Provenance.GetValueOrDefault(id),
            counterparts.Absent.GetValueOrDefault(id),
            null,
            Morphology(features),
            Feature(features, Phono),
            Feature(features, PhonoTrailer),
            Feature(features, Language))
        {
            Elided = elided,
        };
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
        int VerseNumber, string Label, long Id, string Text, string Trailer, string? Gloss, string? Lemma,
        string? StrongNumber, JsonDocument? Morphology, bool Elided);

    private sealed record CanonicalWordRow(
        int CanonicalVerse, int VerseNumber, string Label, int Position, long Id, string Text, string Trailer,
        string? Gloss, string? Lemma, string? StrongNumber, JsonDocument? Morphology, bool Elided);
}
