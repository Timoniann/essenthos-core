using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.TextusReceptus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Glaux;

/// <param name="Ambiguous">
/// Words whose form GLAUx lemmatises more than one way, without four fifths of its evidence
/// agreeing on one. Left without a lemma rather than given the leading guess: a lemma nobody can
/// see the doubt behind is worse than none, because everything downstream treats it as a fact.
/// </param>
/// <param name="Unknown">Words whose form GLAUx does not lemmatise at all.</param>
internal sealed record GlauxOutcome(
    bool AlreadyLoaded,
    int Forms,
    int Words,
    int Lemmatised,
    int Bridged,
    int Ambiguous,
    int Unknown,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Septuagint already has its lemmas"
            : $"{Lemmatised} of {Words} words lemmatised from {Forms} forms in {Elapsed} " +
              $"({(double)Lemmatised / Words:P1}): {Bridged} through the Attic-to-Koine bridge, " +
              $"{Ambiguous} left alone as ambiguous, {Unknown} with a form GLAUx does not carry";
}

/// <summary>
/// Lemmas for the Septuagint, which is the one text in the corpus that arrived without any.
///
/// Brenton is public domain and unannotated: 577,361 words with no lemma, no morphology and no
/// Strong number, so every method that needs a dictionary form stops at the Greek New Testament.
/// GLAUx annotates a *different* edition of the same book, and 99.4% of Brenton's tokens are
/// written the same way somewhere in it — so GLAUx is read as a **form-to-lemma dictionary** and
/// its own Greek is never loaded. That keeps a text whose transcription provenance Wikisource does
/// not document out of the corpus and confines what we take to lexical facts. DOC-0161 has the
/// licence reading; the owner accepted CC BY-SA on 2026-09-03.
///
/// <para>
/// **A form is only lemmatised where the evidence agrees.** GLAUx lemmatises αὐτοῦ as a pronoun in
/// one place and as an adverb in another, and a table that kept only the winner would state a fact
/// where there was a vote. So the lexicon records the leading lemma and the share of occurrences
/// behind it, and this writes only what four fifths of the evidence agrees on. Measured over the
/// whole text, that threshold costs 2.1 points of coverage against taking every leader and buys
/// back the forms where GLAUx itself is unsure — and 90% would cost less than a point more, so it
/// is not a knife edge.
/// </para>
///
/// <para>
/// **The Strong number is deliberately not written.** It is derivable from the lemma, but it would
/// be *our* inference, and everywhere else in this corpus <c>word.strong_number</c> means a source
/// said so. Writing it in that column would make an inference indistinguishable from testimony,
/// which is the one thing this schema exists to prevent. Where it should live is a schema question
/// and it is filed as one.
/// </para>
/// </summary>
internal sealed class GlauxLemmaLoader(AppDbContext db, ILogger<GlauxLemmaLoader> logger)
{
    /// <summary>
    /// The share of GLAUx's own occurrences that must agree before a form's lemma is written.
    /// </summary>
    private const double Agreed = 0.8;

    /// <summary>The text the lemmas are written onto. GLAUx annotates no other text we serve.</summary>
    public const string Septuagint = "lxx-brenton";

    public async Task<GlauxOutcome> Load(string directory, CancellationToken cancellationToken = default)
    {
        var text = await db.Texts.Where(t => t.Slug == Septuagint).Select(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (text == 0)
        {
            logger.LogInformation("The Septuagint is not loaded; there is nothing to lemmatise");
            return new GlauxOutcome(true, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (await db.Words.AnyAsync(w => w.TextId == text && w.Lemma != null, cancellationToken))
        {
            logger.LogInformation("The Septuagint already has its lemmas; nothing to do");
            return new GlauxOutcome(true, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (!Directory.Exists(directory))
        {
            logger.LogWarning(
                "GLAUx is not at {Directory}, so the Septuagint keeps no lemmas. It is 111 MB of "
                + "third-party data and is fetched rather than committed; DOC-0161 says from where",
                directory);
            return new GlauxOutcome(true, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var (lexicon, spelling) = Read(directory);

        var words = await db.Words
            .Where(word => word.TextId == text)
            .Select(word => new { word.Id, word.NormalisedText, word.Surface })
            .ToListAsync(cancellationToken);

        var written = new List<(long Id, string Lemma)>(words.Count);
        int bridged = 0, ambiguous = 0, unknown = 0;

        foreach (var word in words)
        {
            // NormalisedText is the folded form and the lexicon is keyed the same way, but a word
            // loaded before the folding pass has none, so fold it here rather than skip it.
            var form = word.NormalisedText is { Length: > 0 } folded
                ? folded
                : GreekLetters.Bare(word.Surface);

            if (!lexicon.TryGetValue(form, out var choice))
            {
                unknown++;
                continue;
            }

            if (choice.Share < Agreed)
            {
                ambiguous++;
                continue;
            }

            // The bridge only fires where the plain lemma is unknown to the spelling table, which
            // cannot happen for a lemma GLAUx itself wrote — it is here for the caller's count and
            // for the day the lexicon is joined against a New Testament lemma list instead.
            if (spelling.TryGetValue(choice.Lemma, out var asWritten))
            {
                written.Add((word.Id, asWritten));
                continue;
            }

            var candidate = GreekLemmaBridge.Candidates(choice.Lemma)
                .Select(c => spelling.GetValueOrDefault(c))
                .FirstOrDefault(c => c is not null);

            if (candidate is null)
            {
                unknown++;
                continue;
            }

            bridged++;
            written.Add((word.Id, candidate));
        }

        await Write(written, cancellationToken);

        var outcome = new GlauxOutcome(
            false, lexicon.Count, words.Count, written.Count, bridged, ambiguous, unknown,
            started.Elapsed);
        logger.LogInformation("Lemmatised the Septuagint: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The form-to-lemma table, and the spelling each folded lemma is most often written with.
    ///
    /// The lexicon folds both sides so that an accented text can be looked up by an unaccented key,
    /// which is the whole point of it — but a folded lemma is not a lemma anybody would print. So
    /// the written spellings are counted alongside, and what lands in the column is the dictionary
    /// form as GLAUx writes it.
    /// </summary>
    private static (Dictionary<string, LemmaChoice> Lexicon, Dictionary<string, string> Spelling) Read(
        string directory)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var words = new List<GlauxWord>(700_000);

        foreach (var file in Directory.EnumerateFiles(directory, "*.xml").Order(StringComparer.Ordinal))
        {
            foreach (var word in GlauxReader.Read(file))
            {
                words.Add(word);
                var folded = GreekLetters.Bare(word.Lemma);
                if (folded.Length == 0)
                {
                    continue;
                }

                if (!counts.TryGetValue(folded, out var spellings))
                {
                    spellings = new Dictionary<string, int>(StringComparer.Ordinal);
                    counts[folded] = spellings;
                }

                spellings[word.Lemma] = spellings.GetValueOrDefault(word.Lemma) + 1;
            }
        }

        var spelling = new Dictionary<string, string>(counts.Count, StringComparer.Ordinal);
        foreach (var (folded, options) in counts)
        {
            var leader = string.Empty;
            var best = 0;
            foreach (var (written, count) in options)
            {
                if (count > best || (count == best && string.CompareOrdinal(written, leader) < 0))
                {
                    best = count;
                    leader = written;
                }
            }

            spelling[folded] = leader;
        }

        return (GlauxLexicon.Build(words), spelling);
    }

    /// <summary>
    /// Half a million updates, so they go through a temporary table and one join rather than half a
    /// million statements — the same shape <c>WordFoldingLoader</c> uses for the same reason.
    /// </summary>
    private async Task Write(
        List<(long Id, string Lemma)> written,
        CancellationToken cancellationToken)
    {
        if (written.Count == 0)
        {
            return;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using (var create = new NpgsqlCommand(
            "CREATE TEMP TABLE glaux_lemma (word_id bigint PRIMARY KEY, lemma text NOT NULL) ON COMMIT DROP",
            connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(
            "COPY glaux_lemma (word_id, lemma) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var (id, lemma) in written)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(id, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(lemma, NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var update = new NpgsqlCommand(
            "UPDATE word SET lemma = g.lemma FROM glaux_lemma g WHERE word.id = g.word_id",
            connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
        {
            update.CommandTimeout = 600;
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
