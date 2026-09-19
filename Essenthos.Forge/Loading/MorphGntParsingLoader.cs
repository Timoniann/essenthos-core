using System.Diagnostics;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.MorphGnt;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

/// <param name="Printed">Words the two editions print the same way, letter for letter.</param>
/// <param name="Spelled">
/// Words the two editions spell differently and that are the same word — almost all of them proper
/// names, which is where two editors have only convention to go on.
/// </param>
/// <param name="Unreached">
/// Words of Nestle 1904 that get no parsing, because the SBLGNT has nothing standing where they
/// stand. Reported rather than smoothed over: it is the count that says how far the two editions
/// are apart, and a load where it moves is a load worth looking at.
/// </param>
internal sealed record MorphGntOutcome(
    bool AlreadyLoaded,
    int Printed,
    int Spelled,
    int Unreached,
    int Unused,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Greek already carries MorphGNT's parsing"
            : $"{Printed + Spelled} parsings in {Elapsed}: {Printed} on words the two editions print " +
              $"alike, {Spelled} on words they spell differently, {Unreached} Nestle words the SBLGNT " +
              $"does not reach, {Unused} MorphGNT words with no Nestle word to sit on";
}

/// <summary>
/// Puts MorphGNT's morphology beside Nestle 1904's, word by word, so that the corpus holds two
/// analyses of the Greek New Testament instead of one.
///
/// <para>
/// One analysis cannot be checked. Nestle's own <c>case</c> attribute carried a gender for 20,772
/// words and the corpus could not notice, because nothing else had an opinion about those words;
/// it took a reader to find it. A second stated morphology makes that a query. It also fills in
/// what the first leaves out — 9,537 words gain a number, 9,534 a gender, 303 a degree the first
/// has no field for, and 4,602 forms Nestle can only call middle-or-passive are told which.
/// </para>
///
/// <para>
/// **Nothing here overwrites anything.** The rows go into <c>word_parsing</c> beside the word's own
/// <c>morphology</c> and the two stand together. Where they disagree — 440 cases, 70 genders, 29
/// tenses — that disagreement is a fact about the state of Greek scholarship on those words and is
/// worth more standing than resolved by whichever loader ran second.
/// </para>
///
/// <para>
/// A pass of its own rather than part of the corpus loader, for the reason
/// <see cref="StatedNumberLoader"/> gives: the corpus loader returns early for a text already in
/// the text table, and Nestle is loaded in every database this will ever run against. Idempotent by
/// its own rows, so it costs one indexed existence check per boot.
/// </para>
/// </summary>
internal sealed class MorphGntParsingLoader(AppDbContext db, ILogger<MorphGntParsingLoader> logger)
{
    /// <summary>
    /// Named in every row, because both licences over this data require attribution and this
    /// project attributes a source whether or not it has to. The version rather than the commit: a
    /// row has to say which dataset spoke, and which commit of it was read is recorded once, in the
    /// licence file kept beside the data, rather than 136,404 times here.
    /// </summary>
    internal const string Source =
        "morphgnt/sblgnt 6.12, the parsing MorphGNT gives the SBLGNT word standing in the same place";

    /// <summary>
    /// Both editions print the same letters and no third thing stands between them. It is not
    /// certainty — nobody stated this pairing, and the SBLGNT differs from the Nestle line in more
    /// than 540 variation units — so it sits at the top of the lexical band rather than outside it.
    /// </summary>
    private const double PrintedConfidence = 0.98;

    /// <summary>
    /// The editions spell it differently and the difference is one of the two this corpus has
    /// measured them to differ by. Lower, and deliberately visible: these are the rows a reader
    /// checking the join should look at first, and the note on each says which two spellings were
    /// joined.
    /// </summary>
    private const double SpellingConfidence = 0.85;

    private const string Import =
        """
        COPY word_parsing (word_id, lemma, morphology, method, confidence, source, note)
        FROM STDIN (FORMAT BINARY)
        """;

    public async Task<MorphGntOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        if (await db.WordParsings.AnyAsync(p => p.Source == Source, cancellationToken))
        {
            logger.LogInformation("The Greek already carries MorphGNT's parsing; nothing to do");
            return new MorphGntOutcome(true, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (!Directory.Exists(Path.Combine(folder, "parsing")))
        {
            logger.LogInformation(
                "No MorphGNT parsing under {Folder}; run scripts/fetch-morphgnt.ps1 to fetch it. The " +
                "Greek keeps its own morphology and nothing else changes",
                folder);
            return new MorphGntOutcome(false, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var text = await db.Texts.SingleOrDefaultAsync(t => t.Slug == NestleTextSource.Slug, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"The text \"{NestleTextSource.Slug}\" must be loaded before a second morphology can be " +
                       "put beside its own. This reads its words; it does not create them.");

        var (ids, witness) = await Witness(text.Id, cancellationToken);
        var parsing = MorphGntReader.ReadAll(folder);
        var join = MorphGntJoin.Of(witness, parsing);

        await Write(ids, witness, parsing, join.Rows, cancellationToken);

        var outcome = new MorphGntOutcome(
            false,
            join.Rows.Count(row => row.Match == MorphGntMatch.Printed),
            join.Rows.Count(row => row.Match == MorphGntMatch.Spelling),
            join.Unmatched.Count,
            join.Unused.Count,
            started.Elapsed);

        logger.LogInformation("Put MorphGNT's parsing on the Greek: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Nestle's words in document order, with the word ids kept alongside so the join can work on
    /// indices and never on database keys.
    /// </summary>
    private async Task<(long[] Ids, JoinWord[] Words)> Witness(int textId, CancellationToken cancellationToken)
    {
        var rows = await db.Words
            .Where(w => w.TextId == textId)
            .OrderBy(w => w.Verse!.Book!.CanonicalOrdinal)
            .ThenBy(w => w.Verse!.ChapterNumber)
            .ThenBy(w => w.Verse!.Number)
            .ThenBy(w => w.Position)
            .Select(w => new
            {
                w.Id,
                Book = w.Verse!.Book!.CanonicalOrdinal,
                Chapter = w.Verse!.ChapterNumber,
                Verse = w.Verse!.Number,
                w.Surface,
                w.Morphology,
            })
            .ToListAsync(cancellationToken);

        var ids = new long[rows.Count];
        var words = new JoinWord[rows.Count];

        for (var at = 0; at < rows.Count; at++)
        {
            ids[at] = rows[at].Id;
            words[at] = new JoinWord(
                rows[at].Book,
                rows[at].Chapter,
                rows[at].Verse,
                rows[at].Surface,
                Stated(rows[at].Morphology));

            // Flattened, so the parsed document is done with. Holding 137,779 of them until the
            // whole join finishes is a hundred megabytes of pooled buffers for nine strings each.
            rows[at].Morphology?.Dispose();
        }

        return (ids, words);
    }

    /// <summary>What the word's own text says about it, flattened to the keys it writes.</summary>
    private static IReadOnlyDictionary<string, string> Stated(JsonDocument? morphology)
    {
        if (morphology is null)
        {
            return new Dictionary<string, string>(0);
        }

        var stated = new Dictionary<string, string>(12, StringComparer.Ordinal);
        foreach (var property in morphology.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } value)
            {
                stated[property.Name] = value;
            }
        }

        return stated;
    }

    private async Task Write(
        long[] ids,
        JoinWord[] witness,
        IReadOnlyList<MorphGntWord> parsing,
        IReadOnlyList<MorphGntJoinRow> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var method = EnumSpelling.Of(LinkMethod.Lexical);

        // The transaction is what opens the connection as much as what makes the write atomic: a
        // binary import is issued on the connection rather than through the context, and the
        // context's own connection is closed between queries.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using (var writer = await connection.BeginBinaryImportAsync(Import, cancellationToken))
        {
            foreach (var row in rows)
            {
                var word = parsing[row.Parsing];

                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(ids[row.Witness], NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(word.Lemma, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(Morphology(word), NpgsqlDbType.Jsonb, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(
                    row.Match == MorphGntMatch.Printed ? PrintedConfidence : SpellingConfidence,
                    NpgsqlDbType.Double,
                    cancellationToken);
                await writer.WriteAsync(Source, NpgsqlDbType.Text, cancellationToken);

                // A note only where there is something to say. The words the two editions print
                // alike need no explanation and 135,960 copies of one would be most of the table.
                if (row.Match == MorphGntMatch.Spelling)
                {
                    await writer.WriteAsync(
                        Note(witness[row.Witness], word), NpgsqlDbType.Text, cancellationToken);
                }
                else
                {
                    await writer.WriteNullAsync(cancellationToken);
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The features, in the keys <c>word.morphology</c> uses, plus MorphGNT's own part of speech
    /// under <c>pos</c>. That vocabulary is finer than Nestle's on one point worth keeping: Nestle
    /// says <c>det</c> and <c>pron</c> where this separates the article from the personal,
    /// demonstrative, relative and interrogative pronouns, so the code is kept as written rather
    /// than mapped into a vocabulary that cannot hold it.
    /// </summary>
    private static string Morphology(MorphGntWord word)
    {
        var features = new Dictionary<string, string>(10, StringComparer.Ordinal)
        {
            ["pos"] = word.PartOfSpeech,
            ["parse"] = word.Parse,
        };

        Add(features, "case", MorphGntParsing.Case(word.Parse));
        Add(features, "number", MorphGntParsing.Number(word.Parse));
        Add(features, "gender", MorphGntParsing.Gender(word.Parse));
        Add(features, "tense", MorphGntParsing.Tense(word.Parse));
        Add(features, "voice", MorphGntParsing.Voice(word.Parse));
        Add(features, "mood", MorphGntParsing.Mood(word.Parse));
        Add(features, "person", MorphGntParsing.Person(word.Parse));
        Add(features, "degree", MorphGntParsing.Degree(word.Parse));

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

    /// <summary>
    /// Both spellings, on every row that needed them to differ, so a reader checking the join can
    /// see what was joined to what without going back to either edition.
    /// </summary>
    private static string Note(JoinWord witness, MorphGntWord parsing) =>
        $"Nestle writes {witness.Surface} where the SBLGNT writes {parsing.Word}";
}
