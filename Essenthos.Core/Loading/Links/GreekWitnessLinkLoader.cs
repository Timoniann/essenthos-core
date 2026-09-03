using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.TextusReceptus;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

/// <param name="Same">Words the two editions write identically once accents are set aside.</param>
/// <param name="Differing">Words at the same place in the verse that are not the same word.</param>
/// <param name="Missing">
/// A word the second edition has and the first does not. This and <paramref name="Added"/> are the
/// interesting numbers and the reason to hold two Greek witnesses at all: they are the textual
/// variants, stated, and each says which edition lacks the word.
/// </param>
/// <param name="Added">A word the first edition has and the second does not.</param>
internal sealed record GreekWitnessOutcome(
    bool AlreadyLoaded,
    int Verses,
    int Links,
    int Same,
    int Differing,
    int Missing,
    int Added,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Greek witnesses are already linked"
            : $"{Links} links over {Verses} verses in {Elapsed}: {Same} the same word, {Differing} a " +
              $"different word in the same place, {Added} the first edition has and the second does not, " +
              $"{Missing} the second has and the first does not";
}

/// <summary>
/// The Greek witnesses to each other: Nestle 1904 against Scrivener's Textus Receptus.
///
/// Until this existed the two Greek panes were dark to one another. Every translation reached
/// both — the King James is linked to Nestle and to Scrivener, and so are the Synodal and the
/// Ukrainian — but Nestle and the Textus Receptus were linked to nothing in common, so hovering a
/// Greek word lit the translations and left the other Greek text alone. Two witnesses of the same
/// sentence, and the reader could compare either against English and neither against the other.
///
/// Scrivener is the hub on purpose. Stephanus is already linked to Scrivener word for word, so
/// linking Nestle to Scrivener alone joins all four Greek panes: a word's witness set carries the
/// ids it reaches, and both sides then hold Scrivener's.
///
/// The pairing is by Strong number within a verse, which both editions state for themselves — no
/// aligner, no statistics. Where a number stands once on each side the pairing is certain enough
/// to say so; where it repeats, the repeats are handed out in order, which is right far more often
/// than not and is recorded at a lower confidence because "far more often than not" is what it is.
/// The words no number answers are then read as variants where they stand at the same place, and
/// that one step is positional rather than stated, so it says <c>lexical</c> and says it cheaply.
/// </summary>
internal sealed class GreekWitnessLinkLoader(AppDbContext db, ILogger<GreekWitnessLinkLoader> logger)
{
    private const string Source = "the Strong numbers both editions carry, paired within each verse";

    private const string SubstitutionSource =
        "the words left over once the Strong numbers were paired, matched by their place in the verse";

    /// <summary>A number standing once on each side of the verse. There is nothing to choose between.</summary>
    private const double Unique = 0.95;

    /// <summary>A number standing more than once, handed out in the order it occurs.</summary>
    private const double Repeated = 0.85;

    /// <summary>
    /// Two words the editions do not share, standing at the same place in the verse. Position is
    /// the whole evidence, so this sits below every number match and well above nothing.
    /// </summary>
    private const double Substituted = 0.6;

    /// <summary>
    /// How far apart two leftovers may stand and still be one reading. The editions run word for
    /// word between variants, so a substitution moves a word by one at most; anything further is
    /// two unrelated absences that happened to fall in the same verse.
    /// </summary>
    private const int SamePlace = 1;

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    public async Task<GreekWitnessOutcome> Load(
        string fromSlug,
        string toSlug,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        if (await db.Links.AnyAsync(l => l.FromTextId == from && l.ToTextId == to, cancellationToken))
        {
            logger.LogInformation("{From} and {To} are already linked; nothing to do", fromSlug, toSlug);
            return new GreekWitnessOutcome(true, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var here = await Words(from, cancellationToken);
        var there = await Words(to, cancellationToken);

        var drafts = new List<GreekDraft>(150_000);
        var verses = 0;

        foreach (var (address, left) in here)
        {
            if (!there.TryGetValue(address, out var right))
            {
                continue;
            }

            verses++;
            Pair(left, right, drafts);
        }

        await Write(from, to, drafts, cancellationToken);

        var outcome = new GreekWitnessOutcome(
            false,
            verses,
            drafts.Count,
            drafts.Count(d => d.Relation == LinkRelation.Equals),
            drafts.Count(d => d.Relation == LinkRelation.Renders),
            drafts.Count(d => d.Relation == LinkRelation.Omits),
            drafts.Count(d => d.Relation == LinkRelation.Expands),
            started.Elapsed);

        logger.LogInformation("Linked {From} to {To}: {Outcome}", fromSlug, toSlug, outcome);
        return outcome;
    }

    /// <summary>
    /// One verse of each edition, paired by the Strong numbers they both state. A number on one
    /// side only is written positively as an absence rather than left as a hole — that is the
    /// variant, and recording it is the whole point.
    /// </summary>
    private static void Pair(List<GreekWord> left, List<GreekWord> right, List<GreekDraft> drafts)
    {
        var mine = left.Where(w => w.Strong is not null).GroupBy(w => w.Strong!)
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.Position).ToList());
        var yours = right.Where(w => w.Strong is not null).GroupBy(w => w.Strong!)
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.Position).ToList());

        var hereOnly = new List<Leftover>();
        var thereOnly = new List<Leftover>();

        foreach (var strong in mine.Keys.Union(yours.Keys))
        {
            var ours = mine.GetValueOrDefault(strong, []);
            var theirs = yours.GetValueOrDefault(strong, []);
            var shared = Math.Min(ours.Count, theirs.Count);
            var certainty = ours.Count == 1 && theirs.Count == 1 ? Unique : Repeated;

            for (var i = 0; i < shared; i++)
            {
                drafts.Add(new GreekDraft(
                    GreekLetters.Same(ours[i].Surface, theirs[i].Surface)
                        ? LinkRelation.Equals
                        : LinkRelation.Renders,
                    [ours[i].Id],
                    [theirs[i].Id],
                    certainty,
                    LinkMethod.StrongNumber));
            }

            hereOnly.AddRange(ours.Skip(shared).Select(word => new Leftover(word, certainty)));
            thereOnly.AddRange(theirs.Skip(shared).Select(word => new Leftover(word, certainty)));
        }

        Unpaired(hereOnly, thereOnly, drafts);
    }

    /// <summary>
    /// What is left once the numbers have been paired, which is where the variants are.
    ///
    /// Two leftovers standing at the same place in their verses are one reading written two ways —
    /// Matthew 1:10 has Ἀμώς where Scrivener has αμων, twice — and four independent absences say
    /// that far worse than two substitutions do. The place is the only evidence for it, so the link
    /// says <c>lexical</c> and carries a confidence well below a matched number.
    ///
    /// Everything else stands in one edition and not the other, and the side it is missing from is
    /// what the relation has to name: <c>omits</c> where the first edition lacks the word,
    /// <c>expands</c> where it has one the second does not.
    /// </summary>
    private static void Unpaired(List<Leftover> here, List<Leftover> there, List<GreekDraft> drafts)
    {
        here.Sort((a, b) => a.Word.Position.CompareTo(b.Word.Position));
        there.Sort((a, b) => a.Word.Position.CompareTo(b.Word.Position));

        var substituted = new HashSet<long>();
        for (var i = 0; i < here.Count && i < there.Count; i++)
        {
            if (Math.Abs(here[i].Word.Position - there[i].Word.Position) > SamePlace)
            {
                continue;
            }

            substituted.Add(here[i].Word.Id);
            substituted.Add(there[i].Word.Id);
            drafts.Add(new GreekDraft(
                LinkRelation.Renders,
                [here[i].Word.Id],
                [there[i].Word.Id],
                Substituted,
                LinkMethod.Lexical));
        }

        foreach (var leftover in here.Where(l => !substituted.Contains(l.Word.Id)))
        {
            drafts.Add(new GreekDraft(
                LinkRelation.Expands, [leftover.Word.Id], [], leftover.Certainty, LinkMethod.StrongNumber));
        }

        foreach (var leftover in there.Where(l => !substituted.Contains(l.Word.Id)))
        {
            drafts.Add(new GreekDraft(
                LinkRelation.Omits, [], [leftover.Word.Id], leftover.Certainty, LinkMethod.StrongNumber));
        }
    }

    private async Task<Dictionary<(int, int, int), List<GreekWord>>> Words(
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.TextId == textId)
            .SelectMany(r => r.Verse!.Words.Select(w => new
            {
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                w.Id,
                w.Position,
                w.Surface,
                w.StrongNumber,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(r => new GreekWord(r.Id, r.Position, r.Surface, r.StrongNumber))
                    .ToList());
    }

    private async Task Write(
        int fromTextId,
        int toTextId,
        List<GreekDraft> drafts,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveLinkIds(connection, drafts.Count, cancellationToken);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);

        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(
                    EnumSpelling.Of(drafts[i].Relation), NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(
                    EnumSpelling.Of(drafts[i].Method), NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(drafts[i].Confidence, NpgsqlDbType.Double, cancellationToken);
                await writer.WriteAsync(
                    drafts[i].Method == LinkMethod.Lexical ? SubstitutionSource : Source,
                    NpgsqlDbType.Text,
                    cancellationToken);
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

        // The claim that says this loader is the one asserting these links. Written here rather
        // than left to a backfill: a link with no claim is invisible to the agreement measure, and
        // the measure spent a day reporting the migration instead of the corpus. PRB-0198.
        await LinkClaims.Record(connection, transaction, firstId, drafts.Count, cancellationToken);

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

    private async Task<int> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.Where(t => t.Slug == slug).Select(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken) is var id and not 0
            ? id
            : throw new InvalidOperationException(
                $"The text \"{slug}\" must be loaded before it can be linked.");

    private sealed record GreekWord(long Id, int Position, string Surface, string? Strong);

    /// <summary>A word whose number the other edition did not answer, and how sure that is.</summary>
    private sealed record Leftover(GreekWord Word, double Certainty);

    private sealed record GreekDraft(
        LinkRelation Relation,
        List<long> From,
        List<long> To,
        double Confidence,
        LinkMethod Method);
}
