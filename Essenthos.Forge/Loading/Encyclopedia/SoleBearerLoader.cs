using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>Why a record still resting on the dataset that supplied it was not reached.</summary>
public enum SoleBearerRefusal
{
    None,

    /// <summary>
    /// Another record carries the name, so how many people bear it is a question — and it is the
    /// registers' question rather than this one's.
    /// </summary>
    ANameSomebodyElseCarries,

    /// <summary>
    /// A title or a description and no name at all: <em>Pharaoh</em>, <em>King of Bela</em>,
    /// <em>Wife of Lot</em>, <em>the angel of the LORD</em>. There is no headword to look up, and
    /// four Pharaohs share the label without sharing a name.
    /// </summary>
    NoNameToLookUp,

    /// <summary>The name is one, and nothing on it says which lexeme it is.</summary>
    NoStrongNumber,

    /// <summary>
    /// The name is attested only in Greek, where this lexicon states no part of speech and
    /// enumerates no bearers on any of its 5,523 entries. Nothing is missing from the parse: the
    /// witness the Hebrew half supplies does not exist on the Greek side at all.
    /// </summary>
    GreekOnly,

    /// <summary>A number the lexicon holds no entry for, so there is nothing to read.</summary>
    NoEntry,

    /// <summary>
    /// Strong parts the headword a common noun, a verb or a gentilic adjective, so his entry heads
    /// no name — <em>Anamim</em> is <c>n</c> and <em>Arvadite</em> is <c>a</c>, and the encyclopedia
    /// files a man under each.
    /// </summary>
    NotAName,

    /// <summary>The entry heads a name and numbers nobody under it.</summary>
    EnumeratesNobody,

    /// <summary>
    /// A clause under a part of speech that names two kinds at once, with no heading over it. Which
    /// of the two the clause is is not stated, and this pass does not decide it.
    /// </summary>
    AClauseNothingTags,

    /// <summary>
    /// The entry knows no bearer of this record's kind: <em>Jebus</em>, <em>Etam</em> and
    /// <em>Keilah</em> are towns to Strong and men to the encyclopedia, and the disagreement is
    /// somebody's to settle rather than this pass's to paper over.
    /// </summary>
    NoBearerOfThisKind,

    /// <summary>
    /// Several bearers of this kind under one part of speech — <em>Adam</em> the first man and the
    /// city in the Jordan valley, <em>Reuben</em> the son of Jacob and the tribe and the territory.
    /// One record is held, the entry names more than one thing, and which of them the record is is
    /// exactly what a register is for.
    /// </summary>
    SeveralBearers,

    /// <summary>No verse of this corpus attests the name, so there is nothing here to stand on.</summary>
    NoOccurrence,
}

/// <param name="Resting">Records that still rest on the dataset that supplied them.</param>
/// <param name="Reached">Records this corpus now derives for itself.</param>
/// <param name="Refused">
/// Why each of the rest was left alone, which is the number worth reading: it is the honest residue,
/// and a refusal here costs a line of provenance where a guess would cost a reader the wrong man.
/// </param>
internal sealed record SoleBearerOutcome(
    bool AlreadyLoaded,
    int Resting,
    int Reached,
    IReadOnlyDictionary<SoleBearerRefusal, int> Refused,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the sole bearers are already ours"
            : $"{Reached} of {Resting} records the datasets supplied are now this corpus's own — a "
              + "name nobody else carries, an entry of Strong's that heads it, one bearer numbered "
              + $"under it. {Resting - Reached} keep the provenance they had: "
              + string.Join(", ", Refused.OrderByDescending(r => r.Value).Select(r => $"{r.Value} {r.Key}"))
              + $", in {Elapsed}";
}

/// <summary>
/// The records nobody ever asked about: a name one page carries, an entry of Strong's that heads it,
/// and occurrences already loaded.
///
/// The two registers ran over shared names, because that is where the hard question is — how many
/// men are called Zechariah. A record whose name nobody else carries was never in that population,
/// so 1,379 of them still stood as a dataset's after both registers, and 1,368 of those carry a
/// Strong number and 1,365 carry verses. They are not records this corpus failed to reach. They were
/// never asked.
///
/// <para>
/// <strong>No model, and no run.</strong> Where a name is borne by one record the count question the
/// registers exist to answer does not arise, and what is left is the derivation both of them already
/// perform: Strong parts the headword a proper name of a kind, numbers the bearers under it, and
/// this corpus attests the name in verses of its own. The peoples were built exactly this way out of
/// the gentilics and needed nothing else. So this reads two columns of the lexicon and one index of
/// the encyclopedia, and it costs nothing.
/// </para>
///
/// <para>
/// <strong>The address does not change and the row is not replaced.</strong>
/// <see cref="EntityVerse"/>, <see cref="EntityRelationship"/>, <see cref="EntityDescriptor"/>,
/// <see cref="WordEntity"/> and the reader's own URLs all key on the record, so nothing here writes
/// to any of them, adds a record or takes one away. What changes is <see cref="Entity.Source"/> and
/// the claims beside it — who the record rests on, and why.
/// </para>
///
/// <para>
/// <strong>What it cannot reach, it says why about.</strong> A title is not a name and four Pharaohs
/// share one; the Greek half of the lexicon states no part of speech on any entry it holds; Strong
/// parts <em>Anamim</em> a common noun and <em>Arvadite</em> an adjective while the encyclopedia
/// files a man under each; and where one entry lists a man and the town named after him under one
/// part of speech, which of them a record is is a register's question. Each of those keeps the
/// provenance it had, and <see cref="SoleBearerRefusal"/> is the account a reader is owed.
/// </para>
///
/// <para>
/// Idempotent on the claim this pass writes, which is the one thing that cannot be true before it
/// has run.
/// </para>
/// </summary>
internal sealed class SoleBearerLoader(AppDbContext db, ILogger<SoleBearerLoader> logger)
{
    /// <summary>
    /// What a record reached here says about itself. The record is ours — which of Strong's
    /// headwords a page of this encyclopedia stands under is nobody's statement but this corpus's —
    /// and the entry it is made of is his.
    /// </summary>
    private const string FromTheEntry =
        "Essenthos, from the names Strong's Dictionary heads for one bearer";

    /// <summary>
    /// What establishes a record, and why it is an inference rather than testimony. Nobody states
    /// that this page is the man H8348 heads; it is concluded from the part of speech, the one
    /// numbered clause under it, and an index of every name this corpus holds.
    /// </summary>
    private const LinkMethod ByTheEntry = LinkMethod.Lexical;

    /// <summary>
    /// The room three things agreeing leave. Strong parts the headword a name of this kind, numbers
    /// exactly one bearer under it, and nothing else in this corpus carries the name — and what is
    /// short of certainty is that a dictionary of words is not a census.
    /// </summary>
    private const double Settled = 0.95;

    /// <summary>
    /// The prefix every record this project asserts carries, as against one it merely carries for
    /// somebody. The same word <see cref="OwnRecordLoader"/> writes, so a reader filtering on it
    /// still gets one answer.
    /// </summary>
    private const string Ours = "Essenthos";

    /// <summary>The kind of label a namesake index is built of.</summary>
    private const string ProperName = "proper name";

    private const char Hebrew = 'H';

    private const char Greek = 'G';

    /// <summary>
    /// The separator a name row joins the numbers of a title's words with. A value carrying one is
    /// several lexemes and not this record's name, so it is passed over rather than split — the same
    /// refusal <see cref="EntityCandidates"/> makes of the same column.
    /// </summary>
    private const char Joined = ',';

    public async Task<SoleBearerOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        if (await db.EntityClaims.AnyAsync(c => c.Source == FromTheEntry, cancellationToken))
        {
            logger.LogInformation("The sole bearers are already ours; nothing to do");
            return new SoleBearerOutcome(
                true, 0, 0, new Dictionary<SoleBearerRefusal, int>(), started.Elapsed);
        }

        var carriers = await Carriers(cancellationToken);
        var entries = await db.StrongEntries
            .Select(e => new { e.StrongNumber, e.Morphology, e.DetailedDefinition })
            .ToListAsync(cancellationToken);
        var lexicon = entries.ToDictionary(
            e => e.StrongNumber,
            e => new StrongLine(e.StrongNumber, e.Morphology, e.DetailedDefinition),
            StringComparer.Ordinal);

        var resting = await db.Entities
            .Where(e => (e.Kind == EntityKind.Person || e.Kind == EntityKind.Place)
                        && !e.Source.StartsWith(Ours))
            .Include(e => e.Names)
            .Include(e => e.Verses)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var refused = new Dictionary<SoleBearerRefusal, int>();
        var reached = 0;

        foreach (var entity in resting.OrderBy(e => e.Id))
        {
            if (Read(entity, carriers, lexicon, out var refusal) is not { } bearer)
            {
                refused[refusal] = refused.GetValueOrDefault(refusal) + 1;
                continue;
            }

            entity.Claims.Add(new EntityClaim
            {
                Method = LinkMethod.StatedBySource,
                Confidence = null,
                Source = entity.Source,
                Note = "holds this one as a record of its own, which is where this record's "
                       + "relationships, verses and descriptors come from and whose they stay",
            });

            entity.Source = FromTheEntry;
            entity.Claims.Add(new EntityClaim
            {
                Method = ByTheEntry,
                Confidence = Settled,
                Source = FromTheEntry,
                Note = Says(entity, bearer),
            });

            reached++;
        }

        await db.SaveChangesAsync(cancellationToken);

        var outcome = new SoleBearerOutcome(false, resting.Count, reached, refused, started.Elapsed);
        logger.LogInformation("Reached the sole bearers: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>One entry as this pass reads it, which is two of its columns and no more.</summary>
    private sealed record StrongLine(string Number, string? Morphology, string? DetailedDefinition);

    /// <summary>The name a record was reached under, and Strong's numbered clause for each entry heading it.</summary>
    private sealed record SoleBearer(string Label, IReadOnlyList<(string Number, StrongBearer Clause)> Entries);

    /// <summary>
    /// The bearer this record is, or null with the reason it was left alone.
    ///
    /// Every name row is read and every one must answer the same way. A record carrying two names is
    /// two headwords, and a headword that heads two men makes the record one of two whichever of its
    /// names is read — so the strictest of its names decides, and one that cannot be read at all
    /// refuses the record rather than being skipped in favour of a name that can.
    /// </summary>
    private static SoleBearer? Read(
        Entity entity,
        IReadOnlyDictionary<string, int> carriers,
        IReadOnlyDictionary<string, StrongLine> lexicon,
        out SoleBearerRefusal refusal)
    {
        var names = entity.Names.Where(name => name.Kind == ProperName).ToList();
        if (names.Count == 0)
        {
            refusal = SoleBearerRefusal.NoNameToLookUp;
            return null;
        }

        foreach (var label in names.Select(name => name.Label).Append(entity.Name).Distinct(StringComparer.Ordinal))
        {
            if (carriers.GetValueOrDefault(label) > 1)
            {
                refusal = SoleBearerRefusal.ANameSomebodyElseCarries;
                return null;
            }
        }

        if (entity.Verses.Count == 0)
        {
            refusal = SoleBearerRefusal.NoOccurrence;
            return null;
        }

        var hebrew = Numbers(names, Hebrew);
        if (hebrew.Count == 0)
        {
            refusal = Numbers(names, Greek).Count > 0
                ? SoleBearerRefusal.GreekOnly
                : SoleBearerRefusal.NoStrongNumber;
            return null;
        }

        var clauses = new List<(string, StrongBearer)>();
        foreach (var number in hebrew)
        {
            if (!lexicon.TryGetValue(number, out var entry))
            {
                refusal = SoleBearerRefusal.NoEntry;
                return null;
            }

            if (!StrongNameEntries.HeadsAName(entry.Morphology, entry.DetailedDefinition))
            {
                refusal = SoleBearerRefusal.NotAName;
                return null;
            }

            var bearers = StrongNameEntries.Bearers(entry.Morphology, entry.DetailedDefinition);
            if (bearers.Count == 0)
            {
                refusal = SoleBearerRefusal.EnumeratesNobody;
                return null;
            }

            if (bearers.Any(bearer => bearer.Kind is null))
            {
                refusal = SoleBearerRefusal.AClauseNothingTags;
                return null;
            }

            var mine = bearers.Where(bearer => bearer.Kind == Wanted(entity.Kind)).ToList();
            if (mine.Count == 0)
            {
                refusal = SoleBearerRefusal.NoBearerOfThisKind;
                return null;
            }

            if (mine.Count > 1)
            {
                refusal = SoleBearerRefusal.SeveralBearers;
                return null;
            }

            clauses.Add((number, mine[0]));
        }

        refusal = SoleBearerRefusal.None;
        return new SoleBearer(entity.Name, clauses);
    }

    /// <summary>
    /// What Strong has to have numbered for this record to be the one he numbered. A people is not
    /// asked for: this pass runs over the persons and the places, and the peoples are already this
    /// corpus's own out of the gentilics.
    /// </summary>
    private static StrongNameKind Wanted(EntityKind kind) =>
        kind == EntityKind.Place ? StrongNameKind.Place : StrongNameKind.Person;

    /// <summary>
    /// Why this record exists, in a sentence a reader can weigh: which entry, what the entry says,
    /// and how much of this corpus stands behind it.
    /// </summary>
    private static string Says(Entity entity, SoleBearer bearer)
    {
        var read = string.Join("; ", bearer.Entries.Select(e => $"{e.Number} \"{e.Clause.Says}\""));
        var verses = entity.Verses.Count == 1 ? "verse" : "verses";
        return $"{bearer.Label} — {read}. Strong heads the name and numbers one bearer under it, no "
               + $"other record of this corpus carries the name, and {entity.Verses.Count} {verses} "
               + "of this corpus attest it";
    }

    /// <summary>
    /// The single-lexeme numbers a record's names carry in one language. A comma-joined value is the
    /// numbers of the words of a title rather than the name's own, and reading one would put
    /// <em>king</em>'s entry on the man called king of it.
    /// </summary>
    private static IReadOnlyList<string> Numbers(IEnumerable<EntityName> names, char language) =>
        names
            .Select(name => language == Hebrew ? name.HebrewStrongNumber : name.GreekStrongNumber)
            .Where(number => number is { Length: > 0 } && !number.Contains(Joined))
            .Select(number => number!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// How many records carry each label, so that a name borne once can be told from a name borne
    /// twice.
    ///
    /// A record's own <see cref="Entity.Name"/> counts as well as its name rows, and that is not
    /// belt and braces: 180 of the 181 records this corpus writes for itself carry no
    /// <c>entity_name</c> row at all, so an index built out of that table alone would
    /// call a name unique that one of them already bears.
    /// </summary>
    private async Task<Dictionary<string, int>> Carriers(CancellationToken cancellationToken)
    {
        var labelled = await db.EntityNames
            .Where(n => n.Kind == ProperName)
            .Select(n => new { n.EntityId, n.Label })
            .ToListAsync(cancellationToken);

        var named = await db.Entities
            .Select(e => new { EntityId = e.Id, Label = e.Name })
            .ToListAsync(cancellationToken);

        return labelled
            .Concat(named)
            .GroupBy(row => row.Label, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.EntityId).Distinct().Count(),
                StringComparer.Ordinal);
    }
}
