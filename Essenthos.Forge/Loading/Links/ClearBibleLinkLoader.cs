using System.Diagnostics;
using System.Text.RegularExpressions;
using Essenthos.Core.ClearBible;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Corpus;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Corroborated">
/// Records naming a link the corpus already holds. These write no link at all — they add a claim,
/// which is the whole point: a word pair two independent people arrived at is worth more than one
/// either of them arrived at alone, and until <c>link_claim</c> existed there was nowhere to say so.
/// </param>
/// <param name="Added">Records naming words no link joined, which become links of their own.</param>
/// <param name="Contradicted">
/// Records whose witness word the corpus already links to *different* words of the translation.
/// Written, because a disagreement between two people who both looked is a fact about the
/// translation and the most interesting row in the corpus — not an error to be resolved by whoever
/// loaded second.
/// </param>
/// <param name="Unresolved">
/// Records naming a word this corpus could not place — a verse neither text has, or a word inside a
/// verse the two editions write too differently to line up. A record is resolved whole or not at
/// all: half of it would be a claim about fewer words than the person made, which is a different
/// claim.
/// </param>
/// <param name="WithoutCounterpart">
/// The part of <paramref name="Unresolved"/> refused because a source word it names stands in a
/// verse the two editions were laid against each other in and has no word of ours: a word their
/// edition has and ours has not, or a different word in its place. Nothing is guessed for them.
/// </param>
/// <param name="Astray">
/// Records refused because they pair the translation's verse with the source verse of the same
/// number where the file's own token list and the canonical frame both say the translation's verse
/// renders another: in the Reina-Valera, the Hebrew verse that happens to carry the Spanish verse's
/// number where the two divide a chapter differently. See <see cref="ClearBibleLinkLoader.Astray"/>.
/// </param>
/// <param name="Shifted">
/// Records refused because they put a source word on the translation's <em>and</em> that the word
/// after it renders. See <see cref="ClearBibleLinkLoader.Shifted"/>.
/// </param>
/// <param name="Misnumbered">
/// Records refused because they stand in a verse where the translation's words, counted the way the
/// records count them, put some record on a mark of punctuation or past the verse's last word: past
/// that point the numbers and the words no longer agree. See
/// <see cref="ClearBibleLinkLoader.Retokenised"/>.
/// </param>
/// <param name="Overruled">
/// Records naming a witness word this project's reading of the translation says the record is wrong
/// about, as <see cref="LinkRulings"/> records it: withheld where it was the record's only witness
/// word, and otherwise written without it (<paramref name="Trimmed"/>).
/// </param>
/// <param name="Placed">
/// Their words that became ours, on each side. It is the measure of the join itself rather than of
/// the alignment, and it is what says whether an empty result means the two disagree or means
/// nothing was ever compared.
/// </param>
internal sealed record ClearBibleOutcome(
    bool AlreadyLoaded,
    int Records,
    int Corroborated,
    int Added,
    int Contradicted,
    int Unresolved,
    int WithoutCounterpart,
    int Astray,
    int Shifted,
    int Misnumbered,
    int Overruled,
    int Trimmed,
    ClearBiblePlacement Placed,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "Clear Bible has already spoken about this pair"
            : $"{Records} records in {Elapsed}: {Corroborated} corroborate a link the corpus already " +
              $"holds ({(Records == 0 ? 0 : (double)Corroborated / Records):P1}), {Added} name words " +
              $"nothing joined, {Contradicted} disagree with a link already here, {Unresolved} could " +
              $"not be resolved to words on both sides ({WithoutCounterpart} of them naming a source word " +
              $"the witness has no counterpart for), {Astray} refused for pairing verses by number where the " +
              $"file and the frame both say they do not answer each other, {Shifted} refused for putting a " +
              $"word on the 'and' before the word that renders it, {Misnumbered} refused in verses whose words "
              + $"the records number otherwise than the text does, {Overruled} naming a word a ruling on the translation "
              + $"sets aside ({Trimmed} written without it, the rest withheld); {Placed}";
}

/// <param name="SourceWords">Their source words this corpus could name a word of its own for.</param>
/// <param name="SourceTotal">Their source words altogether.</param>
/// <param name="Verses">Verses the two sides could be lined up in at all.</param>
/// <param name="Refused">Verses where they could not, so every record in them is unresolved.</param>
/// <param name="Realigned">
/// Source verses where some word of theirs is not our word of the same number: a word one edition
/// has and the other has not, and every word after it. Each is a verse counting positions would have
/// joined wrongly.
/// </param>
internal readonly record struct ClearBiblePlacement(
    int SourceWords,
    int SourceTotal,
    int TargetWords,
    int TargetTotal,
    int Verses,
    int Refused,
    int Realigned)
{
    public override string ToString() =>
        $"{SourceWords} of {SourceTotal} source words and {TargetWords} of {TargetTotal} target words " +
        $"were placed, over {Verses} verses, {Refused} of which would not line up and {Realigned} of " +
        "whose source words do not all stand at the number the file gives them";
}

/// <summary>
/// Clear Bible's hand-made alignments: a second opinion on the Berean, and the only opinion anybody
/// has published on the Reina-Valera.
///
/// <para>
/// **The Berean.** Its own publisher states which English word renders which Greek word, and that is
/// loaded. Clear Bible's team answered the same question about the same translation, without
/// consulting them. Measured before this was written, over the 7,925 verses where the two
/// tokenise the text identically: of 115,016 Greek words both name, **96.6% get exactly the same
/// English words**, 1.1% overlap, 2.3% share none. So this mostly writes nothing: where the two
/// agree it adds a claim to the link that is already there, and the link's own method and source do
/// not change — the Berean stated it first and still states it. What changes is that the link can
/// now say two people arrived at it, which is the cheapest evidence this corpus has and the thing
/// it threw away for as long as a method that spoke second was simply not recorded.
/// </para>
///
/// <para>
/// **The Reina-Valera.** Nothing else states a single Spanish correspondence. `process = "manual"`,
/// CC BY 4.0, whole Bible, and keyed by eBible identifier to the exact file this corpus loads. It
/// is <c>stated-by-source</c> and carries no confidence, which is the strongest thing this corpus
/// can say about a pair of words and is said here because a person made the claim, not because the
/// claim is beyond question — <see cref="ClearBibleSet"/> records how the identity was checked, and
/// the same repository's Russian set, believed on its label, turned out to name a punctuation mark
/// as the Russian word in 12,550 of its 89,248 records and to drift further with every one after.
/// </para>
///
/// <para>
/// Where two sources disagree, both answers are kept. A second link naming different words is not a
/// duplicate and does not trip the check that catches those — that check is about two links naming
/// *the same* words, which is agreement stored as rivalry. Two people disagreeing about which word
/// renders which is a fact about translation, and the corpus should hold it rather than pick.
/// </para>
///
/// <para>
/// Their Russian set is in the same download and is not loaded by anything: its records do not
/// correspond to the token file shipped beside them.
/// </para>
/// </summary>
internal sealed partial class ClearBibleLinkLoader(AppDbContext db, ILogger<ClearBibleLinkLoader> logger)
{
    /// <summary>
    /// How much of a verse the two editions have to write the same way before their words are put in
    /// step at all. Below it the verse is refused whole and every record in it is unresolved: an
    /// alignment laid on a verse the two sides do not share is a claim about the wrong words, and it
    /// would look exactly like the correct ones.
    /// </summary>
    private const double SameVerse = 0.5;

    public async Task<ClearBibleOutcome> Load(
        string directory,
        ClearBibleSet set,
        LinkRulings? rulings = null,
        CancellationToken cancellationToken = default)
    {
        var from = await db.Texts.SingleOrDefaultAsync(t => t.Slug == set.From, cancellationToken);
        var to = await db.Texts.SingleOrDefaultAsync(t => t.Slug == set.To, cancellationToken);

        if (from is null || to is null)
        {
            return Nothing();
        }

        var alignment = Path.Combine(directory, set.Alignment);
        var target = Path.Combine(directory, set.Target);
        var source = Path.Combine(directory, set.Source);

        var retokenised = set.Numbering == ClearBibleNumbering.Retokenised;
        if (!File.Exists(alignment) || (!retokenised && !File.Exists(target)) || !File.Exists(source))
        {
            logger.LogWarning(
                "Clear Bible's {Alignment} is not under {Directory}, so nothing is loaded from it. It is "
                + "fetched rather than committed; scripts/fetch-clearbible.ps1 says from where",
                set.Alignment, directory);
            return Nothing();
        }

        if (await db.LinkClaims.AnyAsync(
                c => c.Provenance!.Source == set.Statement && c.Link!.FromTextId == from.Id, cancellationToken))
        {
            logger.LogInformation("Clear Bible has already spoken about {From} and {To}", set.From, set.To);
            return Nothing();
        }

        var started = Stopwatch.StartNew();
        var placement = new Placement();
        List<ClearBibleToken> tokens;
        Dictionary<string, List<long>> theirTarget;
        if (retokenised)
        {
            (tokens, theirTarget) = await Retokenised(from.Id, placement.Target, cancellationToken);
        }
        else
        {
            tokens = [.. ClearBibleAlignment.Tokens(target)];
            theirTarget = await Placed(
                from, ClearBibleJoin.Letters, tokens, ClearBibleAlignment.Word, placement.Target, cancellationToken);
        }

        var theirSource = await Placed(
            to, set.Join, [.. ClearBibleAlignment.Tokens(source)], ClearBibleAlignment.Unit, placement.Source,
            cancellationToken);

        var spokenFor = await SpokenFor(from.Id, to.Id, cancellationToken);
        var renders = Renders(tokens);
        var targetFrame = await Frame(from.Id, cancellationToken);
        var sourceFrame = set.Join == ClearBibleJoin.Edition ? null : await Frame(to.Id, cancellationToken);
        var all = ClearBibleAlignment.Records(alignment).ToList();
        var shift = set.Shift is { } and
            ? new Shift(Ands(tokens, and.And), Joining(source, and.Also), Named(all))
            : null;
        var misnumbered = retokenised ? Misnumbered(all, tokens) : [];
        var overruled = await (rulings ?? LinkRulings.None).Overruled(
            db, set.From, set.To, LinkRulings.ClearBible, logger, cancellationToken);

        var drafts = new List<Draft>();
        int records = 0, corroborated = 0, added = 0, contradicted = 0, unresolved = 0, withoutCounterpart = 0;
        int astray = 0, shifted = 0, refused = 0, overruledRecords = 0, trimmed = 0;

        foreach (var record in all)
        {
            records++;
            if (record.Target.Any(id => misnumbered.Contains(ClearBibleAlignment.Verse(id) ?? 0)))
            {
                refused++;
                continue;
            }

            if (Astray(record, renders, targetFrame, sourceFrame))
            {
                astray++;
                continue;
            }

            if (shift is not null && Shifted(record, shift.Ands, shift.Joining, shift.Named))
            {
                shifted++;
                continue;
            }

            var witness = Ours(record.Source, theirSource, ClearBibleAlignment.Unit);
            var translation = Ours(record.Target, theirTarget, ClearBibleAlignment.Word);

            if (witness.Count == 0 || translation.Count == 0)
            {
                unresolved++;
                if (record.Source.Any(id => placement.Source.WithoutCounterpart.Contains(ClearBibleAlignment.Unit(id))))
                {
                    withoutCounterpart++;
                }

                continue;
            }

            var (kept, lost) = LinkRulings.Trim(witness, overruled);
            if (lost)
            {
                overruledRecords++;
                if (!kept)
                {
                    continue;
                }

                trimmed++;
            }

            drafts.Add(new Draft(translation, witness));
        }

        var written = await Write(from.Id, to.Id, set.Statement, drafts, cancellationToken);
        for (var i = 0; i < drafts.Count; i++)
        {
            // A record naming the words a link already names is a second opinion on it. A witness
            // word the corpus already joins to different words of the translation is two people who
            // both looked, disagreeing: counted apart from a plain addition because the two mean
            // different things about the corpus and a single total would hide it.
            if (!written.Fresh[i])
            {
                corroborated++;
            }
            else if (drafts[i].To.Any(spokenFor.Contains))
            {
                contradicted++;
            }
            else
            {
                added++;
            }
        }

        var outcome = new ClearBibleOutcome(
            false,
            records,
            corroborated,
            added,
            contradicted,
            unresolved,
            withoutCounterpart,
            astray,
            shifted,
            refused,
            overruledRecords,
            trimmed,
            placement.Read(),
            started.Elapsed);
        logger.LogInformation("Clear Bible on {From} against {To}: {Outcome}", set.From, set.To, outcome);
        return outcome;
    }

    /// <summary>
    /// Everything one set said, removed so that the next <see cref="Load"/> says it again under the
    /// join as it now stands: the links it wrote, the claims it added to links somebody else wrote, and
    /// the verse links its word links stated where the frame joins nothing. The links themselves stay
    /// where another source stated them first.
    /// </summary>
    public async Task<int> Withdraw(ClearBibleSet set, CancellationToken cancellationToken = default)
    {
        var from = await db.Texts.Where(t => t.Slug == set.From).Select(t => t.Id).SingleAsync(cancellationToken);
        var to = await db.Texts.Where(t => t.Slug == set.To).Select(t => t.Id).SingleAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claims = await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM link_claim c
            USING link l
            WHERE c.link_id = l.id AND l.from_text_id = {0} AND l.to_text_id = {1}
              AND c.provenance_id IN (SELECT id FROM provenance WHERE source = {2})
              AND l.provenance_id NOT IN (SELECT id FROM provenance WHERE source = {2})
            """,
            [from, to, set.Statement], cancellationToken);
        var links = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM link WHERE from_text_id = {0} AND to_text_id = {1} " +
            "AND provenance_id IN (SELECT id FROM provenance WHERE source = {2})",
            [from, to, set.Statement], cancellationToken);
        var verses = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM verse_link WHERE from_text_id = {0} AND to_text_id = {1} AND source = {2}",
            [from, to, set.Statement], cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Withdrew what Clear Bible said about {From} and {To}: {Links} links, {Claims} claims on links "
            + "another source stated and {Verses} verse links its word links stated", set.From, set.To, links,
            claims, verses);
        return links + claims + verses;
    }

    private static ClearBibleOutcome Nothing() =>
        new(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, default, TimeSpan.Zero);

    /// <summary>
    /// Whether a record pairs the translation's verse with the source verse of the same number where
    /// the two number it differently. That is the one way these hand-made alignments go wrong
    /// verse-wide: the Reina-Valera's Numbers 13:19, which the file itself and the frame both say
    /// renders the Hebrew 13:18, has its words linked to the Hebrew 13:19, and in the chapters where
    /// the Spanish runs a verse behind the English its words are linked to the Hebrew verse the
    /// English numbers alike.
    ///
    /// All three have to hold. The file's list of the verses a token renders is kept verse by verse,
    /// so a Spanish verse that opens with the last words of the Hebrew verse before it is listed
    /// against the next one and its correct links would look astray; and it is keyed to one edition
    /// where the alignment may be to another, as the Berean's Greek numbers Acts 19:41 where the list
    /// does not. The frame is this project's reading of the versification data. Where the file and
    /// the frame both put the verses apart and the record's source verse is merely the one carrying
    /// the translation verse's own number, printed or placed, the record is the odd one out. A record
    /// either of them says nothing about is kept.
    /// </summary>
    internal static bool Astray(
        ClearBibleRecord record,
        IReadOnlyDictionary<string, (int First, int Last)> renders,
        IReadOnlyDictionary<int, HashSet<int>> targetFrame,
        IReadOnlyDictionary<int, HashSet<int>>? sourceFrame)
    {
        var sources = record.Source.Select(ClearBibleAlignment.Verse).OfType<int>().ToHashSet();
        var own = record.Target.Select(ClearBibleAlignment.Verse).OfType<int>().ToHashSet();
        var said = new List<(int First, int Last)>(record.Target.Count);
        foreach (var id in record.Target)
        {
            if (!renders.TryGetValue(ClearBibleAlignment.Word(id), out var range))
            {
                return false;
            }

            said.Add(range);
        }

        if (sources.Count == 0 || said.Count == 0 ||
            sources.Any(verse => said.Any(range => range.First <= verse && verse <= range.Last)))
        {
            return false;
        }

        var here = Canonical(own, targetFrame);
        var there = sourceFrame is null ? sources : Canonical(sources, sourceFrame);
        return here.Count > 0 && there.Count > 0 && !here.Overlaps(there)
               && (sources.Overlaps(own) || there.Overlaps(own));
    }

    /// <summary>
    /// Whether a record puts a source word on the translation's <em>and</em> where the word after the
    /// <em>and</em> renders it. The Reina-Valera's set does this about five thousand times in the Old
    /// Testament: Genesis 1:4's וַיַּרְא, <em>and he saw</em>, is on the <em>Y</em> of <em>Y vió</em>,
    /// and <em>vió</em> is on nothing. The set leaves the Hebrew ו unaligned almost everywhere, so
    /// this is not a ו misread as the verb; it is the verb, the noun or the particle after it put one
    /// word too early.
    ///
    /// The <em>and</em> has to be the record's only target word, no source word may be a conjunction
    /// or a particle the translation renders with <em>and</em>, and the word after the <em>and</em>
    /// has to be named by no record. The record is refused rather than moved: which of the words
    /// after the <em>and</em> renders the source word is a question the record does not answer — for
    /// a verb it is mostly the next word, for a noun often the one after the article — and an answer
    /// this loader picked would be stored as the set's.
    /// </summary>
    internal static bool Shifted(
        ClearBibleRecord record,
        IReadOnlyDictionary<string, string?> ands,
        IReadOnlySet<string> joining,
        IReadOnlySet<string> named) =>
        record is { Target.Count: 1, Source.Count: > 0 }
        && ands.TryGetValue(ClearBibleAlignment.Word(record.Target[0]), out var next)
        && next is not null
        && !named.Contains(next)
        && !record.Source.Any(id => joining.Contains(ClearBibleAlignment.Unit(id)));

    /// <summary>
    /// The translation's tokens that are its word for <em>and</em>, each with the token after it in
    /// its verse, punctuation left out, or null where it ends the verse.
    /// </summary>
    internal static Dictionary<string, string?> Ands(string tokens, IReadOnlySet<string> and) =>
        Ands(ClearBibleAlignment.Tokens(tokens), and);

    /// <inheritdoc cref="Ands(string, IReadOnlySet{string})"/>
    internal static Dictionary<string, string?> Ands(IEnumerable<ClearBibleToken> tokens, IReadOnlySet<string> and)
    {
        var ands = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? pending = null;
        int? verse = null;
        foreach (var token in tokens)
        {
            if (token.Excluded)
            {
                continue;
            }

            var id = ClearBibleAlignment.Word(token.Id);
            var here = ClearBibleAlignment.Verse(id);
            if (pending is not null && here == verse)
            {
                ands[pending] = id;
            }

            pending = and.Contains(token.Text.ToLowerInvariant()) ? id : null;
            verse = here;
            if (pending is not null)
            {
                ands[pending] = null;
            }
        }

        return ands;
    }

    /// <summary>
    /// The source edition's words a translation's <em>and</em> can render: its conjunctions, and the
    /// particles whose Strong numbers are in <paramref name="also"/>.
    /// </summary>
    internal static HashSet<string> Joining(string tokens, IReadOnlySet<int> also)
    {
        var joining = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in ClearBibleAlignment.Tokens(tokens))
        {
            if (token.Part is "conjunction" or "conj" || (Lemma(token.Strong) is { } lemma && also.Contains(lemma)))
            {
                joining.Add(ClearBibleAlignment.Unit(token.Id));
            }
        }

        return joining;
    }

    /// <summary>A Strong number's number however the file writes it: <c>1571</c>, <c>0637a</c>, <c>G2532</c>.</summary>
    private static int? Lemma(string? strong) =>
        int.TryParse(strong.AsSpan().TrimStart("GHgh").TrimEnd("abcdefghijklmnopqrstuvwxyz"), out var number)
            ? number
            : null;

    internal static HashSet<string> Named(IEnumerable<ClearBibleRecord> records) =>
        records.SelectMany(record => record.Target).Select(ClearBibleAlignment.Word).ToHashSet(StringComparer.Ordinal);

    /// <param name="Ands">See <see cref="ClearBibleLinkLoader.Ands"/>.</param>
    /// <param name="Joining">See <see cref="ClearBibleLinkLoader.Joining"/>.</param>
    /// <param name="Named">Every target word some record names.</param>
    private sealed record Shift(
        Dictionary<string, string?> Ands,
        HashSet<string> Joining,
        HashSet<string> Named);

    private static HashSet<int> Canonical(IEnumerable<int> printed, IReadOnlyDictionary<int, HashSet<int>> frame)
    {
        var addresses = new HashSet<int>();
        foreach (var verse in printed)
        {
            if (frame.TryGetValue(verse, out var placed))
            {
                addresses.UnionWith(placed);
            }
        }

        return addresses;
    }

    /// <summary>The source verses the target file says each of its tokens renders, by word id.</summary>
    private static Dictionary<string, (int First, int Last)> Renders(IEnumerable<ClearBibleToken> tokens)
    {
        var renders = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (token.Renders is { } range)
            {
                renders[ClearBibleAlignment.Word(token.Id)] = range;
            }
        }

        return renders;
    }

    /// <summary>
    /// Every canonical address the frame places each of a text's verses at, keyed by the verse the
    /// text prints, both written as <see cref="ClearBibleAlignment.Verse(string)"/> writes them.
    /// </summary>
    private async Task<Dictionary<int, HashSet<int>>> Frame(int textId, CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(reference => reference.Verse!.TextId == textId)
            .Select(reference => new
            {
                reference.Verse!.Book!.CanonicalOrdinal,
                reference.Verse.ChapterNumber,
                reference.Verse.Number,
                reference.CanonicalBook,
                reference.CanonicalChapter,
                reference.CanonicalVerse,
            })
            .ToListAsync(cancellationToken);

        var frame = new Dictionary<int, HashSet<int>>();
        foreach (var row in rows)
        {
            var printed = ClearBibleAlignment.Verse(row.CanonicalOrdinal, row.ChapterNumber, row.Number);
            if (!frame.TryGetValue(printed, out var placed))
            {
                frame[printed] = placed = [];
            }

            placed.Add(ClearBibleAlignment.Verse(row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse));
        }

        return frame;
    }

    /// <summary>
    /// Their word ids as ours, dropping a record whose words this corpus does not hold. A record
    /// half-resolved would be a claim about fewer words than the person made, which is a different
    /// claim.
    /// </summary>
    private static List<long> Ours(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, List<long>> placed,
        Func<string, string> key)
    {
        var resolved = new List<long>(ids.Count);
        foreach (var id in ids)
        {
            if (!placed.TryGetValue(key(id), out var words))
            {
                return [];
            }

            foreach (var word in words)
            {
                if (!resolved.Contains(word))
                {
                    resolved.Add(word);
                }
            }
        }

        return resolved;
    }

    /// <summary>
    /// The translation's words, numbered the way a set whose token file does not match its records
    /// numbers them, each number with the word of ours it falls in.
    ///
    /// The numbering is the text's, not a join: each verse is counted from its first word, a word is
    /// divided after an elided article or pronoun — <em>l’</em> and <em>un</em> are two — a
    /// hyphenated word stays one, and every mark of punctuation counts as one. A number that falls on
    /// a mark belongs to no word, and <see cref="Misnumbered"/> refuses the verse it stands in.
    /// </summary>
    private async Task<(List<ClearBibleToken> Tokens, Dictionary<string, List<long>> Placed)> Retokenised(
        int textId,
        Counter counter,
        CancellationToken cancellationToken)
    {
        var words = await db.Words
            .Where(word => word.TextId == textId)
            .Select(word => new
            {
                word.Verse!.Book!.CanonicalOrdinal,
                word.Verse.ChapterNumber,
                word.Verse.Number,
                word.Position,
                word.Id,
                word.Surface,
                word.Trailer,
            })
            .ToListAsync(cancellationToken);

        var tokens = new List<ClearBibleToken>(words.Count * 2);
        var placed = new Dictionary<string, List<long>>(words.Count * 2, StringComparer.Ordinal);

        foreach (var verse in words
                     .GroupBy(word => (word.CanonicalOrdinal, word.ChapterNumber, word.Number))
                     .OrderBy(verse => verse.Key))
        {
            counter.Verses++;
            var at = 0;
            foreach (var word in verse.OrderBy(word => word.Position))
            {
                foreach (var (text, punctuation) in Pieces(word.Surface + word.Trailer))
                {
                    var id = $"{verse.Key.CanonicalOrdinal:00}{verse.Key.ChapterNumber:000}{verse.Key.Number:000}{++at:000}";
                    tokens.Add(new ClearBibleToken(id, text, punctuation, null));
                    counter.Total++;
                    if (!punctuation)
                    {
                        placed[id] = [word.Id];
                        counter.Placed++;
                    }
                }
            }
        }

        return (tokens, placed);
    }

    /// <summary>The pieces a word and the punctuation after it are counted as, in order.</summary>
    internal static IEnumerable<(string Text, bool Punctuation)> Pieces(string written)
    {
        foreach (Match piece in Piece().Matches(written))
        {
            yield return (piece.Value, !char.IsLetterOrDigit(piece.Value[0]));
        }
    }

    /// <summary>
    /// The verses, as <see cref="ClearBibleAlignment.Verse(string)"/> writes them, in which some record
    /// names a mark of punctuation or a number past the verse's last piece. Numbering goes wrong from
    /// the first place the records' text and ours divide differently — a comma one of them has, an
    /// elision one of them writes together — and every number after it in the verse is out by as
    /// much, so the whole verse is refused rather than the record that happened to show it.
    /// </summary>
    internal static HashSet<int> Misnumbered(IEnumerable<ClearBibleRecord> records, IEnumerable<ClearBibleToken> tokens)
    {
        var punctuation = new HashSet<string>(StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            known.Add(token.Id);
            if (token.Excluded)
            {
                punctuation.Add(token.Id);
            }
        }

        var misnumbered = new HashSet<int>();
        foreach (var id in records.SelectMany(record => record.Target).Select(ClearBibleAlignment.Word))
        {
            if ((punctuation.Contains(id) || !known.Contains(id)) && ClearBibleAlignment.Verse(id) is { } verse)
            {
                misnumbered.Add(verse);
            }
        }

        return misnumbered;
    }

    /// <summary>
    /// One piece of written text as the records count it: a word up to and including an elision's
    /// apostrophe, a hyphenated word whole, <em>aujourd’hui</em> whole, or a single mark.
    /// </summary>
    [GeneratedRegex(@"(?i:aujourd[’']hui)|[\p{L}\p{M}\p{N}]+(?:-[\p{L}\p{M}\p{N}]+)*[’']?|\S")]
    private static partial Regex Piece();

    /// <summary>
    /// Their tokens as our word ids, joined on the letters inside each verse.
    ///
    /// The two sides are numbered by different tokenisers, so a token is placed by what it is
    /// written with and not by where it stands. A run the two divide differently is one span naming
    /// every word on both sides of it — their <em>bə</em> and <em>rēʾšîṯ</em> against our two words,
    /// or their two tokens against our one — which is the truth about a division and the only thing
    /// that can be said without inventing a split.
    ///
    /// A token is keyed by <paramref name="key"/>, which for a source edition is everything its
    /// identifier names: each morpheme of the Westminster morphology is placed on its own, so the ו
    /// and the verb of <em>וַיֹּאמֶר</em> land on BHSA's two words and a record naming one of them is
    /// about that one.
    /// </summary>
    private async Task<Dictionary<string, List<long>>> Placed(
        Database.Entities.Text text,
        ClearBibleJoin join,
        IReadOnlyList<ClearBibleToken> tokens,
        Func<string, string> key,
        Counter counter,
        CancellationToken cancellationToken)
    {
        if (join == ClearBibleJoin.Edition)
        {
            return await Edition(text, tokens, key, counter, cancellationToken);
        }

        var ours = await Words(text.Id, text.Language, canonical: false, cancellationToken);
        var placed = new Dictionary<string, List<long>>(ours.Count * 20, StringComparer.Ordinal);

        foreach (var (address, theirs) in WithTheirTitles(Verses(tokens, text.Language, key), ours))
        {
            counter.Total += theirs.Sum(token => 1 + token.With.Count);
            if (!ours.TryGetValue(address, out var mine))
            {
                continue;
            }

            counter.Verses++;
            var spans = TaggedEdition.Align(
                [.. theirs.Select(token => token.Folded)],
                [.. mine.Select(word => word.Folded)]);

            if (TaggedEdition.Agreement(spans, mine.Count) < SameVerse)
            {
                counter.Refused++;
                continue;
            }

            foreach (var span in spans)
            {
                var words = new List<long>(span.CorpusTo - span.CorpusFrom);
                for (var at = span.CorpusFrom; at < span.CorpusTo; at++)
                {
                    words.Add(mine[at].Id);
                }

                for (var at = span.TaggedFrom; at < span.TaggedTo; at++)
                {
                    placed[theirs[at].Id] = words;
                    counter.Placed++;
                    foreach (var part in theirs[at].With)
                    {
                        placed[part] = words;
                        counter.Placed++;
                    }
                }
            }
        }

        return placed;
    }

    /// <summary>
    /// Their verses, with a psalm's title laid at the head of its first verse wherever they number the
    /// title as a verse of its own and ours prints it as the opening of verse 1. The Van Dyck's and
    /// the Indian Revised Version's token files number 721 and 973 words as a verse 0; left apart,
    /// their verse 1 would be laid against a verse of ours that opens with a title it does not have,
    /// and a long title is enough to refuse the verse.
    /// </summary>
    private static IEnumerable<((int, int, int) Address, List<Token> Tokens)> WithTheirTitles(
        IEnumerable<((int, int, int) Address, List<Token> Tokens)> verses,
        IReadOnlyDictionary<(int, int, int), List<Word>> ours)
    {
        List<Token>? title = null;
        var titled = (0, 0);
        foreach (var (address, tokens) in verses)
        {
            var (book, chapter, verse) = address;
            if (verse == 0 && !ours.ContainsKey(address) && ours.ContainsKey((book, chapter, 1)))
            {
                title = tokens;
                titled = (book, chapter);
                continue;
            }

            if (title is not null && verse == 1 && titled == (book, chapter))
            {
                yield return (address, [.. title, .. tokens]);
            }
            else
            {
                yield return (address, tokens);
            }

            title = null;
        }
    }

    /// <summary>
    /// Their words as ours where their source is another edition of a text the corpus holds, laid
    /// against it word for word inside each canonical verse. See <see cref="ClearBibleJoin.Edition"/>.
    /// </summary>
    private async Task<Dictionary<string, List<long>>> Edition(
        Database.Entities.Text text,
        IReadOnlyList<ClearBibleToken> tokens,
        Func<string, string> key,
        Counter counter,
        CancellationToken cancellationToken)
    {
        var ours = await Words(text.Id, text.Language, canonical: true, cancellationToken);
        var placed = new Dictionary<string, List<long>>(ours.Count * 20, StringComparer.Ordinal);

        foreach (var (address, theirs) in Verses(tokens, text.Language, key))
        {
            counter.Total += theirs.Count;
            if (!ours.TryGetValue(address, out var mine))
            {
                continue;
            }

            counter.Verses++;
            var counterparts = Counterparts(
                [.. theirs.Select(token => Form(token.Folded, token.Strong))],
                [.. mine.Select(word => Form(word.Folded, word.Strong))]);

            var realigned = theirs.Count != mine.Count;
            for (var at = 0; at < theirs.Count; at++)
            {
                realigned |= counterparts[at] != at;
                if (counterparts[at] < 0)
                {
                    counter.WithoutCounterpart.Add(theirs[at].Id);
                    continue;
                }

                placed[theirs[at].Id] = [mine[counterparts[at]].Id];
                counter.Placed++;
            }

            if (realigned)
            {
                counter.Realigned++;
            }
        }

        return placed;
    }

    /// <summary>
    /// Which of our words each of theirs is, or -1 where it is none of them.
    ///
    /// A word is the same word when the two editions write the same letters, when they state the same
    /// lemma number, or when the spelling is a letter or two apart and nothing says it is a different
    /// word. A word one edition has and the other has not has no counterpart, and neither has a word
    /// standing where the other edition prints a different one: the person who aligned theirs was
    /// looking at a word ours does not print, and what they said about it says nothing about the word
    /// ours prints instead.
    /// </summary>
    internal static int[] Counterparts(IReadOnlyList<WitnessForm> theirs, IReadOnlyList<WitnessForm> ours)
    {
        var counterparts = new int[theirs.Count];
        Array.Fill(counterparts, -1);

        foreach (var pairing in WitnessAlignment.Pair(theirs, ours))
        {
            if (pairing.From.Count != 1 || pairing.To.Count != 1)
            {
                continue;
            }

            var (mine, yours) = (theirs[pairing.From[0]], ours[pairing.To[0]]);
            var differentWord = pairing.Relation != LinkRelation.Equals
                                && mine.Lexeme.Length > 0
                                && yours.Lexeme.Length > 0
                                && mine.Lexeme != yours.Lexeme;

            if (!differentWord)
            {
                counterparts[pairing.From[0]] = pairing.To[0];
            }
        }

        return counterparts;
    }

    private static WitnessForm Form(string folded, string? strong) =>
        new(folded, GreekLemmaNumbers.Of(StrongNumbers.Normalize(strong)) ?? string.Empty);

    /// <summary>
    /// Their token file as verses, each in the order its identifiers number it, with the punctuation
    /// the file marks out of the alignment left out and everything folded to the letters it is
    /// compared by.
    ///
    /// The order is the identifiers' and not the file's because the two disagree in 781 verses of the
    /// Westminster morphology, where a word's rows stand after the next word's — in 1 Samuel 22:15
    /// <em>דָבָר</em> is listed before <em>בְּעַבְדּוֹ</em> — and laid in file order against BHSA
    /// their letters would be placed on the wrong words or on none.
    ///
    /// A pronominal suffix is compared as part of the morpheme before it, and placed with it, because
    /// BHSA never writes one apart. Standing alone, the ו of <em>בָנָיו</em> is matched to the next
    /// word's conjunction as readily as to its own word, and the words between are left unplaced.
    /// </summary>
    private static IEnumerable<((int, int, int) Address, List<Token> Tokens)> Verses(
        IReadOnlyList<ClearBibleToken> tokens,
        string language,
        Func<string, string> key)
    {
        var address = (0, 0, 0);
        var verse = new List<ClearBibleToken>(64);

        foreach (var token in tokens)
        {
            if (!ClearBibleAlignment.Address(token.Id, out var book, out var chapter, out var number))
            {
                continue;
            }

            if ((book, chapter, number) != address)
            {
                if (verse.Count > 0)
                {
                    yield return (address, Joined(verse, language, key));
                }

                address = (book, chapter, number);
                verse = [];
            }

            if (!token.Excluded)
            {
                verse.Add(token);
            }
        }

        if (verse.Count > 0)
        {
            yield return (address, Joined(verse, language, key));
        }
    }

    private static List<Token> Joined(List<ClearBibleToken> verse, string language, Func<string, string> key)
    {
        verse.Sort((one, other) => string.CompareOrdinal(key(one.Id), key(other.Id)));
        var tokens = new List<Token>(verse.Count);
        foreach (var token in verse)
        {
            var folded = Comparable(token.Text, language);
            if (token.Part == ClearBibleAlignment.Suffix
                && tokens.Count > 0
                && ClearBibleAlignment.Word(tokens[^1].Id) == ClearBibleAlignment.Word(token.Id))
            {
                tokens[^1] = tokens[^1] with
                {
                    Folded = tokens[^1].Folded + folded,
                    With = [.. tokens[^1].With, key(token.Id)],
                };
                continue;
            }

            tokens.Add(new Token(key(token.Id), folded, token.Strong, []));
        }

        return tokens;
    }

    /// <summary>
    /// The form two editions are compared by: the word's own letters, folded the way the corpus
    /// folds them for search, with the punctuation each edition attaches differently removed.
    ///
    /// It is <see cref="WordFolding"/> and not a second fold written here, so that a Hebrew word
    /// loses its points and a Greek word its accents by the same rule the reader searches by. What
    /// is added is dropping everything that is not a letter or a digit, because the two sides of
    /// this join disagree about punctuation by construction: their tokeniser makes a token of it and
    /// this reader hangs it on the word before.
    /// </summary>
    private static string Comparable(string text, string? language)
    {
        var folded = WordFolding.Fold(text, language);
        var letters = new System.Text.StringBuilder(folded.Length);
        foreach (var character in folded)
        {
            if (char.IsLetterOrDigit(character))
            {
                letters.Append(char.ToLowerInvariant(character));
            }
        }

        return letters.ToString();
    }

    /// <summary>
    /// The witness words some link of the pair already reaches, which is what separates *nobody had
    /// an answer for this word* from *somebody had a different one*. The aligner's are left out: a
    /// guess is not somebody's answer.
    /// </summary>
    private async Task<HashSet<long>> SpokenFor(
        int fromTextId,
        int toTextId,
        CancellationToken cancellationToken) =>
        (await db.LinkWords
            .Where(word => word.Side == LinkSide.To
                           && word.Link!.FromTextId == fromTextId
                           && word.Link!.ToTextId == toTextId
                           && word.Link!.Method != LinkMethod.Aligner)
            .Select(word => word.WordId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

    /// <param name="canonical">
    /// Whether to key by where the canonical frame places a verse or by the number the text prints
    /// for it. They differ wherever a Hebrew book is numbered the Hebrew way, and a file keyed to
    /// one and read by the other lands a whole chapter one verse out.
    /// </param>
    private async Task<Dictionary<(int, int, int), List<Word>>> Words(
        int textId,
        string language,
        bool canonical,
        CancellationToken cancellationToken)
    {
        var rows = canonical
            ? await db.VerseReferences
                .Where(reference => reference.IsPrimary && reference.Verse!.TextId == textId)
                .SelectMany(reference => reference.Verse!.Words.Select(word => new WordRow(
                    reference.CanonicalBook,
                    reference.CanonicalChapter,
                    reference.CanonicalVerse,
                    word.Position,
                    word.Id,
                    word.Surface,
                    word.StrongNumber)))
                .ToListAsync(cancellationToken)
            : await db.Words
                .Where(word => word.TextId == textId)
                .Select(word => new WordRow(
                    word.Verse!.Book!.CanonicalOrdinal,
                    word.Verse!.ChapterNumber,
                    word.Verse!.Number,
                    word.Position,
                    word.Id,
                    word.Surface,
                    word.StrongNumber))
                .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => (row.Book, row.Chapter, row.Verse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.Position)
                    .Select(row => new Word(row.Id, Comparable(row.Surface, language), row.StrongNumber))
                    .ToList());
    }

    private async Task<LinkWrite> Write(
        int fromTextId,
        int toTextId,
        string statement,
        List<Draft> drafts,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return new LinkWrite([], []);
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var written = await LinkWriter.Write(
            (NpgsqlConnection)db.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction(),
            [
                .. drafts.Select(draft => new NewLink(
                    fromTextId, toTextId, LinkRelation.Renders, LinkMethod.StatedBySource, null, statement, null,
                    draft.From, draft.To)),
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return written;
    }

    private sealed record WordRow(
        int Book,
        int Chapter,
        int Verse,
        int Position,
        long Id,
        string Surface,
        string? StrongNumber);

    private sealed record Word(long Id, string Folded, string? Strong);

    /// <param name="With">The morphemes compared and placed as part of this one: its suffixes.</param>
    private sealed record Token(string Id, string Folded, string? Strong, IReadOnlyList<string> With);

    private sealed record Draft(List<long> From, List<long> To);

    private sealed class Counter
    {
        public int Total { get; set; }

        public int Placed { get; set; }

        public int Verses { get; set; }

        public int Refused { get; set; }

        public int Realigned { get; set; }

        public HashSet<string> WithoutCounterpart { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Placement
    {
        public Counter Source { get; } = new();

        public Counter Target { get; } = new();

        public ClearBiblePlacement Read() => new(
            Source.Placed,
            Source.Total,
            Target.Placed,
            Target.Total,
            Math.Max(Source.Verses, Target.Verses),
            Source.Refused + Target.Refused,
            Source.Realigned);
    }
}
