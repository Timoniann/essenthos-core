using System.Diagnostics;
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
              $"the witness has no counterpart for); {Placed}";
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
internal sealed class ClearBibleLinkLoader(AppDbContext db, ILogger<ClearBibleLinkLoader> logger)
{
    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    private const string ClaimImport =
        """
        COPY link_claim (link_id, method, confidence, source, note)
        FROM STDIN (FORMAT BINARY)
        """;

    /// <summary>
    /// How much of a verse the two editions have to write the same way before their words are put in
    /// step at all. Below it the verse is refused whole and every record in it is unresolved: an
    /// alignment laid on a verse the two sides do not share is a claim about the wrong words, and it
    /// would look exactly like the correct ones.
    /// </summary>
    private const double SameVerse = 0.5;

    /// <summary>
    /// A withdrawal cascades through tens of thousands of link words and claims, which does not
    /// finish inside the default thirty seconds.
    /// </summary>
    private static readonly TimeSpan WithdrawTimeout = TimeSpan.FromMinutes(30);

    public async Task<ClearBibleOutcome> Load(
        string directory,
        ClearBibleSet set,
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

        if (!File.Exists(alignment) || !File.Exists(target) || !File.Exists(source))
        {
            logger.LogWarning(
                "Clear Bible's {Alignment} is not under {Directory}, so nothing is loaded from it. It is "
                + "fetched rather than committed; scripts/fetch-clearbible.ps1 says from where",
                set.Alignment, directory);
            return Nothing();
        }

        if (await db.LinkClaims.AnyAsync(
                c => c.Source == set.Statement && c.Link!.FromTextId == from.Id, cancellationToken))
        {
            logger.LogInformation("Clear Bible has already spoken about {From} and {To}", set.From, set.To);
            return Nothing();
        }

        var started = Stopwatch.StartNew();
        var placement = new Placement();
        var theirTarget = await Placed(from, ClearBibleJoin.Letters, target, placement.Target, cancellationToken);
        var theirSource = await Placed(to, set.Join, source, placement.Source, cancellationToken);

        var existing = await Shapes(from.Id, to.Id, cancellationToken);

        var claims = new List<long>();
        var drafts = new List<Draft>();
        int records = 0, corroborated = 0, added = 0, contradicted = 0, unresolved = 0, withoutCounterpart = 0;

        foreach (var record in ClearBibleAlignment.Records(alignment))
        {
            records++;
            var witness = Ours(record.Source, theirSource);
            var translation = Ours(record.Target, theirTarget);

            if (witness.Count == 0 || translation.Count == 0)
            {
                unresolved++;
                if (record.Source.Any(id => placement.Source.WithoutCounterpart.Contains(ClearBibleAlignment.Word(id))))
                {
                    withoutCounterpart++;
                }

                continue;
            }

            if (existing.Shapes.TryGetValue(Shape(translation, witness), out var link))
            {
                corroborated++;
                claims.Add(link);
                continue;
            }

            // A witness word the corpus already joins to different words of the translation: two
            // people who both looked, disagreeing. Counted apart from a plain addition because the
            // two mean different things about the corpus and a single total would hide it.
            if (witness.Any(existing.SpokenFor.Contains))
            {
                contradicted++;
            }
            else
            {
                added++;
            }

            drafts.Add(new Draft(translation, witness));
        }

        await Write(from.Id, to.Id, set.Statement, drafts, [.. claims.Distinct()], cancellationToken);

        var outcome = new ClearBibleOutcome(
            false,
            records,
            corroborated,
            added,
            contradicted,
            unresolved,
            withoutCounterpart,
            placement.Read(),
            started.Elapsed);
        logger.LogInformation("Clear Bible on {From} against {To}: {Outcome}", set.From, set.To, outcome);
        return outcome;
    }

    /// <summary>
    /// Everything one set said, removed so that the next <see cref="Load"/> says it again under the
    /// join as it now stands: the links it wrote, and the claims it added to links somebody else wrote.
    /// The links themselves stay where another source stated them first.
    /// </summary>
    public async Task<int> Withdraw(ClearBibleSet set, CancellationToken cancellationToken = default)
    {
        var from = await db.Texts.Where(t => t.Slug == set.From).Select(t => t.Id).SingleAsync(cancellationToken);
        var to = await db.Texts.Where(t => t.Slug == set.To).Select(t => t.Id).SingleAsync(cancellationToken);

        db.Database.SetCommandTimeout(WithdrawTimeout);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var claims = await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM link_claim c
            USING link l
            WHERE c.link_id = l.id AND l.from_text_id = {0} AND l.to_text_id = {1}
              AND c.source = {2} AND l.source <> {2}
            """,
            [from, to, set.Statement], cancellationToken);
        var links = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM link WHERE from_text_id = {0} AND to_text_id = {1} AND source = {2}",
            [from, to, set.Statement], cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Withdrew what Clear Bible said about {From} and {To}: {Links} links and {Claims} claims on "
            + "links another source stated", set.From, set.To, links, claims);
        return links + claims;
    }

    private static ClearBibleOutcome Nothing() =>
        new(true, 0, 0, 0, 0, 0, 0, default, TimeSpan.Zero);

    /// <summary>
    /// Their word ids as ours, dropping a record whose words this corpus does not hold. A record
    /// half-resolved would be a claim about fewer words than the person made, which is a different
    /// claim.
    /// </summary>
    private static List<long> Ours(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, List<long>> placed)
    {
        var resolved = new List<long>(ids.Count);
        foreach (var id in ids)
        {
            if (!placed.TryGetValue(ClearBibleAlignment.Word(id), out var words))
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
    /// Their tokens as our word ids, joined on the letters inside each verse.
    ///
    /// The two sides are numbered by different tokenisers, so a token is placed by what it is
    /// written with and not by where it stands. A run the two divide differently is one span naming
    /// every word on both sides of it — their <em>bə</em> and <em>rēʾšîṯ</em> against our two words,
    /// or their two tokens against our one — which is the truth about a division and the only thing
    /// that can be said without inventing a split.
    /// </summary>
    private async Task<Dictionary<string, List<long>>> Placed(
        Database.Entities.Text text,
        ClearBibleJoin join,
        string tokens,
        Counter counter,
        CancellationToken cancellationToken)
    {
        if (join == ClearBibleJoin.Edition)
        {
            return await Edition(text, tokens, counter, cancellationToken);
        }

        var ours = await Words(text.Id, text.Language, canonical: false, cancellationToken);
        var placed = new Dictionary<string, List<long>>(ours.Count * 20, StringComparer.Ordinal);

        foreach (var (address, theirs) in Verses(tokens, text.Language))
        {
            counter.Total += theirs.Count;
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
                }
            }
        }

        return placed;
    }

    /// <summary>
    /// Their words as ours where their source is another edition of a text the corpus holds, laid
    /// against it word for word inside each canonical verse. See <see cref="ClearBibleJoin.Edition"/>.
    /// </summary>
    private async Task<Dictionary<string, List<long>>> Edition(
        Database.Entities.Text text,
        string tokens,
        Counter counter,
        CancellationToken cancellationToken)
    {
        var ours = await Words(text.Id, text.Language, canonical: true, cancellationToken);
        var placed = new Dictionary<string, List<long>>(ours.Count * 20, StringComparer.Ordinal);

        foreach (var (address, theirs) in Verses(tokens, text.Language))
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
    /// Their token file as verses, in file order, with the punctuation the file marks out of the
    /// alignment left out and everything folded to the letters it is compared by.
    /// </summary>
    private static IEnumerable<((int, int, int) Address, List<Token> Tokens)> Verses(
        string path,
        string language)
    {
        var address = (0, 0, 0);
        var tokens = new List<Token>(64);

        foreach (var token in ClearBibleAlignment.Tokens(path))
        {
            if (!ClearBibleAlignment.Address(token.Id, out var book, out var chapter, out var verse))
            {
                continue;
            }

            if ((book, chapter, verse) != address)
            {
                if (tokens.Count > 0)
                {
                    yield return (address, tokens);
                }

                address = (book, chapter, verse);
                tokens = [];
            }

            if (token.Excluded)
            {
                continue;
            }

            tokens.Add(new Token(ClearBibleAlignment.Word(token.Id), Comparable(token.Text, language), token.Strong));
        }

        if (tokens.Count > 0)
        {
            yield return (address, tokens);
        }
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

    /// <summary>Every link of a pair by the words it names, so a second opinion can find it.</summary>
    private async Task<Existing> Shapes(
        int fromTextId,
        int toTextId,
        CancellationToken cancellationToken)
    {
        var rows = await db.LinkWords
            .Where(word => word.Link!.FromTextId == fromTextId && word.Link!.ToTextId == toTextId)
            .Select(word => new { word.LinkId, word.WordId, word.Side })
            .ToListAsync(cancellationToken);

        var shapes = new Dictionary<string, long>(rows.Count / 2, StringComparer.Ordinal);
        var spokenFor = new HashSet<long>(rows.Count / 2);

        foreach (var link in rows.GroupBy(row => row.LinkId))
        {
            var fromWords = link.Where(w => w.Side == LinkSide.From).Select(w => w.WordId).ToList();
            var toWords = link.Where(w => w.Side == LinkSide.To).Select(w => w.WordId).ToList();
            shapes[Shape(fromWords, toWords)] = link.Key;
            spokenFor.UnionWith(toWords);
        }

        return new Existing(shapes, spokenFor);
    }

    /// <param name="Shapes">Every link of the pair by the words it names.</param>
    /// <param name="SpokenFor">
    /// The witness words some link already reaches, which is what separates *nobody had an answer
    /// for this word* from *somebody had a different one*.
    /// </param>
    private sealed record Existing(Dictionary<string, long> Shapes, HashSet<long> SpokenFor);

    /// <summary>The words a link names, as one key. Order never enters a link, so it is sorted.</summary>
    private static string Shape(IEnumerable<long> from, IEnumerable<long> to) =>
        string.Join(',', from.Order()) + '|' + string.Join(',', to.Order());

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

    private async Task Write(
        int fromTextId,
        int toTextId,
        string statement,
        List<Draft> drafts,
        IReadOnlyList<long> corroborated,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0 && corroborated.Count == 0)
        {
            return;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var stated = EnumSpelling.Of(LinkMethod.StatedBySource);
        var renders = EnumSpelling.Of(LinkRelation.Renders);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);
        var firstId = drafts.Count == 0 ? 0 : await ReserveLinkIds(connection, drafts.Count, cancellationToken);

        if (drafts.Count > 0)
        {
            await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
            {
                for (var i = 0; i < drafts.Count; i++)
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                    await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteAsync(stated, NpgsqlDbType.Text, cancellationToken);
                    await writer.WriteNullAsync(cancellationToken);
                    await writer.WriteAsync(statement, NpgsqlDbType.Text, cancellationToken);
                }

                await writer.CompleteAsync(cancellationToken);
            }

            await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
            {
                for (var i = 0; i < drafts.Count; i++)
                {
                    foreach (var word in drafts[i].From)
                    {
                        await Row(writer, firstId + i, word, fromSide, cancellationToken);
                    }

                    foreach (var word in drafts[i].To)
                    {
                        await Row(writer, firstId + i, word, toSide, cancellationToken);
                    }
                }

                await writer.CompleteAsync(cancellationToken);
            }
        }

        // The links this loader wrote of its own get their claim the way every other loader's do,
        // copied from the link itself. Only the corroborations are written by hand here, because
        // those are claims on somebody else's link and there is nothing to copy them from.
        await LinkClaims.Record(connection, transaction, firstId, drafts.Count, cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(ClaimImport, cancellationToken))
        {
            foreach (var link in corroborated)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(link, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(stated, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteNullAsync(cancellationToken);
                await writer.WriteAsync(statement, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteNullAsync(cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task Row(
        NpgsqlBinaryImporter writer,
        long linkId,
        long wordId,
        string side,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(linkId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
    }

    private static async Task<long> ReserveLinkIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('link', 'id'), " +
            "coalesce((SELECT max(id) FROM link), 0) + @count) - @count + 1", connection);
        command.Parameters.AddWithValue("count", count);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
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

    private sealed record Token(string Id, string Folded, string? Strong);

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
