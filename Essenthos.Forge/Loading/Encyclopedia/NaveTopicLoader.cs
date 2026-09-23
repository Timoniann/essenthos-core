using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Topics">Subjects written, which is those citing at least one verse.</param>
/// <param name="References">Runs of verses written, one per chapter a citation reaches.</param>
/// <param name="CrossReferencesOnly">Subjects that only send the reader elsewhere — <em>See WIND</em> — and are not written.</param>
/// <param name="Unread">Pieces of citation that did not read as a book, chapter and verse, and were dropped.</param>
internal sealed record TopicOutcome(
    bool AlreadyLoaded,
    int Topics,
    int References,
    int CrossReferencesOnly,
    int Unread,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "Nave's topics are already loaded"
            : $"{Topics} of Nave's topics with {References} runs of verses, {CrossReferencesOnly} subjects " +
              $"that only point elsewhere left out and {Unread} pieces of citation that did not read, in {Elapsed}";
}

/// <summary>
/// Nave's Topical Bible: some five thousand subjects and the verses Orville Nave filed under each.
///
/// <para>
/// **On trial.** It is loaded so a reader can see the topics a chapter's verses are filed under
/// beside the chapter, and whether it stays is the owner's decision after seeing it. Nothing else in
/// the corpus reads these tables, so taking it out is dropping them.
/// </para>
///
/// <para>
/// **Read from BibleData's transcription**, the one copy of Nave on this machine. Nave's own text of
/// 1896 is out of copyright; Stephenson's transcription of it is his, under the CC BY 4.0 the rest of
/// that release carries, and is credited as such. Its references are in the King James numbering,
/// which is the shared frame's, so they are stored as they are written.
/// </para>
///
/// <para>
/// Idempotent by table: a boot after the first writes nothing.
/// </para>
/// </summary>
internal sealed class NaveTopicLoader(AppDbContext db, ILogger<NaveTopicLoader> logger)
{
    public const string FileName = "NavesTopicalDictionary.csv";

    private const string ReferenceImport =
        """
        COPY topic_reference (topic_id, canonical_book, canonical_chapter, first_verse, last_verse, heading)
        FROM STDIN (FORMAT BINARY)
        """;

    public async Task<TopicOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        if (await db.Topics.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Nave's topics are already loaded");
            return new TopicOutcome(true, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Nave's Topical Bible is read from {path}, which is not there. It comes with the BibleData " +
                "release; run scripts/fetch-bibledata.ps1, or point Dataset:ResourcesPath at a corpus that has it.",
                path);
        }

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        var topics = new List<(Topic Topic, List<(string? Heading, NaveEntries.Citation Citation)> Cited)>();
        var crossReferencesOnly = 0;
        var unread = 0;
        foreach (var row in Csv.Read(path))
        {
            var name = row["subject"].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var cited = new List<(string? Heading, NaveEntries.Citation Citation)>();
            foreach (var line in NaveEntries.Lines(row["entry"]))
            {
                unread += line.Unread;
                cited.AddRange(line.Citations.Select(citation => (line.Heading, citation)));
            }

            if (cited.Count == 0)
            {
                crossReferencesOnly++;
                continue;
            }

            var topic = new Topic { Slug = UniqueSlug(name, slugs), Name = name, Source = Sources.Naves };
            topics.Add((topic, cited));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Topics.AddRange(topics.Select(t => t.Topic));
        await db.SaveChangesAsync(cancellationToken);

        var references = 0;
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using (var writer = await connection.BeginBinaryImportAsync(ReferenceImport, cancellationToken))
        {
            foreach (var (topic, cited) in topics)
            {
                foreach (var (heading, citation) in cited.Distinct())
                {
                    await writer.StartRowAsync(cancellationToken);
                    await writer.WriteAsync(topic.Id, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(citation.Book, NpgsqlDbType.Integer, cancellationToken);
                    await writer.WriteAsync(citation.Chapter, NpgsqlDbType.Integer, cancellationToken);
                    await Nullable(writer, citation.FirstVerse, cancellationToken);
                    await Nullable(writer, citation.LastVerse, cancellationToken);
                    if (heading is null)
                    {
                        await writer.WriteNullAsync(cancellationToken);
                    }
                    else
                    {
                        await writer.WriteAsync(heading, NpgsqlDbType.Text, cancellationToken);
                    }

                    references++;
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var outcome = new TopicOutcome(false, topics.Count, references, crossReferencesOnly, unread, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }

    private static async Task Nullable(NpgsqlBinaryImporter writer, int? value, CancellationToken cancellationToken)
    {
        if (value is { } number)
        {
            await writer.WriteAsync(number, NpgsqlDbType.Integer, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }
    }

    /// <summary><c>AARON</c> is <c>aaron</c>; a second subject folding to the same letters takes a number.</summary>
    private static string UniqueSlug(string name, HashSet<string> taken)
    {
        var slug = Slugs.Of(name);
        var unique = slug;
        for (var n = 2; !taken.Add(unique); n++)
        {
            unique = $"{slug}-{n}";
        }

        return unique;
    }
}
