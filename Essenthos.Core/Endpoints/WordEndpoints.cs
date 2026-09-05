using System.Text.Json;
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

            // Every word this one is linked to, in both directions at once. The reader wants the
            // other texts' words whichever side of the link this word happens to sit on, and a link
            // that states an absence has nothing on the other side to show.
            var linked = await db.LinkWords
                .Where(side => side.WordId == id
                               && side.Link!.Relation != LinkRelation.Expands
                               && side.Link.Relation != LinkRelation.Omits)
                .SelectMany(side => db.LinkWords
                    .Where(other => other.LinkId == side.LinkId && other.Side != side.Side)
                    .Select(other => new
                    {
                        other.WordId,
                        Corpus = other.Word!.Text!.Slug,
                        other.Word.Surface,
                        other.Word.Gloss,
                        Kind = other.Word.Text!.Kind,
                        Position = other.Word.Verse!.Number * 1000 + other.Word.Position,
                        CanonicalBook = other.Word.Verse!.References.First(r => r.IsPrimary).CanonicalBook,
                        CanonicalChapter = other.Word.Verse!.References.First(r => r.IsPrimary).CanonicalChapter,
                        CanonicalVerse = other.Word.Verse!.References.First(r => r.IsPrimary).CanonicalVerse,
                    }))
                .ToListAsync(cancellationToken);

            var renderings = linked
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
                        row.CanonicalVerse)))
                .ToList();

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

            var strong = word.StrongNumber is null
                ? null
                : await db.StrongEntries
                    .Where(e => e.StrongNumber == word.StrongNumber)
                    .Select(e => new StrongEntryResponse(
                        e.StrongNumber, e.Lemma, e.Transliteration, e.Pronunciation, e.Definition,
                        e.Derivation, e.KjvDefinition, e.Morphology, e.DetailedDefinition, e.SeeAlso,
                        e.SourceLanguage, e.TwotReference, false))
                    .FirstOrDefaultAsync(cancellationToken);

            var syntax = await Syntax(db, id, cancellationToken);

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
                null,
                strong,
                renderings,
                syntax));
        });
    }

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
    EntityRefResponse? Entity,
    StrongEntryResponse? Strong,
    IList<WordRenderingResponse> Renderings,
    IList<SyntaxGroupResponse> Syntax);

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
internal record WordRenderingResponse(
    string Corpus, string Text, string? Gloss, long WordId, VerseRefResponse Reference);
