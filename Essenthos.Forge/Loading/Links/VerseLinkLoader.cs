using System.Diagnostics;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Straight">Verse pairs that are one verse against one verse.</param>
/// <param name="Divided">
/// Pairs where one side says in several verses what the other says in one, or the two divide a
/// passage differently. These are the ones worth having: a word link inside them crosses a verse
/// boundary legitimately, and without the verse link there is nothing to say so.
/// </param>
/// <param name="Alone">
/// Verses on one side that no verse of the other answers. Counted and not written: a verse absent
/// from the other text is not a correspondence, and writing it as one would assert an absence the
/// frame does not state.
/// </param>
/// <param name="Covered">
/// Memberships added because a verse covers a second canonical address and something stands at that
/// address in the other text. Non-zero on a start after the frame has learned to say that a verse
/// spans two rows, and zero on every start after it.
/// </param>
/// <param name="Stated">
/// Verse pairs a source states through its own word links, where the frame joins nothing. Zero
/// wherever the two agree, which is almost everywhere.
/// </param>
internal sealed record VerseLinkOutcome(
    bool AlreadyLoaded,
    int Pairs,
    int Links,
    int Straight,
    int Divided,
    int Alone,
    int Covered,
    int Stated,
    TimeSpan Elapsed)
{
    public override string ToString() => (AlreadyLoaded, Covered) switch
    {
        (true, 0) when Stated == 0 => "the verse links are already loaded",
        (true, _) => $"the verse links are already loaded; {Covered} verses joined at an address " +
                     $"another verse covers, {Stated} pairs a source states",
        _ => $"{Links} verse links over {Pairs} text pairs in {Elapsed}: {Straight} one verse against " +
             $"one, {Divided} where the two divide the passage differently, {Alone} verses with no " +
             $"counterpart at all, {Covered} joined at an address another verse covers, {Stated} " +
             "stated by a source through its own word links",
    };
}

/// <param name="Without">
/// Books of <paramref name="From"/>, by canonical ordinal, that are left out: divided in a way no
/// mapping to another text's verses has been established for, so a shared address would be a guess.
/// </param>
internal sealed record DeclaredVersePair(string From, string To, IReadOnlySet<int> Without)
{
    /// <summary>
    /// In a book the frame has no rules for, join only the chapters where both texts print the same
    /// verses. There the shared address is all there is to go on, and a chapter one edition divides
    /// otherwise would pair every verse after the first difference with the wrong one.
    /// </summary>
    public bool AgreeingChaptersOnly { get; init; }
}

/// <summary>
/// Which verse of one text is which verse of another — the statement one level above a word link,
/// and the one that has to exist first.
///
/// The schema declares <c>verse_link</c> and nothing was writing it. That is not a cosmetic hole. A
/// word link may name words in two verses on purpose, because that is how *the word ended up
/// elsewhere* is said, and the verification check that separates a legitimate crossing from a wrong
/// one asks whether a verse link joins the two verses. With the table empty the check could only
/// ever report every crossing as a fault, so it was measuring the emptiness of this table rather
/// than the correctness of the links.
///
/// <para>
/// The first crossings it found, on 2026-09-03, turned out to be wrong links rather than legitimate
/// ones: Nestle's Philippians 1:16 and 1:17 stand in the opposite order to the Textus Receptus, the
/// frame learned to say so, and the word links between the two editions had been built under the
/// older frame and still paired 1:17 with 1:17. Deleting and rebuilding them is what fixed those.
/// This table is what will tell the difference the next time — and it is the verse-level
/// correspondence in its own right, which is the layer word alignment rests on.
/// </para>
///
/// <para>
/// It is derived from the canonical frame and then stored rather than queried each time, because
/// verse correspondence is what constrains word alignment and so has to exist first. Two
/// verses correspond when they stand at the same canonical address, and the correspondence is
/// transitive through the addresses they share: where one text divides a passage into two verses
/// and another into three, all five belong to one link rather than to some arbitrary pairing of
/// them. So this walks the shared addresses as a graph and takes its connected components, and a
/// component is one link naming a set against a set — the same shape as <c>link</c>, one level up.
/// </para>
///
/// <para>
/// The method is <c>stated-by-source</c> and the confidence is therefore null, because the frame
/// comes from the versification data rather than from anything we inferred. Where a text's own
/// numbering has been identified rather than declared, that identification is itself stated: the
/// conditions it was tested against are in the file.
/// </para>
///
/// <para>
/// Pairs come from the word links that exist, so a pair the aligner has not run on yet has no verse
/// links either. That is deliberate — the table answers *is this crossing legitimate*, and a
/// question nobody has asked needs no answer. It also means the loader has to run after the
/// alignment commands rather than before, which it does: it is idempotent per pair, so the next
/// start picks up whatever a `compose` or an `align` added.
/// </para>
///
/// <para>
/// It is two steps, and <see cref="Cover"/> is the second. The components are built from the
/// address each verse primarily stands at; a verse that covers a further address is then joined to
/// what stands there in the other text. That second step runs on every start whether or not any
/// pair was built, because the frame can learn to say that a verse spans two rows long after the
/// verse links for its pair were written.
/// </para>
/// </summary>
internal sealed class VerseLinkLoader(AppDbContext db, ILogger<VerseLinkLoader> logger)
{
    private const string Source = "the canonical frame: the addresses the two texts share";

    /// <summary>
    /// Pairs that are joined verse by verse before any word of them is. A text that arrives with no
    /// word link to anything — the Ethiopic, until it is aligned — would otherwise stand beside
    /// nothing, although the frame already says which of its verses stands where another text's does.
    /// </summary>
    internal static IReadOnlyList<DeclaredVersePair> DeclaredPairs =>
        [.. GeezTextSource.VersePairs, .. DeuterocanonTextSource.VersePairs, .. AlexandrinusTextSource.VersePairs];

    private const string LinkImport =
        """
        COPY verse_link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string VerseImport =
        "COPY verse_link_verse (verse_link_id, verse_id, side) FROM STDIN (FORMAT BINARY)";

    /// <summary>
    /// For every verse standing at an address that is not its own, the verse at that address in the
    /// other text of each link the first one already belongs to, on the opposite side of it.
    ///
    /// Inserted on conflict rather than checked first, so a start that has nothing to add costs one
    /// statement and writes nothing.
    /// </summary>
    private const string CoveredVerseImport =
        """
        INSERT INTO verse_link_verse (verse_link_id, verse_id, side)
        SELECT DISTINCT member.verse_link_id,
               counterpart.id,
               CASE WHEN member.side = 'from' THEN 'to' ELSE 'from' END
        FROM verse_reference cover
        JOIN verse_link_verse member ON member.verse_id = cover.verse_id
        JOIN verse_link link ON link.id = member.verse_link_id
        JOIN verse_reference stands
          ON stands.is_primary
         AND stands.canonical_book = cover.canonical_book
         AND stands.canonical_chapter = cover.canonical_chapter
         AND stands.canonical_verse = cover.canonical_verse
        JOIN verse counterpart
          ON counterpart.id = stands.verse_id
         AND counterpart.text_id = CASE WHEN member.side = 'from' THEN link.to_text_id ELSE link.from_text_id END
        WHERE NOT cover.is_primary
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// The verse correspondences a source states through its word links, wherever the frame joins
    /// nothing.
    ///
    /// A person who says <em>this Spanish word renders that Hebrew word</em> has said, in the same
    /// breath, that the verse holding the one answers the verse holding the other. Usually the frame
    /// says it too and there is nothing to add. Where it does not, the source is the better witness:
    /// Clear Bible's Reina-Valera alignment crosses verse boundaries the frame refuses in the ten
    /// chapters where eBible left the Spanish at its own numbering and mapped the German. Without
    /// this the corpus holds a hand-made claim and reports it as a fault.
    ///
    /// <para>
    /// **Only <c>stated-by-source</c> links.** A model proposing a link across a verse boundary is
    /// not testifying to anything, and taking its word here would turn every stray alignment into a
    /// statement about versification. The source recorded is the one the word links carry, so a
    /// reader is answered with who said it rather than with the fact that it was derived.
    /// </para>
    ///
    /// <para>
    /// A <c>transposes</c> link is the other exception, whatever its method: it exists only to say
    /// that a passage one text writes here the other writes elsewhere, as the Samaritan writes the
    /// altar of incense after Exodus 26:35. The verse pair it names is the transposition itself, so
    /// it is written as one, with the link's own method and its lowest confidence.
    /// </para>
    /// </summary>
    private const string StatedVerseImport =
        """
        CREATE TEMP TABLE stated_verse_pair ON COMMIT DROP AS
        WITH crossing AS (
            SELECT l.from_text_id, l.to_text_id, fw.verse_id AS from_verse, tw.verse_id AS to_verse,
                   min(p.source) AS source,
                   bool_or(l.relation = 'transposes' AND l.method <> 'stated-by-source') AS transposed,
                   min(l.method) FILTER (WHERE l.relation = 'transposes') AS transposed_method,
                   min(l.confidence) FILTER (WHERE l.relation = 'transposes') AS transposed_confidence
            FROM link l
            JOIN provenance p ON p.id = l.provenance_id
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            JOIN word fw ON fw.id = f.word_id
            JOIN link_word t ON t.link_id = l.id AND t.side = 'to'
            JOIN word tw ON tw.id = t.word_id
            WHERE l.method = 'stated-by-source' OR l.relation = 'transposes'
            GROUP BY l.from_text_id, l.to_text_id, fw.verse_id, tw.verse_id
        )
        SELECT nextval(pg_get_serial_sequence('verse_link', 'id'))::int AS id, c.*
        FROM crossing c
        WHERE NOT EXISTS (
            SELECT 1 FROM verse_link_verse a
            JOIN verse_link_verse b ON b.verse_link_id = a.verse_link_id AND b.verse_id = c.to_verse
            WHERE a.verse_id = c.from_verse);

        INSERT INTO verse_link (id, from_text_id, to_text_id, relation, method, confidence, source, note)
        SELECT id, from_text_id, to_text_id,
               CASE WHEN transposed THEN 'transposes' ELSE 'renders' END,
               CASE WHEN transposed THEN transposed_method ELSE 'stated-by-source' END,
               CASE WHEN transposed THEN transposed_confidence END,
               source,
               CASE WHEN transposed THEN 'the passage these word links find written at another place'
                    ELSE 'the verse this source''s own word links put against that one' END
        FROM stated_verse_pair;

        INSERT INTO verse_link_verse (verse_link_id, verse_id, side)
        SELECT id, from_verse, 'from' FROM stated_verse_pair
        UNION ALL
        SELECT id, to_verse, 'to' FROM stated_verse_pair;

        SELECT count(*) FROM stated_verse_pair;
        """;

    public async Task<VerseLinkOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        var linked = await db.Links
            .Select(link => new { link.FromTextId, link.ToTextId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var declared = await Declared(cancellationToken);
        var aligned = linked.Select(pair => (pair.FromTextId, pair.ToTextId)).ToHashSet();
        var wanted = aligned
            .Union(declared.Keys)
            .ToList();

        var already = (await db.VerseLinks
                .Select(link => new { link.FromTextId, link.ToTextId })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(pair => (pair.FromTextId, pair.ToTextId))
            .ToHashSet();

        // A declared pair already joined is joined again in the books it has not been, which is how
        // the books a loaded text gains reach the texts it is declared against.
        var todo = new List<((int FromTextId, int ToTextId) Pair, IReadOnlySet<int> Joined)>();
        foreach (var pair in wanted.OrderBy(pair => pair.Item1).ThenBy(pair => pair.Item2))
        {
            if (!already.Contains(pair))
            {
                todo.Add((pair, new HashSet<int>()));
            }
            else if (declared.TryGetValue(pair, out var declaration))
            {
                var joined = await Joined(pair, cancellationToken);
                if (await Unjoined(pair, Outside(declaration, aligned.Contains(pair)), joined, cancellationToken))
                {
                    todo.Add((pair, joined));
                }
            }
        }

        var mapped = await Mapped(declared, cancellationToken);
        var geez = await db.Texts.Where(text => text.Slug == GeezTextSource.Slug)
            .Select(text => (int?)text.Id).SingleOrDefaultAsync(cancellationToken);

        var addresses = new Dictionary<int, Dictionary<(int, int, int), List<int>>>();
        var covers = new Dictionary<int, IReadOnlySet<(int, int, int)>>();
        int pairs = 0, straight = 0, divided = 0, alone = 0, written = 0;

        foreach (var (pair, joined) in todo)
        {
            var here = await Addressed(addresses, pair.FromTextId, cancellationToken);
            var there = await Addressed(addresses, pair.ToTextId, cancellationToken);
            if (declared.TryGetValue(pair, out var declaration))
            {
                var left = Outside(declaration, aligned.Contains(pair)).Union(joined).ToHashSet();
                here = here
                    .Where(address => !left.Contains(address.Key.Item1))
                    .ToDictionary(address => address.Key, address => address.Value);
                if (declaration.AgreeingChaptersOnly)
                {
                    (here, there) = Agreeing(
                        here,
                        there,
                        await Covered(covers, pair.FromTextId, cancellationToken),
                        await Covered(covers, pair.ToTextId, cancellationToken),
                        declaration.Without);
                }
            }

            var components = Components(here, there, ref alone);

            straight += components.Count(c => c.From.Count == 1 && c.To.Count == 1);
            divided += components.Count(c => c.From.Count != 1 || c.To.Count != 1);
            written += components.Count;
            pairs += components.Count > 0 ? 1 : 0;

            await Write(pair.FromTextId, pair.ToTextId, components, cancellationToken);
        }

        // A declared book no chapter of which the two print alike is asked about again on every load
        // and answers nothing, which is a pair already joined as far as it can be.
        if (pairs == 0)
        {
            var only = await Cover(cancellationToken);
            var alreadyStated = await Stated(cancellationToken);
            logger.LogInformation(
                "Every linked pair already has its verse links; {Covered} memberships added for the " +
                "addresses a verse covers, {Stated} verse pairs a source states through its word links, " +
                "{Mapped} read from a verse map",
                only, alreadyStated, mapped);
            return new VerseLinkOutcome(true, 0, mapped, 0, 0, 0, only, alreadyStated, started.Elapsed);
        }

        written += mapped;

        var outcome = new VerseLinkOutcome(
            false, pairs, written, straight, divided, alone, await Cover(cancellationToken),
            await Stated(cancellationToken), started.Elapsed);
        logger.LogInformation("Verse links: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The books a declared pair leaves out. A pair the aligner or a source has linked word by word is
    /// joined wherever those links reach, as every linked pair is, and its declaration only adds the
    /// books it names: the Synodal is declared against the King James for the books it gains, and read
    /// as the declaration alone its sixty-six were left unjoined the first time the pair was built
    /// afresh, beneath half a million word links.
    /// </summary>
    private static IReadOnlySet<int> Outside(DeclaredVersePair declaration, bool linked) =>
        linked ? new HashSet<int>() : declaration.Without;

    /// <summary>
    /// The two texts' addresses without the chapters, in books the frame has no rules for, that the
    /// two do not print alike. Everything else is kept as it was.
    /// </summary>
    /// <param name="hereCovers">
    /// The further addresses the first text's verses cover, which it prints as much as the ones they
    /// stand at: the Synodal's Prayer of Manasseh, read against the King James, stands at ten of its
    /// fifteen verses and covers the other five.
    /// </param>
    /// <param name="spared">Books never left out, which the pair is joined in whole.</param>
    internal static (Dictionary<(int, int, int), List<int>> Here, Dictionary<(int, int, int), List<int>> There)
        Agreeing(
            Dictionary<(int, int, int), List<int>> here,
            Dictionary<(int, int, int), List<int>> there,
            IReadOnlySet<(int, int, int)>? hereCovers = null,
            IReadOnlySet<(int, int, int)>? thereCovers = null,
            IReadOnlySet<int>? spared = null)
    {
        var differing = Differing(here.Keys.Concat(hereCovers ?? Empty), there.Keys.Concat(thereCovers ?? Empty), spared);

        Dictionary<(int, int, int), List<int>> Without(Dictionary<(int, int, int), List<int>> addresses) =>
            addresses
                .Where(address => !differing.Contains((address.Key.Item1, address.Key.Item2)))
                .ToDictionary(address => address.Key, address => address.Value);

        return (Without(here), Without(there));
    }

    private static readonly HashSet<(int, int, int)> Empty = [];

    /// <summary>
    /// The chapters of the first text, in books the frame has no rules for, where the two do not reach
    /// the same addresses.
    /// </summary>
    internal static HashSet<(int Book, int Chapter)> Differing(
        IEnumerable<(int, int, int)> here,
        IEnumerable<(int, int, int)> there,
        IReadOnlySet<int>? spared = null)
    {
        Dictionary<(int, int), HashSet<int>> Chapters(IEnumerable<(int, int, int)> addresses) =>
            addresses
                .Where(address => !BookCodes.Places(address.Item1) && spared?.Contains(address.Item1) != true)
                .GroupBy(address => (address.Item1, address.Item2))
                .ToDictionary(chapter => chapter.Key, chapter => chapter.Select(address => address.Item3).ToHashSet());

        var mine = Chapters(here);
        var theirs = Chapters(there);
        return mine.Keys
            .Where(chapter => !theirs.TryGetValue(chapter, out var verses) || !verses.SetEquals(mine[chapter]))
            .ToHashSet();
    }

    /// <summary>
    /// The chapters of <paramref name="fromSlug"/> its verse links to <paramref name="toSlug"/> leave
    /// out, under the name <see cref="TwinPassages"/> joins them at. A verse standing in one of them
    /// belongs to no verse link of the pair, so nothing joins it to the verse the other text stands at
    /// an address it only covers, and the aligner does not meet the two there. Empty for a pair joined
    /// wherever the two share an address.
    /// </summary>
    internal static async Task<IReadOnlySet<(int Book, int Chapter)>> Refused(
        AppDbContext db,
        string fromSlug,
        string toSlug,
        CancellationToken cancellationToken = default)
    {
        var declaration = DeclaredPairs.FirstOrDefault(pair => pair.From == fromSlug && pair.To == toSlug);
        if (declaration is not { AgreeingChaptersOnly: true })
        {
            return new HashSet<(int, int)>();
        }

        async Task<List<(int, int, int)>> Reached(string slug) =>
            (await db.VerseReferences
                .Where(reference => reference.Verse!.Text!.Slug == slug)
                .Select(reference => new
                {
                    reference.CanonicalBook,
                    reference.CanonicalChapter,
                    reference.CanonicalVerse,
                })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(reference => TwinPassages.Joined(
                (reference.CanonicalBook, reference.CanonicalChapter, reference.CanonicalVerse)))
            .ToList();

        return Differing(await Reached(fromSlug), await Reached(toSlug), declaration.Without);
    }

    /// <summary>The books of the first text a pair's verse links already reach.</summary>
    private async Task<IReadOnlySet<int>> Joined(
        (int FromTextId, int ToTextId) pair,
        CancellationToken cancellationToken) =>
        (await db.VerseLinkVerses
            .Where(member => member.Side == LinkSide.From
                             && member.VerseLink!.FromTextId == pair.FromTextId
                             && member.VerseLink.ToTextId == pair.ToTextId)
            .Select(member => member.Verse!.Book!.CanonicalOrdinal)
            .Distinct()
            .ToListAsync(cancellationToken))
        .ToHashSet();

    /// <summary>
    /// Whether both texts hold a book the pair is declared for and not yet joined in. A book only the
    /// first holds has nothing to be joined to, and would otherwise be asked about on every load.
    /// </summary>
    private async Task<bool> Unjoined(
        (int FromTextId, int ToTextId) pair,
        IReadOnlySet<int> without,
        IReadOnlySet<int> joined,
        CancellationToken cancellationToken)
    {
        var books = await db.Books
            .Where(book => book.TextId == pair.FromTextId)
            .Select(book => book.CanonicalOrdinal)
            .Intersect(db.Books.Where(book => book.TextId == pair.ToTextId).Select(book => book.CanonicalOrdinal))
            .ToListAsync(cancellationToken);
        return books.Any(book => !without.Contains(book) && !joined.Contains(book));
    }

    /// <summary>
    /// The verse links a verse map reads for the books the frame places at their own numbers, for
    /// every declared pair of the mapped text that has none yet: each line of the map joins its
    /// verses to whatever the other text holds at the rows the line names. They are written as the
    /// reading they are, with the line's confidence, and not as the frame's statement.
    /// </summary>
    private async Task<int> Mapped(
        Dictionary<(int FromTextId, int ToTextId), DeclaredVersePair> declared,
        CancellationToken cancellationToken)
    {
        var geez = await db.Texts.Where(text => text.Slug == GeezTextSource.Slug)
            .Select(text => (int?)text.Id).SingleOrDefaultAsync(cancellationToken);
        var pairs = declared.Keys.Where(pair => pair.FromTextId == geez).ToList();
        if (geez is null || pairs.Count == 0 || GeezVerseMap.Lines.Count == 0)
        {
            return 0;
        }

        var own = (await db.Verses
                .Where(verse => verse.TextId == geez)
                .Select(verse => new { Book = verse.Book!.CanonicalOrdinal, verse.ChapterNumber, verse.Number, verse.Id })
                .ToListAsync(cancellationToken))
            .ToDictionary(verse => (verse.Book, verse.ChapterNumber, verse.Number), verse => verse.Id);
        var slugs = await db.Texts.ToDictionaryAsync(text => text.Id, text => text.Slug, cancellationToken);
        var addresses = new Dictionary<int, Dictionary<(int, int, int), List<int>>>();
        var written = 0;

        foreach (var pair in pairs)
        {
            if (await db.VerseLinks.AnyAsync(
                    link => link.FromTextId == pair.FromTextId && link.ToTextId == pair.ToTextId
                            && link.Method == LinkMethod.ModelReading,
                    cancellationToken))
            {
                continue;
            }

            var there = await Addressed(addresses, pair.ToTextId, cancellationToken);
            var components = new List<(Component Verses, double Confidence)>();
            foreach (var line in GeezVerseMap.Lines)
            {
                var from = line.From
                    .Select(verse => own.GetValueOrDefault((line.Book, verse.Chapter, verse.Verse)))
                    .Where(id => id != 0)
                    .ToList();
                var to = line.To
                    .Where(address => GeezTextSource.Aligns(slugs[pair.ToTextId], address.Book, address.Chapter, address.Verse))
                    .SelectMany(address => there.GetValueOrDefault(address) ?? [])
                    .Distinct()
                    .ToList();
                if (from.Count > 0 && to.Count > 0)
                {
                    components.Add((new Component(from, to), line.Confidence));
                }
            }

            await Write(pair.FromTextId, pair.ToTextId, [.. components.Select(c => c.Verses)], cancellationToken,
                new MappedVerses([.. components.Select(c => c.Confidence)], GeezVerseMap.Source));
            written += components.Count;
        }

        return written;
    }

    /// <summary>How a set of verse links was established, where it is not the frame's statement.</summary>
    private sealed record MappedVerses(IReadOnlyList<double> Confidences, string Source);

    /// <summary>
    /// The pairs joined verse by verse whether or not any word of them is linked yet, by the texts'
    /// ids, with the books of the first text left out of each. Only pairs whose two texts are both
    /// loaded; a declaration about a text this corpus does not hold says nothing.
    /// </summary>
    private async Task<Dictionary<(int FromTextId, int ToTextId), DeclaredVersePair>> Declared(
        CancellationToken cancellationToken)
    {
        var slugs = DeclaredPairs.SelectMany(pair => new[] { pair.From, pair.To }).Distinct().ToList();
        var ids = await db.Texts
            .Where(text => slugs.Contains(text.Slug))
            .ToDictionaryAsync(text => text.Slug, text => text.Id, cancellationToken);

        return DeclaredPairs
            .Where(pair => ids.ContainsKey(pair.From) && ids.ContainsKey(pair.To))
            .ToDictionary(pair => (ids[pair.From], ids[pair.To]));
    }

    /// <summary>
    /// Joins a verse that covers a second canonical address to whatever stands at that address in
    /// the other text.
    ///
    /// <para>
    /// The components above are built from the address each verse primarily stands at, which is
    /// what makes them a partition: every verse belongs to one of them, and a verse that covers a
    /// second address would otherwise pull two passages into one statement wherever the frame parks
    /// an address on a verse rather than placing it. So the skeleton is the primary addresses and
    /// this adds the coverage on top of it, which is also what lets it repair a corpus whose verse
    /// links were written before the frame learned to say that a verse spans two rows.
    /// </para>
    ///
    /// <para>
    /// **This is what makes a word link crossing a verse boundary legitimate.** BHSA writes Isaiah
    /// 63:19 as one verse where the English begins chapter 64 inside it, Nehemiah 7:68 where the
    /// English has 7:68 and 7:69, and Psalm 13:6 where the English has 13:5 and 13:6 — and the
    /// aligner, which buckets a verse by every address it stands at, proposes links across those
    /// boundaries because the frame says the verse is there. Without this the frame states the
    /// address and refuses to join it, and the verification reads the aligner's true answer as a
    /// fault. The psalm superscriptions are the same statement read the other way: a Slavic verse
    /// covers the title address, and the Hebrew's title verse is what stands there.
    /// </para>
    ///
    /// <para>
    /// The counterpart has to stand at the address <em>primarily</em>, which is the difference
    /// between a verse the other text really holds there and an address the versification data has
    /// parked absent material on. The King James' Esther 1:1 carries eighteen of the second kind,
    /// standing for the Greek additions it does not contain; joining one covering to another
    /// covering of the same empty address would state a correspondence between two absences.
    /// </para>
    /// </summary>
    /// <inheritdoc cref="StatedVerseImport"/>
    public async Task<int> Stated(CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var command = new NpgsqlCommand(StatedVerseImport, connection)
        {
            Transaction = (NpgsqlTransaction)transaction.GetDbTransaction(),
        };

        var written = (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return written;
    }

    public async Task<int> Cover(CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var command = new NpgsqlCommand(CoveredVerseImport, connection);

        // A verse one covering joins can cover a further address, which only the next pass sees.
        var added = 0;
        for (var pass = await command.ExecuteNonQueryAsync(cancellationToken);
             pass > 0;
             pass = await command.ExecuteNonQueryAsync(cancellationToken))
        {
            added += pass;
        }

        return added;
    }

    /// <summary>
    /// The connected components of the graph whose edges are *these two verses share a canonical
    /// address*.
    ///
    /// Union-find rather than a pairwise join, because the relation is transitive and a pairwise
    /// join would cut it: if the one text's verse 12 covers the other's 12 and 13, and its verse 13
    /// covers the other's 13 as well, all four are one statement. Taking them two at a time would
    /// write three links that each contradict the others about what corresponds to what.
    /// </summary>
    internal static List<Component> Components(
        Dictionary<(int, int, int), List<int>> here,
        Dictionary<(int, int, int), List<int>> there,
        ref int alone)
    {
        var parent = new Dictionary<long, long>();
        var side = new Dictionary<long, LinkSide>();

        // A verse is only *missing a counterpart* inside a book the other text has. Without this
        // the count is dominated by pairs that cover one testament, and reads as thirty thousand
        // faults where the truth is that the King James New Testament is not in BHSA.
        var shared = here.Keys.Select(address => address.Item1)
            .Intersect(there.Keys.Select(address => address.Item1))
            .ToHashSet();
        var books = new Dictionary<long, int>();

        long Key(int verseId, LinkSide which) => which == LinkSide.From ? verseId : -(long)verseId;

        long Find(long node)
        {
            while (parent[node] != node)
            {
                parent[node] = parent[parent[node]];
                node = parent[node];
            }

            return node;
        }

        void Add(int verseId, LinkSide which)
        {
            var key = Key(verseId, which);
            if (parent.TryAdd(key, key))
            {
                side[key] = which;
            }
        }

        void Union(long left, long right)
        {
            var a = Find(left);
            var b = Find(right);
            if (a != b)
            {
                parent[a] = b;
            }
        }

        // Every verse of both texts is a node, including the ones the other text never answers, so
        // that a verse with no counterpart is counted rather than quietly absent.
        foreach (var (address, verses) in here)
        {
            foreach (var verse in verses)
            {
                Add(verse, LinkSide.From);
                books[Key(verse, LinkSide.From)] = address.Item1;
            }
        }

        foreach (var (address, verses) in there)
        {
            foreach (var verse in verses)
            {
                Add(verse, LinkSide.To);
                books[Key(verse, LinkSide.To)] = address.Item1;
            }
        }

        foreach (var (address, mine) in here)
        {
            if (!there.TryGetValue(address, out var theirs))
            {
                continue;
            }

            // Every verse standing at this address is one statement, whichever text it belongs to.
            foreach (var verse in mine.Skip(1))
            {
                Union(Key(mine[0], LinkSide.From), Key(verse, LinkSide.From));
            }

            foreach (var verse in theirs)
            {
                Union(Key(mine[0], LinkSide.From), Key(verse, LinkSide.To));
            }
        }

        var grouped = new Dictionary<long, Component>();
        foreach (var node in parent.Keys)
        {
            var root = Find(node);
            if (!grouped.TryGetValue(root, out var component))
            {
                component = new Component([], []);
                grouped[root] = component;
            }

            (side[node] == LinkSide.From ? component.From : component.To).Add((int)Math.Abs(node));
        }

        var complete = new List<Component>(grouped.Count);
        foreach (var (root, component) in grouped)
        {
            if (component.From.Count > 0 && component.To.Count > 0)
            {
                complete.Add(component);
            }
            else if (shared.Contains(books[root]))
            {
                alone++;
            }
        }

        return complete;
    }

    /// <summary>
    /// Every verse of a text by the canonical address it primarily stands at, cached because a text
    /// takes part in several pairs and the query is the expensive half of this.
    ///
    /// The primary address alone, deliberately: it is the one address every verse has exactly one
    /// of, so the components come out as a partition of the two texts' verses. The further
    /// addresses a verse covers are joined afterwards by <see cref="Cover"/>, which adds a
    /// membership to the statement the covering verse is already part of instead of merging two
    /// passages into one.
    /// </summary>
    private async Task<Dictionary<(int, int, int), List<int>>> Addressed(
        Dictionary<int, Dictionary<(int, int, int), List<int>>> cache,
        int textId,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(textId, out var known))
        {
            return known;
        }

        var rows = await db.VerseReferences
            .Where(reference => reference.IsPrimary && reference.Verse!.TextId == textId)
            .Select(reference => new
            {
                reference.CanonicalBook,
                reference.CanonicalChapter,
                reference.CanonicalVerse,
                reference.VerseId,
            })
            .ToListAsync(cancellationToken);

        // A passage printed under two names is joined under one, so that an edition printing the Letter
        // of Jeremiah as Baruch 6 meets one printing it as a book of its own.
        var addressed = rows
            .GroupBy(row => TwinPassages.Joined((row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse)))
            .ToDictionary(group => group.Key, group => group.Select(row => row.VerseId).Distinct().ToList());

        cache[textId] = addressed;
        return addressed;
    }

    /// <summary>
    /// The further addresses a text's verses cover beyond the one each stands at, under the name
    /// <see cref="TwinPassages"/> joins them at.
    /// </summary>
    private async Task<IReadOnlySet<(int, int, int)>> Covered(
        Dictionary<int, IReadOnlySet<(int, int, int)>> cache,
        int textId,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(textId, out var known))
        {
            return known;
        }

        var covered = (await db.VerseReferences
                .Where(reference => !reference.IsPrimary && reference.Verse!.TextId == textId)
                .Select(reference => new
                {
                    reference.CanonicalBook,
                    reference.CanonicalChapter,
                    reference.CanonicalVerse,
                })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(reference => TwinPassages.Joined(
                (reference.CanonicalBook, reference.CanonicalChapter, reference.CanonicalVerse)))
            .ToHashSet();

        cache[textId] = covered;
        return covered;
    }

    /// <param name="mapped">What a verse map read, where these are not the frame's statement.</param>
    private async Task Write(
        int fromTextId,
        int toTextId,
        List<Component> components,
        CancellationToken cancellationToken,
        MappedVerses? mapped = null)
    {
        if (components.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveIds(connection, components.Count, cancellationToken);
        var relation = EnumSpelling.Of(LinkRelation.Equals);
        var method = EnumSpelling.Of(mapped is null ? LinkMethod.StatedBySource : LinkMethod.ModelReading);

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < components.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(relation, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                if (mapped is null)
                {
                    await writer.WriteNullAsync(cancellationToken);
                }
                else
                {
                    await writer.WriteAsync(mapped.Confidences[i], NpgsqlDbType.Double, cancellationToken);
                }

                await writer.WriteAsync(mapped?.Source ?? Source, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);

        await using (var writer = await connection.BeginBinaryImportAsync(VerseImport, cancellationToken))
        {
            for (var i = 0; i < components.Count; i++)
            {
                foreach (var verse in components[i].From)
                {
                    await Row(writer, firstId + i, verse, fromSide, cancellationToken);
                }

                foreach (var verse in components[i].To)
                {
                    await Row(writer, firstId + i, verse, toSide, cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task Row(
        NpgsqlBinaryImporter writer,
        int verseLinkId,
        int verseId,
        string side,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(verseLinkId, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync(verseId, NpgsqlDbType.Integer, cancellationToken);
        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
    }

    private static async Task<int> ReserveIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('verse_link', 'id'), " +
            "coalesce((SELECT max(id) FROM verse_link), 0) + @count) - @count + 1", connection);
        command.Parameters.AddWithValue("count", count);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    internal sealed record Component(List<int> From, List<int> To);
}
