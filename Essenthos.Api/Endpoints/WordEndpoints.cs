using Essenthos.Core.Corpus;
﻿using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// One word, and everything the corpus knows about it.
///
/// This is what a reader clicking a word gets, and it is the only screen where all four of the
/// things this project holds meet on one object: the annotation the witness carries, the lexicon
/// entry its Strong number points at, the words other texts put where it stands, and the sentence,
/// clause and phrase its own text's analysis places it in. Three of those four were reachable and
/// the fourth had nowhere to be reached from — the reader called an endpoint that did not exist and
/// showed an empty panel for every word in the corpus.
/// </summary>
internal static class WordEndpoints
{
    /// <summary>
    /// The order the syntax reads in: innermost first, because that is how a reader reads it — this
    /// word is the predicate of this phrase, in this clause, in this sentence. The atom kinds sit
    /// beside their whole and say nothing extra to a reader, so they are dropped here rather than
    /// making the list twice as long and half as clear.
    /// </summary>
    private static readonly WordGroupKind[] Told =
    [
        WordGroupKind.Subphrase,
        WordGroupKind.Phrase,
        WordGroupKind.Clause,
        WordGroupKind.Sentence,
    ];

    public static void MapWords(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/words/{corpus}/{id:long}", async (
            string corpus,
            long id,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            if (await canon.Text(corpus, cancellationToken) is not { } text)
            {
                return Results.NotFound(new ProblemResponse($"There is no text \"{corpus}\"."));
            }

            var word = await db.Words
                .Where(w => w.Id == id && w.TextId == text.Id)
                .Select(w => new
                {
                    w.Id,
                    w.Surface,
                    w.Gloss,
                    w.Lemma,
                    w.StrongNumber,
                    w.Morphology,
                    Ordinal = w.Verse!.Book!.CanonicalOrdinal,
                    BookName = w.Verse!.Book!.Name,
                    Chapter = w.Verse!.ChapterNumber,
                    Verse = w.Verse!.Number,
                    CanonicalBook = w.Verse!.References.First(r => r.IsPrimary).CanonicalBook,
                    CanonicalChapter = w.Verse!.References.First(r => r.IsPrimary).CanonicalChapter,
                    CanonicalVerse = w.Verse!.References.First(r => r.IsPrimary).CanonicalVerse,
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (word is null)
            {
                return Results.NotFound(new ProblemResponse($"{text.Slug} has no word {id}."));
            }

            var linked = await Linked(db, id, cancellationToken);
            var renderings = await Renderings(db, linked, cancellationToken);

            // The same set the reader highlights on: the witness words this one reaches, plus its
            // own id where it is a witness itself. Texts.Counterparts explains why it is the
            // witness's id and not the link's.
            var witnesses = linked
                .Where(row => row.Kind != TextKind.Translation)
                .Select(row => row.WordId)
                .Distinct()
                .ToList();

            if (text.Id is var _ && await db.Texts
                    .Where(t => t.Id == text.Id)
                    .Select(t => t.Kind)
                    .FirstAsync(cancellationToken) != TextKind.Translation)
            {
                witnesses.Insert(0, id);
            }

            // The gentilic travels with the entry rather than being fetched separately, because
            // this response is what a reader hovering a word gets: the Ukrainian моавітяни reaches
            // the Hebrew word behind it, the word carries H4125, and the answer worth showing is
            // that the people are named after Moab.
            var stated = word.StrongNumber is null
                ? null
                : await StrongEndpoints.Gentilic(db, word.StrongNumber, cancellationToken);

            var entry = word.StrongNumber is null
                ? null
                : await db.StrongEntries
                    .FirstOrDefaultAsync(e => e.StrongNumber == word.StrongNumber, cancellationToken);

            var strong = entry is null
                ? null
                : new StrongEntryResponse(
                    entry.StrongNumber, entry.Lemma, entry.Transliteration, entry.Pronunciation,
                    entry.Definition, entry.Derivation, entry.KjvDefinition, entry.Morphology,
                    entry.DetailedDefinition, entry.SeeAlso, entry.SourceLanguage,
                    entry.TwotReference, false, stated);

            var supplied = await Supplied(db, id, cancellationToken);

            var syntax = await Syntax(db, id, cancellationToken);
            var named = await Annotations.AllOf(db, id, cancellationToken);

            return Results.Ok(new WordDetailResponse(
                word.Id,
                text.Slug,
                word.Surface,
                word.Gloss,
                word.Lemma,
                word.StrongNumber,
                [.. witnesses],
                new BookRefResponse(word.Ordinal, BookReferences.Name(word.Ordinal),
                    BookReferences.Slug(word.Ordinal)),
                word.Chapter,
                word.Verse,
                new VerseRefResponse(
                    word.CanonicalBook,
                    BookReferences.Name(word.CanonicalBook),
                    BookReferences.Slug(word.CanonicalBook),
                    word.CanonicalChapter,
                    word.CanonicalVerse),
                Morphology(word.Morphology),
                supplied,
                named.FirstOrDefault(),
                strong,
                renderings,
                syntax)
            {
                Entities = named,
            });
        });
    }

    /// <summary>
    /// Every word this one is linked to, in both directions at once. The reader wants the other
    /// texts' words whichever side of the link this word happens to sit on, and a link that states
    /// an absence has nothing on the other side to show.
    /// </summary>
    internal static async Task<List<LinkedWord>> Linked(
        AppDbContext db,
        long id,
        CancellationToken cancellationToken) =>
        await db.LinkWords
            .Where(side => side.WordId == id
                           && side.Link!.Relation != LinkRelation.Expands
                           && side.Link.Relation != LinkRelation.Omits)
            .SelectMany(side => db.LinkWords
                .Where(other => other.LinkId == side.LinkId && other.Side != side.Side)
                .Select(other => new LinkedWord(
                    other.WordId,
                    other.LinkId,
                    side.Link!.Method,
                    side.Link.Confidence,
                    side.Link.Source,
                    other.Word!.Text!.Slug,
                    other.Word.Surface,
                    other.Word.Gloss,
                    other.Word.Text!.Kind,
                    other.Word.Verse!.Number * 1000 + other.Word.Position,
                    other.Word.Verse!.References.First(r => r.IsPrimary).CanonicalBook,
                    other.Word.Verse!.References.First(r => r.IsPrimary).CanonicalChapter,
                    other.Word.Verse!.References.First(r => r.IsPrimary).CanonicalVerse)))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The linked words as renderings, each with what made its link and who says so.
    ///
    /// Every method that made a claim on the link is named, so a rendering one source stated and
    /// the aligner also found says both rather than only the claim that won the link. Where two
    /// links reach the same word, the one a source stated speaks for it, and after that the surer
    /// inference: the same order the chapter's highlighting is described in.
    /// </summary>
    internal static async Task<IList<WordRenderingResponse>> Renderings(
        AppDbContext db,
        IReadOnlyCollection<LinkedWord> linked,
        CancellationToken cancellationToken)
    {
        var linkIds = linked.Select(row => row.LinkId).Distinct().ToList();
        var claims = (await db.LinkClaims
                .Where(claim => linkIds.Contains(claim.LinkId))
                .Select(claim => new { claim.LinkId, claim.Method })
                .ToListAsync(cancellationToken))
            .ToLookup(claim => claim.LinkId, claim => claim.Method);

        return
        [
            .. linked
                .OrderByDescending(row => row.Confidence is null)
                .ThenByDescending(row => row.Confidence)
                .DistinctBy(row => row.WordId)
                .OrderBy(row => row.Corpus)
                .ThenBy(row => row.Position)
                .Select(row => new WordRenderingResponse(
                    row.Corpus,
                    row.Surface,
                    row.Gloss,
                    row.WordId,
                    new VerseRefResponse(
                        row.CanonicalBook,
                        BookReferences.Name(row.CanonicalBook),
                        BookReferences.Slug(row.CanonicalBook),
                        row.CanonicalChapter,
                        row.CanonicalVerse),
                    EnumSpelling.Of(row.Method),
                    row.Confidence,
                    [
                        .. claims[row.LinkId]
                            .Where(method => method != row.Method)
                            .Distinct()
                            .Select(method => EnumSpelling.Of(method))
                            .Order(StringComparer.Ordinal),
                    ],
                    Datasets.Match(row.Source)?.Name)),
        ];
    }

    /// <param name="Position">Verse number and position folded into one key, for ordering only.</param>
    internal sealed record LinkedWord(
        long WordId,
        long LinkId,
        LinkMethod Method,
        double? Confidence,
        string Source,
        string Corpus,
        string Surface,
        string? Gloss,
        TextKind Kind,
        int Position,
        int CanonicalBook,
        int CanonicalChapter,
        int CanonicalVerse);

    /// <summary>
    /// Whether the edition prints this word as its own, rather than as a rendering of anything in
    /// the text it translated: a <see cref="WordGroupKind.Supplied"/> group, which is where every
    /// edition's mark is kept — the Synodal's and the Berean's brackets and the King James' italics
    /// alike.
    /// </summary>
    internal static async Task<bool> Supplied(
        AppDbContext db,
        long id,
        CancellationToken cancellationToken) =>
        await db.WordGroupWords
            .AnyAsync(m => m.WordId == id && m.WordGroup!.Kind == WordGroupKind.Supplied, cancellationToken);

    /// <summary>
    /// The groups this word sits in, smallest first. Each carries its own features, so the reader
    /// sees <em>predicate</em> on the phrase and <em>narrative</em> on the clause rather than one
    /// undifferentiated bag of codes.
    /// </summary>
    private static async Task<IList<SyntaxGroupResponse>> Syntax(
        AppDbContext db,
        long id,
        CancellationToken cancellationToken)
    {
        var groups = await db.WordGroupWords
            .Where(m => m.WordId == id && Told.Contains(m.WordGroup!.Kind))
            .Select(m => new
            {
                m.WordGroup!.Id,
                m.WordGroup.Kind,
                m.WordGroup.Features,
                Words = m.WordGroup.Words.Count,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. groups
                .OrderBy(g => Array.IndexOf(Told, g.Kind))
                .ThenBy(g => g.Words)
                .Select(g => new SyntaxGroupResponse(
                    g.Id, EnumSpelling.Of(g.Kind), g.Words, Features(g.Features), null)),
        ];
    }

    private static Dictionary<string, string>? Features(JsonDocument? features) =>
        features?.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty);

    /// <summary>
    /// The annotation under the names the old contract gives it. Kept identical to the chapter
    /// reader's projection on purpose: a word looked at in the panel and the same word read in the
    /// text must not describe themselves differently.
    /// </summary>
    private static MorphologyResponse? Morphology(JsonDocument? morphology)
    {
        if (morphology is null)
        {
            return null;
        }

        var features = morphology.RootElement;

        string? Of(string name) =>
            features.TryGetProperty(name, out var value) ? value.GetString() : null;

        return Of("pos") is null
            ? null
            : new MorphologyResponse(
                Of("pos"), Of("case"), Of("gender"), Of("number"), Of("person"), Of("state"),
                Of("stem"), Of("tense"), Of("lexicalSet"), Of("phrasePos"), Of("suffixGender"),
                Of("suffixNumber"), Of("suffixPerson"), Of("nameType")?.Split(','));
    }
}

/// <param name="OriginalWordIds">
/// The witness words this word reaches — the set the reader intersects to light two texts up
/// together. Named for the old contract; the model calls them witnesses.
/// </param>
/// <param name="Supplied">
/// Whether the edition prints this word as its own, with nothing behind it in the text it was
/// translating. The reader marks these, and until now nothing in this response distinguished a
/// bracketed word from any other.
/// </param>
/// <param name="Syntax">
/// The groups this word's own text places it in, innermost first. Empty for every text but BHSA,
/// which is the only one that carries an analysis.
/// </param>
/// <param name="Reference">
/// Where this word stands in the shared frame. <c>Chapter</c> and <c>Verse</c> are its own text's
/// numbering and stay that way, because a reader asking about a Hebrew word wants the number the
/// Hebrew prints. This is the coordinate that can be compared with another text's.
/// </param>
internal record WordDetailResponse(
    long Id,
    string Corpus,
    string Text,
    string? Gloss,
    string? Lexeme,
    string? StrongNo,
    long[] OriginalWordIds,
    BookRefResponse Book,
    int Chapter,
    int Verse,
    VerseRefResponse Reference,
    MorphologyResponse? Morphology,
    bool Supplied,
    EntityRefResponse? Entity,
    StrongEntryResponse? Strong,
    IList<WordRenderingResponse> Renderings,
    IList<SyntaxGroupResponse> Syntax)
{
    /// <summary>
    /// Every record the word names, in a fixed order, <see cref="Entity"/> first. Empty for a word
    /// that names nothing. <see cref="Entity"/> stays for a client that reads one.
    /// </summary>
    public IReadOnlyList<EntityRefResponse> Entities { get; init; } = [];
}

/// <param name="Reference">
/// Where this rendering stands in the shared frame, not in its own text's numbering.
///
/// It has to be the frame, because the two texts number differently and that is the whole reason
/// the field exists: the Hebrew calls a psalm's superscription 3:1 and the Synodal calls the verse
/// holding both the superscription and the psalm's first line 3:1 too, so their own numbers agree
/// while the words sit a row apart. Compared against <see cref="WordDetailResponse.Reference"/>,
/// which is the same coordinate, the difference is visible; compared against either text's own
/// numbering it is not.
/// </param>
/// <param name="Method">What made the link this rendering stands on.</param>
/// <param name="Confidence">
/// How sure the process that made it was, and null where a source or a person stated it — the
/// only kind of link that carries no number.
/// </param>
/// <param name="AlsoBy">The other methods that made the same claim on the same link.</param>
/// <param name="StatedBy">The dataset the link's source belongs to, where a dataset stated it.</param>
internal record WordRenderingResponse(
    string Corpus,
    string Text,
    string? Gloss,
    long WordId,
    VerseRefResponse Reference,
    string Method,
    double? Confidence,
    IList<string> AlsoBy,
    string? StatedBy);
