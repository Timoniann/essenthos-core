using Essenthos.Core.Corpus;
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

    /// <summary>The language whose words the Greek lexicon glosses.</summary>
    private const string Greek = "grc";

    /// <summary>How a gloss was reached, as <see cref="LexiconGlossResponse.Via"/> spells it.</summary>
    private const string ByStatedNumber = "strong";
    private const string ByLemma = "lemma";
    private const string ByEqualWord = "equals";
    private const string ByProposedNumber = "strong-candidate";

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
        Translators = text.Translators,
        Editors = text.Editors,
        Edition = text.Edition,
        EditionYear = text.EditionYear,
        About = text.About,
        RightsNote = text.RightsNote,
        Aliases = TextAliases.Of(text.Slug) is { Count: > 0 } aliases ? aliases : null,
        Form = EnumSpelling.Of(text.Kind),
    };

    /// <summary>
    /// Reads one chapter as this text numbers it. The words and the edition's notes each come back
    /// in one query: a chapter is a thousand words, and a query per word is a thousand round trips.
    /// A note-only verse stays in the response too — it is how an edition says a verse is absent
    /// from its text, and the note is the only thing the reader has to show at that address.
    /// </summary>
    public static async Task<IList<TextVerseResponse>> ReadChapter(
        AppDbContext db,
        int textId,
        int bookOrdinal,
        int chapter,
        CancellationToken cancellationToken)
    {
        var verses = await db.Verses
            .Where(verse => verse.TextId == textId
                            && verse.Book!.CanonicalOrdinal == bookOrdinal
                            && verse.ChapterNumber == chapter)
            .OrderBy(verse => verse.Number).ThenBy(verse => verse.Label)
            .Select(verse => new VerseRow(verse.Number, verse.Label))
            .ToListAsync(cancellationToken);

        var rows = await db.Words
            .Where(word => word.TextId == textId
                           && word.Verse!.Book!.CanonicalOrdinal == bookOrdinal
                           && word.Verse.ChapterNumber == chapter)
            .OrderBy(w => w.Verse!.Number).ThenBy(w => w.Verse!.Label).ThenBy(w => w.Position)
            .Select(w => new WordRow(
                w.Verse!.Number, w.Verse!.Label, w.Id, w.Surface, w.Trailer, w.Gloss, w.Lemma, w.StrongNumber,
                w.Morphology, w.Elided, w.Break))
            .ToListAsync(cancellationToken);

        var counterparts = await Counterparts(db, rows.Select(r => r.Id), cancellationToken);
        counterparts = counterparts with
        {
            Glossed = await Glossed(
                db, textId, [.. rows.Select(r => new GlossWanted(r.Id, r.Gloss, r.Lemma, r.StrongNumber))],
                counterparts.Proposed, cancellationToken),
        };
        var notes = await db.VerseNotes
            .Where(note => note.Verse!.TextId == textId
                           && note.Verse.Book!.CanonicalOrdinal == bookOrdinal
                           && note.Verse.ChapterNumber == chapter)
            .OrderBy(note => note.Verse!.Number).ThenBy(note => note.Verse!.Label).ThenBy(note => note.Position)
            .Select(note => new NoteRow(
                note.Verse!.Number, note.Verse.Label, note.Kind, note.Content, note.AnchorWordId,
                note.AnchorWord == null ? null : note.AnchorWord.Surface))
            .ToListAsync(cancellationToken);

        return Group(verses, rows, counterparts, notes);
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

        // Asked of the group table rather than tested per word: a chapter is a thousand words, and
        // BHSA's are each in seven groups, so an EXISTS per word would walk seven rows a thousand
        // times to answer no.
        var supplied = await db.WordGroupWords
            .Where(m => ids.Contains(m.WordId) && m.WordGroup!.Kind == WordGroupKind.Supplied)
            .Select(m => m.WordId)
            .ToListAsync(cancellationToken);

        return new Reached(
            own.Concat(reached).ToLookup(row => row.WordId, row => row.Reached),
            strongest,
            absent,
            supplied.ToHashSet(),
            await Annotations.Of(db, ids, cancellationToken),
            await Proposed(db, ids, cancellationToken));
    }

    /// <summary>
    /// The Strong number the corpus proposed for each word, where it proposed one and only one.
    ///
    /// A word whose lemma two dictionary entries claim has two proposals, and choosing between them
    /// here would be a guess sent out as an answer; it gets none. Two methods proposing the same
    /// number are one answer, and the surer of them describes it.
    /// </summary>
    private static async Task<Dictionary<long, StrongCandidateResponse>> Proposed(
        AppDbContext db,
        List<long> ids,
        CancellationToken cancellationToken)
    {
        var proposals = await db.WordStrongs
            .Where(proposal => ids.Contains(proposal.WordId))
            .Select(proposal => new
            {
                proposal.WordId, proposal.Number, proposal.Method, proposal.Confidence,
            })
            .ToListAsync(cancellationToken);

        return proposals
            .GroupBy(proposal => proposal.WordId)
            .Where(group => group.Select(proposal => proposal.Number).Distinct().Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(proposal => proposal.Confidence is null)
                    .ThenByDescending(proposal => proposal.Confidence)
                    .Select(proposal => new StrongCandidateResponse(
                        proposal.Number, EnumSpelling.Of(proposal.Method), proposal.Confidence))
                    .First());
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
    /// <param name="Supplied">
    /// The words the edition itself marks as its own. It is not <paramref name="Absent"/> read
    /// twice: that is what an alignment found, and this is what the edition printed, which is a
    /// first-hand claim about one text and names no counterpart for it.
    /// </param>
    /// <param name="Named">
    /// The person or place each word names, where the corpus can say and can say it without
    /// choosing. Empty for most words, because most words are not names.
    /// </param>
    /// <param name="Proposed">
    /// The one Strong number the corpus proposed for each word, where it proposed exactly one.
    /// </param>
    private sealed record Reached(
        ILookup<long, long> Witnesses,
        Dictionary<long, string> Provenance,
        Dictionary<long, string> Absent,
        HashSet<long> Supplied,
        Dictionary<long, EntityRefResponse> Named,
        Dictionary<long, StrongCandidateResponse> Proposed)
    {
        /// <summary>The lexicon's gloss for each Greek word its edition does not gloss, and how it was reached.</summary>
        public Dictionary<long, LexiconGlossResponse> Glossed { get; init; } = [];
    }

    private sealed record GlossWanted(long Id, string? Gloss, string? Lemma, string? StrongNumber);

    /// <summary>
    /// A short gloss for every Greek word whose edition prints none, from the lexicon, and the way
    /// each was reached — because the ways are not equally strong and a reader is owed the difference.
    ///
    /// Tried in order of how little stands between the word and the entry: the number the edition
    /// itself prints; then the word's own dictionary form; then, for a word that has neither, the
    /// dictionary form of the word it is linked to as the same word in another edition — Swete's
    /// Septuagint reaches Brenton's lemmas that way; and last the one number the corpus proposed from
    /// the word's form. A form or a number the lexicon files under several entries gets every one of
    /// their glosses, in the lexicon's order, rather than one of them chosen here.
    ///
    /// Three queries for a chapter at most, and none at all for a text that is not Greek.
    /// </summary>
    private static async Task<Dictionary<long, LexiconGlossResponse>> Glossed(
        AppDbContext db,
        int textId,
        IReadOnlyList<GlossWanted> words,
        Dictionary<long, StrongCandidateResponse> proposed,
        CancellationToken cancellationToken)
    {
        var wanting = words.Where(word => word.Gloss is null).ToList();
        if (wanting.Count == 0
            || await db.Texts.Where(text => text.Id == textId).Select(text => text.Language)
                .SingleAsync(cancellationToken) != Greek)
        {
            return [];
        }

        var bare = wanting.Where(word => word.Lemma is null && word.StrongNumber is null).Select(word => word.Id).ToList();
        var borrowed = bare.Count == 0
            ? []
            : (await db.LinkWords
                    .Where(side => bare.Contains(side.WordId) && side.Link!.Relation == LinkRelation.Equals)
                    .SelectMany(side => db.LinkWords
                        .Where(other => other.LinkId == side.LinkId
                                        && other.Side != side.Side
                                        && other.Word!.Lemma != null)
                        .Select(other => new { side.WordId, other.Word!.Lemma }))
                    .ToListAsync(cancellationToken))
                .GroupBy(row => row.WordId)
                .Where(group => group.Select(row => row.Lemma).Distinct().Count() == 1)
                .ToDictionary(group => group.Key, group => group.First().Lemma!);

        var lemmas = wanting.Select(word => word.Lemma).Concat(borrowed.Values).OfType<string>().Distinct().ToList();
        var numbers = wanting
            .Select(word => word.StrongNumber ?? proposed.GetValueOrDefault(word.Id)?.Number)
            .OfType<string>()
            .Distinct()
            .ToList();

        var entries = await db.LexiconGlosses
            .Where(gloss => lemmas.Contains(gloss.Lemma) || numbers.Contains(gloss.StrongNumber))
            .OrderBy(gloss => gloss.Id)
            .Select(gloss => new { gloss.Lemma, gloss.StrongNumber, gloss.Gloss })
            .ToListAsync(cancellationToken);
        var byLemma = entries.ToLookup(entry => entry.Lemma, StringComparer.Ordinal);
        var byNumber = entries.ToLookup(entry => entry.StrongNumber, StringComparer.Ordinal);

        static LexiconGlossResponse? Answer(IEnumerable<string> glosses, string via) =>
            glosses.Distinct(StringComparer.Ordinal).ToArray() is { Length: > 0 } found
                ? new LexiconGlossResponse(found, via)
                : null;

        var glossed = new Dictionary<long, LexiconGlossResponse>();
        foreach (var word in wanting)
        {
            // A number filed under several entries is narrowed by the word's own form where the
            // edition prints one and it names one of them; otherwise every entry under the number stands.
            var stated = word.StrongNumber is { } number ? byNumber[number].ToList() : [];
            var own = stated.Where(entry => entry.Lemma == word.Lemma).ToList();

            var answer =
                Answer((own.Count > 0 ? own : stated).Select(entry => entry.Gloss), ByStatedNumber)
                ?? (word.Lemma is { } lemma ? Answer(byLemma[lemma].Select(entry => entry.Gloss), ByLemma) : null)
                ?? (borrowed.TryGetValue(word.Id, out var equal)
                    ? Answer(byLemma[equal].Select(entry => entry.Gloss), ByEqualWord)
                    : null)
                ?? (word.StrongNumber is null && proposed.GetValueOrDefault(word.Id) is { } candidate
                    ? Answer(byNumber[candidate.Number].Select(entry => entry.Gloss), ByProposedNumber)
                    : null);

            if (answer is not null)
            {
                glossed[word.Id] = answer;
            }
        }

        return glossed;
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
                r.CanonicalVerse, r.Verse.Number, r.Verse.Label, w.Position, w.Id, w.Surface, w.Trailer, w.Gloss,
                w.Lemma, w.StrongNumber, w.Morphology, w.Elided, w.Break)))
            .ToListAsync(cancellationToken);

        var counterparts = await Counterparts(db, rows.Select(r => r.Id), cancellationToken);
        counterparts = counterparts with
        {
            Glossed = await Glossed(
                db, textId, [.. rows.Select(r => new GlossWanted(r.Id, r.Gloss, r.Lemma, r.StrongNumber))],
                counterparts.Proposed, cancellationToken),
        };

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
                        r.Elided, r.Break, counterparts))
                    .ToList());
    }

    /// <summary>
    /// Grouped by the number **and** the letter, because the Septuagint prints Genesis 31 as 49,
    /// 50, 50a, 52 and two verses numbered 50 are two verses. The separate verse list is what
    /// lets a note-only verse remain visible rather than being lost from a query rooted at words.
    /// </summary>
    private static IList<TextVerseResponse> Group(
        List<VerseRow> verses,
        List<WordRow> rows,
        Reached counterparts,
        List<NoteRow>? notes = null)
    {
        var notesByVerse = (notes ?? [])
            .ToLookup(note => (note.VerseNumber, note.Label), note => new SourceNoteResponse(
                EnumSpelling.Of(note.Kind), note.Content, note.AnchorWordId, note.Anchor));
        var wordsByVerse = rows.ToLookup(row => (row.VerseNumber, row.Label));

        return verses
            .Select(verse => new TextVerseResponse(
                verse.Number,
                wordsByVerse[(verse.Number, verse.Label)].Select(row => Word(
                        row.Id, row.Text, row.Trailer, row.Gloss, row.Lemma, row.StrongNumber, row.Morphology,
                        row.Elided, row.Break, counterparts))
                    .ToList(),
                verse.Label)
            {
                Notes = [.. notesByVerse[(verse.Number, verse.Label)]],
            })
            .ToList();
    }

    private static TextWordResponse Word(
        long id,
        string text,
        string trailer,
        string? gloss,
        string? lemma,
        string? strongNumber,
        JsonDocument? morphology,
        bool elided,
        TextBreak? opening,
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
            counterparts.Named.GetValueOrDefault(id),
            Morphology(features),
            Feature(features, Phono),
            Feature(features, PhonoTrailer),
            Feature(features, Language))
        {
            Elided = elided,
            Supplied = counterparts.Supplied.Contains(id),
            StrongCandidate = strongNumber is null ? counterparts.Proposed.GetValueOrDefault(id) : null,
            LexiconGloss = counterparts.Glossed.GetValueOrDefault(id),
            Break = opening is { } kind ? EnumSpelling.Of(kind) : null,
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
            Feature(features, "case"),
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
        string? StrongNumber, JsonDocument? Morphology, bool Elided, TextBreak? Break);

    private sealed record VerseRow(int Number, string Label);

    private sealed record NoteRow(
        int VerseNumber,
        string Label,
        VerseNoteKind Kind,
        string Content,
        long? AnchorWordId,
        string? Anchor);

    private sealed record CanonicalWordRow(
        int CanonicalVerse, int VerseNumber, string Label, int Position, long Id, string Text, string Trailer,
        string? Gloss, string? Lemma, string? StrongNumber, JsonDocument? Morphology, bool Elided, TextBreak? Break);
}
