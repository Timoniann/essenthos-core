using System.Text;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Where two texts disagree, word by word, and how much: per chapter with every word's degree and
/// what decided it, and per book as the counts a researcher can cite.
///
/// The two answers are one computation over two spans, so a chapter's counts in the book summary
/// are the chapter's own counts and never a second opinion.
/// </summary>
internal static class DifferenceEndpoints
{
    private const char TextSeparator = ',';

    public static void MapDifferences(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/differences/{book}/{chapter:int}", async (
            string book,
            int chapter,
            [FromQuery] string? texts,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var (pair, ordinal, refusal) = await Resolve(db, canon, book, texts, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }

            var chapterCount = await canon.ChapterCount(ordinal, cancellationToken);
            if (chapter < 1 || chapter > chapterCount)
            {
                return ApiResults.NotFound(
                    $"{BookReferences.Name(ordinal)} has {chapterCount} chapters in the shared numbering, so there " +
                    $"is no chapter {chapter}.");
            }

            return Results.Ok(await Chapter(db, pair!, ordinal, chapter, cancellationToken));
        });

        routes.MapGet("/differences/{book}", async (
            string book,
            [FromQuery] string? texts,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var (pair, ordinal, refusal) = await Resolve(db, canon, book, texts, cancellationToken);
            return refusal ?? Results.Ok(await Book(db, pair!, ordinal, cancellationToken));
        });
    }

    internal sealed record ComparedText(int Id, string Slug, string Language, TextKind Kind);

    internal sealed record ComparedPair(ComparedText A, ComparedText B)
    {
        public bool SameLanguage => string.Equals(A.Language, B.Language, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(ComparedPair? Pair, int Ordinal, IResult? Refusal)> Resolve(
        AppDbContext db,
        ICanonIndex canon,
        string book,
        string? texts,
        CancellationToken cancellationToken)
    {
        var ordinal = BookReferences.ResolveOrdinal(book);
        if (ordinal is null)
        {
            return (null, 0, ApiResults.NotFound(BookReferences.FormatHint(book)));
        }

        var slugs = (texts ?? string.Empty)
            .Split(TextSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var entries = new List<TextEntry>();
        foreach (var slug in slugs)
        {
            if (await canon.Text(slug, cancellationToken) is { } entry)
            {
                entries.Add(entry);
            }
        }

        if (slugs.Length != 2 || entries.Count != 2 || entries[0].Id == entries[1].Id)
        {
            return (null, 0, ApiResults.Malformed(
                $"Name exactly two different texts to compare, as texts=A{TextSeparator}B with the ids /v1/corpora " +
                "lists; the first is the one the other is read against."));
        }

        var ids = entries.Select(e => e.Id).ToList();
        var rows = await db.Texts
            .Where(t => ids.Contains(t.Id))
            .Select(t => new ComparedText(t.Id, t.Slug, t.Language, t.Kind))
            .ToListAsync(cancellationToken);
        return (new ComparedPair(rows.Single(r => r.Id == ids[0]), rows.Single(r => r.Id == ids[1])), ordinal.Value, null);
    }

    internal static async Task<ChapterDifferencesResponse> Chapter(
        AppDbContext db,
        ComparedPair pair,
        int book,
        int chapter,
        CancellationToken cancellationToken)
    {
        var compared = await Compare(db, pair, book, chapter, cancellationToken);
        var verses = compared.Verses
            .Select(verse => new DifferenceVerseResponse(
                verse.Verse,
                [.. verse.A.Select(Word)],
                [.. verse.B.Select(Word)]))
            .ToList();

        return new ChapterDifferencesResponse(
            BookRef(book),
            chapter,
            pair.A.Slug,
            pair.B.Slug,
            Spelling(compared.Basis),
            compared.Through,
            Counts(compared.Verses.SelectMany(v => v.A)),
            Counts(compared.Verses.SelectMany(v => v.B)),
            verses);
    }

    internal static async Task<BookDifferencesResponse> Book(
        AppDbContext db,
        ComparedPair pair,
        int book,
        CancellationToken cancellationToken)
    {
        var compared = await Compare(db, pair, book, null, cancellationToken);
        var chapters = compared.Verses
            .GroupBy(v => v.Chapter)
            .OrderBy(g => g.Key)
            .Select(g => new ChapterDifferenceCountsResponse(
                g.Key,
                Counts(g.SelectMany(v => v.A)),
                Counts(g.SelectMany(v => v.B))))
            .ToList();

        return new BookDifferencesResponse(
            BookRef(book),
            pair.A.Slug,
            pair.B.Slug,
            Spelling(compared.Basis),
            compared.Through,
            Counts(compared.Verses.SelectMany(v => v.A)),
            Counts(compared.Verses.SelectMany(v => v.B)),
            chapters);
    }

    private static BookRefResponse BookRef(int book) =>
        new(book, BookReferences.Name(book), BookReferences.Slug(book));

    private static DifferenceWordResponse Word((long Id, WordDegree Degree) word) =>
        new(word.Id, Spelling(word.Degree.Degree), Spelling(word.Degree.By), [.. word.Degree.With]);

    internal static DifferenceCountsResponse Counts(IEnumerable<(long Id, WordDegree Degree)> words)
    {
        var byDegree = new Dictionary<Degree, int>();
        var byDecision = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        foreach (var (_, degree) in words)
        {
            total++;
            byDegree[degree.Degree] = byDegree.GetValueOrDefault(degree.Degree) + 1;
            var by = Spelling(degree.By);
            byDecision[by] = byDecision.GetValueOrDefault(by) + 1;
        }

        return new DifferenceCountsResponse(
            total,
            byDegree.GetValueOrDefault(Degree.Same),
            byDegree.GetValueOrDefault(Degree.Form),
            byDegree.GetValueOrDefault(Degree.Lemma),
            byDegree.GetValueOrDefault(Degree.Absent),
            byDegree.GetValueOrDefault(Degree.Moved),
            byDegree.GetValueOrDefault(Degree.Linked),
            byDegree.GetValueOrDefault(Degree.Unsure),
            byDegree.GetValueOrDefault(Degree.Unmatched),
            byDegree.GetValueOrDefault(Degree.Unlinked),
            new Dictionary<string, int>(byDecision));
    }

    internal static string Spelling(Degree degree) => degree switch
    {
        Degree.Same => "same",
        Degree.Form => "form",
        Degree.Lemma => "lemma",
        Degree.Absent => "absent",
        Degree.Moved => "moved",
        Degree.Linked => "linked",
        Degree.Unsure => "unsure",
        Degree.Unmatched => "unmatched",
        Degree.Unlinked => "unlinked",
        _ => throw new ArgumentOutOfRangeException(nameof(degree), degree, "a degree with no spelling"),
    };

    internal static string Spelling(Decision decision) => decision switch
    {
        Decision.Letters => "letters",
        Decision.Lexeme => "lexeme",
        Decision.Lemma => "lemma",
        Decision.Strong => "strong",
        Decision.Original => "original",
        Decision.Through => "through",
        Decision.Recorded => "recorded",
        Decision.Position => "position",
        Decision.Link => "link",
        Decision.Nothing => "nothing",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "a decision with no spelling"),
    };

    private static string Spelling(DifferenceBasis basis) => basis switch
    {
        DifferenceBasis.Links => "links",
        DifferenceBasis.Through => "through",
        _ => "none",
    };

    internal sealed record ComparedVerse(
        int Chapter,
        int Verse,
        List<(long Id, WordDegree Degree)> A,
        List<(long Id, WordDegree Degree)> B);

    internal sealed record Comparison(DifferenceBasis Basis, string? Through, List<ComparedVerse> Verses);

    /// <summary>
    /// The two texts compared over a book, or one chapter of it. A verse only one of them has is
    /// left out: the reader already says the verse is not there, and it is in neither text's count.
    /// </summary>
    internal static async Task<Comparison> Compare(
        AppDbContext db,
        ComparedPair pair,
        int book,
        int? chapter,
        CancellationToken cancellationToken)
    {
        var wordsA = await Words(db, pair.A, book, chapter, cancellationToken);
        var wordsB = await Words(db, pair.B, book, chapter, cancellationToken);
        var ids = wordsA.Concat(wordsB).Select(w => w.Id).ToList();

        var translations = pair.A.Kind == TextKind.Translation && pair.B.Kind == TextKind.Translation;
        var direct = await Linked(db, pair.A.Id, pair.B.Id, cancellationToken);
        var pivot = direct && !translations ? null : await Pivot(db, pair, book, cancellationToken);
        var basis = pivot is not null ? DifferenceBasis.Through
            : direct ? DifferenceBasis.Links
            : DifferenceBasis.None;

        var joined = new List<(IReadOnlyList<long> A, IReadOnlyList<long> B)>();
        var recorded = new HashSet<long>();
        var reaching = new HashSet<long>();
        if (basis == DifferenceBasis.Links)
        {
            (joined, recorded) = await Direct(db, pair, ids, cancellationToken);
        }
        else if (pivot is not null)
        {
            (joined, reaching) = await ThroughPivot(db, pair, pivot.Id, ids, cancellationToken);
        }

        var originals = await Originals(db, pair, [.. wordsA.Concat(wordsB).Where(NeedsOriginals)], cancellationToken);
        var formsA = wordsA.Select(w => Form(w, pair.A.Language, originals)).ToList();
        var formsB = wordsB.Select(w => Form(w, pair.B.Language, originals)).ToList();

        var byVerseA = Group(wordsA, formsA);
        var byVerseB = Group(wordsB, formsB);
        var joinedByWord = new Dictionary<long, List<int>>();
        for (var i = 0; i < joined.Count; i++)
        {
            foreach (var id in joined[i].A.Concat(joined[i].B))
            {
                if (!joinedByWord.TryGetValue(id, out var list))
                {
                    joinedByWord[id] = list = [];
                }

                list.Add(i);
            }
        }

        var verses = new List<ComparedVerse>();
        foreach (var (address, left) in byVerseA)
        {
            if (!byVerseB.TryGetValue(address, out var right))
            {
                continue;
            }

            var own = left.Concat(right).Select(w => w.Id).ToHashSet();
            var links = own
                .SelectMany(id => joinedByWord.GetValueOrDefault(id) ?? [])
                .Distinct()
                .Select(i => joined[i])
                .ToList();
            var (degreesA, degreesB) = Differences.Compare(
                new VersePair(left, right, links, recorded, reaching), basis, pair.SameLanguage);
            verses.Add(new ComparedVerse(
                address.Chapter,
                address.Verse,
                [.. left.Select(w => (w.Id, degreesA[w.Id]))],
                [.. right.Select(w => (w.Id, degreesB[w.Id]))]));
        }

        return new Comparison(basis, pivot?.Slug, verses);
    }

    private sealed record WordRow(
        int Chapter,
        int Verse,
        int OwnVerse,
        string? Label,
        int Position,
        long Id,
        string Surface,
        string? Lemma,
        string? Strong,
        string? Lexeme);

    private static async Task<List<WordRow>> Words(
        AppDbContext db,
        ComparedText text,
        int book,
        int? chapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary
                        && r.Verse!.TextId == text.Id
                        && r.CanonicalBook == book
                        && (chapter == null || r.CanonicalChapter == chapter))
            .SelectMany(r => r.Verse!.Words.Where(w => !w.Elided).Select(w => new WordRow(
                r.CanonicalChapter,
                r.CanonicalVerse,
                r.Verse.Number,
                r.Verse.Label,
                w.Position,
                w.Id,
                w.Surface,
                w.Lemma,
                w.StrongNumber,
                w.Morphology == null ? null : w.Morphology.RootElement.GetProperty(VocalizedLexeme).GetString())))
            .ToListAsync(cancellationToken);

        return [.. rows
            .OrderBy(r => r.Chapter).ThenBy(r => r.Verse).ThenBy(r => r.OwnVerse)
            .ThenBy(r => r.Label, StringComparer.Ordinal).ThenBy(r => r.Position)];
    }

    /// <summary>BHSA's <c>voc_lex</c>: the dictionary form, where its <c>lemma</c> is the occurrence's.</summary>
    private const string VocalizedLexeme = "vocalizedLexeme";

    private static Dictionary<(int Chapter, int Verse), List<DifferenceWord>> Group(
        List<WordRow> rows,
        List<DifferenceWord> forms)
    {
        var grouped = new Dictionary<(int, int), List<DifferenceWord>>();
        for (var i = 0; i < rows.Count; i++)
        {
            var key = (rows[i].Chapter, rows[i].Verse);
            if (!grouped.TryGetValue(key, out var list))
            {
                grouped[key] = list = [];
            }

            list.Add(forms[i]);
        }

        return grouped;
    }

    private static bool Hebrew(string language) => language is "hbo" or "arc";

    private static bool Greek(string language) => language == "grc";

    private static bool NeedsOriginals(WordRow row) => row.Lexeme is null && row.Lemma is null;

    private static DifferenceWord Form(WordRow row, string language, Dictionary<long, HashSet<string>> originals)
    {
        var lexeme = Hebrew(language) ? Blank(HebrewLetters.Of(row.Lexeme ?? row.Lemma ?? string.Empty)) : null;
        var lemma = Greek(language) && row.Lemma is { } greek ? Blank(GreekLetters.Bare(greek)) : null;
        var strong = row.Strong is { } number ? StrongKey(number) : null;
        var theirs = originals.GetValueOrDefault(row.Id) ?? [];
        if (strong is not null && lexeme is null && lemma is null)
        {
            theirs = [.. theirs, strong];
        }

        return new DifferenceWord(row.Id, Letters(row.Surface, language), lexeme, lemma, strong, theirs);
    }

    private static string? Blank(string value) => value.Length == 0 ? null : value;

    /// <summary><c>H0776</c> and <c>H776</c> are one entry, and so are <c>G26</c> and <c>g26</c>.</summary>
    internal static string StrongKey(string number)
    {
        var trimmed = number.Trim().ToUpperInvariant();
        if (trimmed.Length < 2)
        {
            return trimmed;
        }

        var digits = trimmed[1..].TrimStart('0');
        return trimmed[0] + (digits.Length == 0 ? "0" : digits);
    }

    /// <summary>
    /// The word as its letters: what two editions printing the same word both print. Hebrew keeps
    /// its consonants, Greek its bare alphabet, and every other script loses its case, its
    /// diacritics and its punctuation, which is what separates <em>LORD</em> from <em>Lord</em> and
    /// nothing a reader would call another word.
    /// </summary>
    internal static string Letters(string surface, string language)
    {
        if (Hebrew(language))
        {
            return HebrewLetters.Of(surface);
        }

        if (Greek(language))
        {
            return new string([.. GreekLetters.Bare(surface).Where(char.IsLetterOrDigit)]);
        }

        var letters = new StringBuilder(surface.Length);
        foreach (var c in surface.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
            {
                letters.Append(char.ToLowerInvariant(c));
            }
        }

        return letters.ToString().Replace('ё', 'е');
    }

    /// <summary>Whether any link at all joins the two texts, in either direction.</summary>
    private static async Task<bool> Linked(AppDbContext db, int a, int b, CancellationToken cancellationToken) =>
        await db.Links.AnyAsync(
            l => (l.FromTextId == a && l.ToTextId == b) || (l.FromTextId == b && l.ToTextId == a),
            cancellationToken);

    /// <summary>
    /// The text both are linked to, where nothing links them to each other: the Ukrainian and the
    /// Synodal are each linked to BHSA and never to one another, and a word of each linked to the
    /// same Hebrew word is the same word as far as the corpus can say.
    ///
    /// An edition over a translation, and the original languages over the rest, because a word of
    /// the original is what a translation's word stands for; among equals the lower id, so the
    /// choice is the same on every request and a chapter's counts agree with its book's.
    /// </summary>
    private static async Task<ComparedText?> Pivot(
        AppDbContext db,
        ComparedPair pair,
        int book,
        CancellationToken cancellationToken)
    {
        int a = pair.A.Id, b = pair.B.Id;
        var common = await db.Texts
            .Where(t => t.Id != a && t.Id != b
                        && db.Books.Any(held => held.TextId == t.Id && held.CanonicalOrdinal == book)
                        && (db.Links.Any(l => l.FromTextId == a && l.ToTextId == t.Id)
                            || db.Links.Any(l => l.FromTextId == t.Id && l.ToTextId == a))
                        && (db.Links.Any(l => l.FromTextId == b && l.ToTextId == t.Id)
                            || db.Links.Any(l => l.FromTextId == t.Id && l.ToTextId == b)))
            .Select(t => new ComparedText(t.Id, t.Slug, t.Language, t.Kind))
            .ToListAsync(cancellationToken);

        return common
            .OrderBy(t => t.Kind == TextKind.Translation ? 1 : 0)
            .ThenBy(t => Hebrew(t.Language) || Greek(t.Language) ? 0 : 1)
            .ThenBy(t => t.Kind == TextKind.CriticalEdition ? 0 : 1)
            .ThenBy(t => t.Id)
            .FirstOrDefault();
    }

    private static readonly LinkRelation[] Corresponding = [LinkRelation.Renders, LinkRelation.Equals, LinkRelation.Transposes];

    /// <summary>
    /// The links between the two texts that touch these words: each link that joins words of both
    /// sides as one correspondence, and the words a link records the other text as lacking.
    /// </summary>
    private static async Task<(List<(IReadOnlyList<long> A, IReadOnlyList<long> B)> Joined, HashSet<long> Recorded)> Direct(
        AppDbContext db,
        ComparedPair pair,
        List<long> ids,
        CancellationToken cancellationToken)
    {
        int a = pair.A.Id, b = pair.B.Id;
        var rows = await db.LinkWords
            .Where(lw => ids.Contains(lw.WordId)
                         && ((lw.Link!.FromTextId == a && lw.Link.ToTextId == b)
                             || (lw.Link.FromTextId == b && lw.Link.ToTextId == a)))
            .Select(lw => new { lw.LinkId, lw.WordId, lw.Word!.TextId, lw.Link!.Relation })
            .ToListAsync(cancellationToken);

        var joined = new List<(IReadOnlyList<long>, IReadOnlyList<long>)>();
        var recorded = new HashSet<long>();
        foreach (var link in rows.GroupBy(r => r.LinkId))
        {
            var relation = link.First().Relation;
            if (relation is LinkRelation.Omits or LinkRelation.Expands)
            {
                recorded.UnionWith(link.Select(r => r.WordId));
                continue;
            }

            if (Corresponding.Contains(relation))
            {
                joined.Add((
                    [.. link.Where(r => r.TextId == a).Select(r => r.WordId)],
                    [.. link.Where(r => r.TextId == b).Select(r => r.WordId)]));
            }
        }

        return (joined, recorded);
    }

    /// <summary>
    /// Both texts' words joined through a third: a word of one and a word of the other correspond
    /// where both are linked to the same word of it. A word linked to it that meets nothing of the
    /// other text is one the other text has nothing for, as far as its links say.
    /// </summary>
    private static async Task<(List<(IReadOnlyList<long> A, IReadOnlyList<long> B)> Joined, HashSet<long> Reaching)> ThroughPivot(
        AppDbContext db,
        ComparedPair pair,
        int pivot,
        List<long> ids,
        CancellationToken cancellationToken)
    {
        int a = pair.A.Id, b = pair.B.Id;
        var rows = await (
                from mine in db.LinkWords
                where ids.Contains(mine.WordId)
                      && Corresponding.Contains(mine.Link!.Relation)
                      && (mine.Link.FromTextId == pivot || mine.Link.ToTextId == pivot)
                from theirs in mine.Link!.Words
                where theirs.Side != mine.Side && theirs.Word!.TextId == pivot
                select new { mine.WordId, mine.Word!.TextId, Pivot = theirs.WordId })
            .ToListAsync(cancellationToken);

        var joined = new List<(IReadOnlyList<long>, IReadOnlyList<long>)>();
        foreach (var group in rows.GroupBy(r => r.Pivot))
        {
            joined.Add((
                [.. group.Where(r => r.TextId == a).Select(r => r.WordId).Distinct()],
                [.. group.Where(r => r.TextId == b).Select(r => r.WordId).Distinct()]));
        }

        return (joined, [.. rows.Select(r => r.WordId)]);
    }

    /// <summary>
    /// For each of these words, the Strong numbers of the words of other texts it is linked to, where
    /// those texts state them — which is how a word of a translation, carrying no dictionary form of
    /// its own, can be told to render the same word as another translation's or a different one.
    /// Links to the two texts being compared are left out: a link between them is what says the
    /// words correspond, and cannot also be the evidence that they mean the same.
    /// </summary>
    private static async Task<Dictionary<long, HashSet<string>>> Originals(
        AppDbContext db,
        ComparedPair pair,
        List<WordRow> words,
        CancellationToken cancellationToken)
    {
        if (words.Count == 0)
        {
            return [];
        }

        int a = pair.A.Id, b = pair.B.Id;
        var ids = words.Select(w => w.Id).ToList();
        var rows = await (
                from mine in db.LinkWords
                where ids.Contains(mine.WordId) && Corresponding.Contains(mine.Link!.Relation)
                from theirs in mine.Link!.Words
                where theirs.Side != mine.Side
                      && theirs.Word!.StrongNumber != null
                      && theirs.Word.TextId != a
                      && theirs.Word.TextId != b
                select new { mine.WordId, theirs.Word!.StrongNumber })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.WordId)
            .ToDictionary(g => g.Key, g => g.Select(r => StrongKey(r.StrongNumber!)).ToHashSet(StringComparer.Ordinal));
    }
}

/// <param name="Words">The words of this text in the verses both texts hold: what the rest are out of.</param>
/// <param name="Same">The other text has the same word here.</param>
/// <param name="Form">It has the same word, written or formed differently.</param>
/// <param name="Lemma">It has another word here.</param>
/// <param name="Absent">It has nothing for this word.</param>
/// <param name="Moved">It has this word elsewhere in the verse.</param>
/// <param name="Linked">A counterpart is linked across two languages and nothing could be compared.</param>
/// <param name="Unsure">A counterpart is written differently and nothing tells a respelling from another word.</param>
/// <param name="Unmatched">
/// Through a third text: it is linked there and no word of the other text is linked to the same word.
/// </param>
/// <param name="Unlinked">Nothing links the word to the other text.</param>
/// <param name="By">How many were decided each way, by the spelling the words carry.</param>
internal record DifferenceCountsResponse(
    int Words,
    int Same,
    int Form,
    int Lemma,
    int Absent,
    int Moved,
    int Linked,
    int Unsure,
    int Unmatched,
    int Unlinked,
    IDictionary<string, int> By);

/// <param name="Degree">
/// <c>same</c>, <c>form</c>, <c>lemma</c>, <c>absent</c>, <c>moved</c>, <c>linked</c>, <c>unsure</c>,
/// <c>unmatched</c> or <c>unlinked</c>.
/// </param>
/// <param name="By">
/// What decided it: <c>letters</c>, <c>lexeme</c>, <c>lemma</c>, <c>strong</c>, <c>original</c>,
/// <c>through</c>, <c>recorded</c>, <c>position</c>, <c>link</c> or <c>nothing</c>.
/// </param>
/// <param name="With">The words of the other text it was compared with.</param>
internal record DifferenceWordResponse(long Id, string Degree, string By, long[] With);

/// <param name="Number">The verse in the shared numbering, as the reader's rows are.</param>
internal record DifferenceVerseResponse(
    int Number,
    IList<DifferenceWordResponse> A,
    IList<DifferenceWordResponse> B);

/// <param name="Basis">
/// <c>links</c> where links join the two texts themselves, <c>through</c> where both are compared
/// through their links to the text <paramref name="Through"/> names — always so for two
/// translations — and <c>none</c> where neither is possible.
/// </param>
internal record ChapterDifferencesResponse(
    BookRefResponse Book,
    int Chapter,
    string A,
    string B,
    string Basis,
    string? Through,
    DifferenceCountsResponse CountsA,
    DifferenceCountsResponse CountsB,
    IList<DifferenceVerseResponse> Verses);

internal record ChapterDifferenceCountsResponse(
    int Chapter,
    DifferenceCountsResponse CountsA,
    DifferenceCountsResponse CountsB);

internal record BookDifferencesResponse(
    BookRefResponse Book,
    string A,
    string B,
    string Basis,
    string? Through,
    DifferenceCountsResponse CountsA,
    DifferenceCountsResponse CountsB,
    IList<ChapterDifferenceCountsResponse> Chapters);
