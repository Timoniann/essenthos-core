using System.Diagnostics;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Macula;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <param name="Proper">
/// Words MACULA states are proper nouns. It is the figure this dataset was fetched for: the Greek
/// half of the naming pass had to infer that class from a capital letter in the lexicon, and this
/// is the first source in the corpus that says it outright about a word.
/// </param>
/// <param name="Drifted">
/// Words where the two copies of Nestle 1904 print different letters. Both spellings go into the
/// row's note. They are not a join failure — the position is still the position — but they are the
/// count that says how far this corpus's copy of the edition has drifted from MACULA's, and a load
/// where it moves is a load worth looking at.
/// </param>
internal sealed record MaculaOutcome(
    bool AlreadyLoaded,
    int Annotated,
    int Proper,
    int Drifted,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Greek already carries MACULA's annotation"
            : $"{Annotated} MACULA annotations on Nestle 1904 in {Elapsed}: {Proper} words stated " +
              $"to be proper nouns, {Drifted} where the two copies of the edition spell the word " +
              "differently";
}

/// <summary>
/// Puts MACULA Greek's word-level annotation on Nestle 1904, which is the first source in this
/// corpus that states which Greek words are names instead of leaving it to be worked out.
///
/// <para>
/// Nestle's own morphology says <c>noun</c> 28,394 times and never *proper noun*, so the pass that
/// annotates Greek words with the people and places they name had to build its gate out of the
/// lexicon's capital letter and measure it three ways before it could be trusted. MACULA writes
/// <c>type="proper"</c> on the word — 4,639 of them — and it writes it on these words: the same
/// edition, from the same upstream repository, addressed as <c>MAT 1:1!1</c>.
/// </para>
///
/// <para>
/// **So there is no join, and that is checked rather than asserted.** Both sides hold 7,943 verses
/// and 137,779 words and no verse differs in length. The loader compares every verse before it
/// writes a row and stops on the first that disagrees, because a source whose whole value is that
/// it needs no alignment must not be allowed to quietly acquire one: an off-by-one in Matthew would
/// otherwise put the rest of the book's annotations on the wrong words, each one looking exactly as
/// authoritative as the rest.
/// </para>
///
/// <para>
/// Every row is <see cref="LinkMethod.StatedBySource"/> and carries no confidence, which is the
/// difference from every other second opinion here. Nothing was inferred: MACULA named the word and
/// the word is ours. The <c>note</c> is written only where the two copies of the edition print
/// different letters, so a reader can see which 193 rows rest on a spelling that has drifted.
/// </para>
///
/// <para>
/// A pass of its own rather than part of the corpus loader, for the reason
/// <see cref="StatedNumberLoader"/> gives: the corpus loader returns early for a text already in
/// the text table, and Nestle is loaded in every database this will ever run against. Idempotent by
/// its own rows, so it costs one indexed existence check per boot.
/// </para>
/// </summary>
internal sealed class MaculaAnnotationLoader(AppDbContext db, ILogger<MaculaAnnotationLoader> logger)
{
    /// <summary>
    /// Named in every row. CC BY 4.0's one condition is this exact string, given verbatim in the
    /// licence, so the attribution is carried by the data rather than by a file somebody has to
    /// think to read. Which commit it was taken at is in <c>Resources/Macula/LICENCE.md</c>, once,
    /// rather than 137,779 times here.
    /// </summary>
    private const string Source =
        "MACULA Greek Linguistic Datasets, available at https://github.com/Clear-Bible/macula-greek/";

    /// <summary>The value the nominal type takes on a name, which is the whole point of the fetch.</summary>
    private const string Proper = "proper";

    private const string Import =
        """
        COPY word_parsing (word_id, lemma, morphology, method, confidence, source, note)
        FROM STDIN (FORMAT BINARY)
        """;

    public async Task<MaculaOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        if (await db.WordParsings.AnyAsync(p => p.Source == Source, cancellationToken))
        {
            logger.LogInformation("The Greek already carries MACULA's annotation; nothing to do");
            return new MaculaOutcome(true, 0, 0, 0, TimeSpan.Zero);
        }

        if (!File.Exists(MaculaReader.Path(folder)))
        {
            logger.LogInformation(
                "No MACULA word table under {Folder}; run scripts/fetch-macula.ps1 to fetch it. The " +
                "Greek keeps its own morphology and no word gains a stated proper-noun class",
                folder);
            return new MaculaOutcome(false, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var text = await db.Texts.SingleOrDefaultAsync(t => t.Slug == NestleTextSource.Slug, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"The text \"{NestleTextSource.Slug}\" must be loaded before MACULA's annotation of it " +
                       "can be stored. This reads its words; it does not create them.");

        var witness = await Witness(text.Id, cancellationToken);
        var annotation = MaculaReader.ReadFile(folder);
        Check(witness, annotation);

        var (annotated, proper, drifted) = await Write(witness, annotation, cancellationToken);

        var outcome = new MaculaOutcome(false, annotated, proper, drifted, started.Elapsed);
        logger.LogInformation("Put MACULA's annotation on the Greek: {Outcome}", outcome);
        return outcome;
    }

    /// <param name="Id">The word's key, kept beside the address so the write never re-queries.</param>
    private sealed record WitnessWord(long Id, int Book, int Chapter, int Verse, int Position, string Surface);

    private async Task<WitnessWord[]> Witness(int textId, CancellationToken cancellationToken)
    {
        var rows = await db.Words
            .Where(w => w.TextId == textId)
            .OrderBy(w => w.Verse!.Book!.CanonicalOrdinal)
            .ThenBy(w => w.Verse!.ChapterNumber)
            .ThenBy(w => w.Verse!.Number)
            .ThenBy(w => w.Position)
            .Select(w => new WitnessWord(
                w.Id,
                w.Verse!.Book!.CanonicalOrdinal,
                w.Verse!.ChapterNumber,
                w.Verse!.Number,
                w.Position,
                w.Surface))
            .ToArrayAsync(cancellationToken);

        return rows;
    }

    /// <summary>
    /// That the two really are the same words in the same order, which is the assumption every row
    /// this loader writes rests on.
    ///
    /// It is checked address by address rather than by comparing totals, because two files can hold
    /// the same number of words and disagree about where a verse ends — and the failure that would
    /// cause is the worst kind this corpus can produce: not an error, but 137,779 confident
    /// annotations, most of them on the wrong word.
    /// </summary>
    private static void Check(WitnessWord[] witness, IReadOnlyList<MaculaWord> annotation)
    {
        if (witness.Length != annotation.Count)
        {
            throw new InvalidDataException(
                $"Nestle 1904 holds {witness.Length} words here and MACULA's table holds " +
                $"{annotation.Count}. This dataset is loaded by position because it is published " +
                "over the same edition, so a difference in the total means it is not. Re-run " +
                "scripts/fetch-macula.ps1; if the counts still differ, the correspondence has to " +
                "be re-measured before anything is written.");
        }

        for (var at = 0; at < witness.Length; at++)
        {
            var ours = witness[at];
            var theirs = annotation[at];

            if (ours.Book != theirs.Book || ours.Chapter != theirs.Chapter
                || ours.Verse != theirs.Verse || ours.Position != theirs.Position)
            {
                throw new InvalidDataException(
                    $"MACULA's word {at + 1} is addressed {theirs.Book} {theirs.Chapter}:{theirs.Verse}" +
                    $"!{theirs.Position} where Nestle 1904's word {at + 1} is " +
                    $"{ours.Book} {ours.Chapter}:{ours.Verse}!{ours.Position}. The two copies of the " +
                    "edition have stopped agreeing about where a verse ends, so loading by position " +
                    "would put the rest of the book's annotations on the wrong words. Nothing was " +
                    "written. Re-run scripts/fetch-macula.ps1, and if the disagreement survives, the " +
                    "two editions need a join rather than an address.");
            }
        }
    }

    private async Task<(int Annotated, int Proper, int Drifted)> Write(
        WitnessWord[] witness,
        IReadOnlyList<MaculaWord> annotation,
        CancellationToken cancellationToken)
    {
        var method = EnumSpelling.Of(LinkMethod.StatedBySource);
        var proper = 0;
        var drifted = 0;

        // The transaction is what opens the connection as much as what makes the write atomic: a
        // binary import is issued on the connection rather than through the context, and the
        // context's own connection is closed between queries.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using (var writer = await connection.BeginBinaryImportAsync(Import, cancellationToken))
        {
            for (var at = 0; at < annotation.Count; at++)
            {
                var word = annotation[at];

                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(witness[at].Id, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(word.Lemma, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(Morphology(word), NpgsqlDbType.Jsonb, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteNullAsync(cancellationToken);
                await writer.WriteAsync(Source, NpgsqlDbType.Text, cancellationToken);

                // A note only where there is something to say. 137,586 copies of "the two agree"
                // would be most of the table and would say nothing.
                if (word.Surface == witness[at].Surface)
                {
                    await writer.WriteNullAsync(cancellationToken);
                }
                else
                {
                    drifted++;
                    await writer.WriteAsync(Note(witness[at], word), NpgsqlDbType.Text, cancellationToken);
                }

                if (word.Type == Proper)
                {
                    proper++;
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (annotation.Count, proper, drifted);
    }

    /// <summary>
    /// The features, under the keys <c>word.morphology</c> uses so that the two can be compared
    /// without a translation table between them, plus the two this corpus had no way to state.
    ///
    /// <c>class</c> is MACULA's part of speech, which separates the indeclinable numeral from the
    /// adjective, and <c>type</c> is the nominal type — <c>proper</c> against <c>common</c> — which
    /// is the field the whole dataset was fetched for. <c>morph</c> is the form code the annotation
    /// was read from, kept so that a disagreement with the word's own features can be traced to the
    /// reading rather than argued about: it is the same Sandborg-Petersen tag Nestle carries, so
    /// where the two differ one of them has misread a code both were looking at.
    /// </summary>
    private static string Morphology(MaculaWord word)
    {
        var features = new Dictionary<string, string>(12, StringComparer.Ordinal);

        Add(features, "class", word.Class);
        Add(features, "type", word.Type);
        Add(features, "morph", word.Morph);
        Add(features, "case", word.Case);
        Add(features, "number", word.Number);
        Add(features, "gender", word.Gender);
        Add(features, "tense", word.Tense);
        Add(features, "voice", word.Voice);
        Add(features, "mood", word.Mood);
        Add(features, "person", word.Person);
        Add(features, "degree", word.Degree);

        return JsonSerializer.Serialize(features, MorphologyJson);
    }

    private static readonly JsonSerializerOptions MorphologyJson = new() { WriteIndented = false };

    private static void Add(Dictionary<string, string> features, string name, string? value)
    {
        if (value is not null)
        {
            features[name] = value;
        }
    }

    private static string Note(WitnessWord witness, MaculaWord annotation) =>
        $"this corpus prints {witness.Surface} where MACULA prints {annotation.Surface}";
}
