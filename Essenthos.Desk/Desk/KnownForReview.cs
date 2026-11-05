using Essenthos.Core.Configuration;
using System.Text.Json.Nodes;

namespace Essenthos.Core.Desk;

/// <param name="Answer"><c>approved</c>, <c>rejected</c>, or null to take the answer back.</param>
internal sealed record KnownForAnswerRequest(string? Answer, string? Note);

internal sealed record KnownForAnswer(string Answer, string? Note, string? At);

/// <param name="Addresses">Each reference's first verse as the reading API addresses it, in step with <paramref name="References"/>.</param>
/// <param name="Changed">What the reading of the renderings put right in them, where it changed anything.</param>
internal sealed record KnownForClauseView(
    string English,
    string? Ukr,
    string? Deu,
    string? Spa,
    IReadOnlyList<string> References,
    IReadOnlyList<string?> Addresses,
    string? Changed);

/// <param name="Clauses">As the build's files hold them where they show the record, else as the list proposes them.</param>
/// <param name="Readings">What the three readings said of the record: the first reader's reason and doubts, the second's verdict on each clause, its note.</param>
/// <param name="Shown">Whether the build's files show what the record is known for; null where those files are not in this checkout yet.</param>
/// <param name="FoldedInto">The record this one was folded into since the list was written, whose own line is the one shown.</param>
internal sealed record KnownForEntry(
    string Slug,
    string Kind,
    string Name,
    string? Sex,
    int Verses,
    IReadOnlyList<KnownForClauseView> Clauses,
    JsonNode? Readings,
    KnownForAnswer? Decision,
    bool? Shown,
    string? FoldedInto);

internal sealed record KnownForDeclined(string Slug, string Kind, string Name, int Verses, string? Reason);

/// <param name="Written">Whether the list exists; until it does there is nothing to read.</param>
/// <param name="Open">Entries with no answer yet.</param>
/// <param name="Files">Whether the build's known-for files are in this checkout, so that what is shown can be told.</param>
/// <param name="Unapplied">Answered entries the files do not show as answered yet: an approval not written, a rejection not taken out.</param>
internal sealed record KnownForReviewResponse(
    bool Written,
    int Open,
    int Approved,
    int Rejected,
    IReadOnlyList<KnownForEntry> Entries,
    IReadOnlyList<KnownForDeclined> Declined,
    bool Files,
    int Unapplied);

/// <summary>
/// What people, places and things are known for, as a language model wrote it, which the owner reads
/// before any of it is shown: he approves an entry or rejects it.
///
/// <para>
/// The list is the question: the proposals and the readings of them. What the site shows is what the
/// build's files hold (<c>records/facts/known-for</c>, its references and its languages), so each entry
/// says whether they show it, and its clauses are theirs where they do. An answer is written beside its
/// entry and logged as waiting on the <see cref="OwnerChanges.Files"/> step: an agent applies it to those
/// files with the owner's stamp (<c>forge owner-apply</c>). Until the cut-over export puts the files in the
/// checkout, the old load still reads the answer from the list.
/// </para>
/// </summary>
internal sealed class KnownForReview(DeskPaths paths, ChangeLog log, BuildFiles build)
{
    public const string FilesProduct = "records/facts/known-for";
    public const string FileName = "known-for.json";

    public const string Section = "known-for";

    public const string Approved = "approved";

    public const string Rejected = "rejected";

    public string File => Path.Combine(paths.Review, FileName);

    public bool Exists => System.IO.File.Exists(File);

    public KnownForReviewResponse Read()
    {
        var files = Shown();
        if (!Exists)
        {
            return new KnownForReviewResponse(false, 0, 0, 0, [], [], files is not null, 0);
        }

        var root = JsonFiles.Read(File);
        var entries = Entries(root).Select(entry => Entry(entry, files)).ToList();
        var declined = (root["declined"]?.AsArray() ?? []).OfType<JsonObject>().Select(d => new KnownForDeclined(
            d["slug"]?.GetValue<string>() ?? string.Empty,
            d["kind"]?.GetValue<string>() ?? string.Empty,
            d["name"]?.GetValue<string>() ?? string.Empty,
            d["verses"]?.GetValue<int>() ?? 0,
            d["reason"]?.GetValue<string>())).ToList();
        return new KnownForReviewResponse(
            true,
            entries.Count(e => e.Decision is null),
            entries.Count(e => e.Decision?.Answer == Approved),
            entries.Count(e => e.Decision?.Answer == Rejected),
            entries,
            declined,
            files is not null,
            entries.Count(e => e.Decision is not null && e.FoldedInto is null && e.Shown is { } shown && shown != (e.Decision.Answer == Approved)));
    }

    /// <summary>
    /// What the build's files show each record is known for, clause by clause in every language, or null
    /// where the files are not in this checkout.
    /// </summary>
    private KnownForFiles? Shown()
    {
        if (build.Read(FilesProduct) is not { } clauses || build.Read("records/facts/known-for-references") is not { } references)
        {
            return null;
        }

        var texts = new Dictionary<(string Language, string Slug, string Ordinal), string>();
        foreach (var language in new[] { "eng", "ukr", "deu", "spa" })
        {
            if (build.Read($"records/i18n/{language}/known-for") is not { } table)
            {
                continue;
            }

            var (slug, ordinal, text) = (table.Column("slug"), table.Column("ordinal"), table.Column("text"));
            foreach (var row in table.Rows)
            {
                texts[(language, row[slug]!, row[ordinal]!)] = row[text] ?? string.Empty;
            }
        }

        var cited = references.Rows
            .GroupBy(row => (Slug: row[references.Column("slug")]!, Ordinal: row[references.Column("ordinal")]!))
            .ToDictionary(g => g.Key, g => g
                .OrderBy(row => int.Parse(row[references.Column("position")]!, System.Globalization.CultureInfo.InvariantCulture))
                .Select(row => Span(row[references.Column("ref")]!, row[references.Column("end_chapter")], row[references.Column("end_verse")]))
                .ToList());

        var shown = new Dictionary<string, List<KnownForClauseView>>(StringComparer.Ordinal);
        var (slugColumn, ordinalColumn) = (clauses.Column("slug"), clauses.Column("ordinal"));
        foreach (var row in clauses.Rows.OrderBy(r => int.Parse(r[ordinalColumn]!, System.Globalization.CultureInfo.InvariantCulture)))
        {
            var key = (Slug: row[slugColumn]!, Ordinal: row[ordinalColumn]!);
            string? Text(string language) => texts.GetValueOrDefault((language, key.Slug, key.Ordinal));
            var spans = cited.GetValueOrDefault(key) ?? [];
            if (!shown.TryGetValue(key.Slug, out var list))
            {
                shown[key.Slug] = list = [];
            }

            list.Add(new KnownForClauseView(
                Text("eng") ?? string.Empty, Text("ukr"), Text("deu"), Text("spa"),
                [.. spans.Select(s => s.Written)], [.. spans.Select(s => s.Address)], null));
        }

        var folds = build.Read("records/folds") is { } folded
            ? folded.Rows.ToDictionary(row => row[folded.Column("slug")]!, row => row[folded.Column("folded_into")]!, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        return new KnownForFiles(shown, folds);
    }

    /// <summary>A span of the files (<c>2.7:10</c> to chapter 7, verse 12) as the page writes and links it.</summary>
    private static (string Written, string? Address) Span(string start, string? endChapter, string? endVerse)
    {
        var key = Essenthos.Core.Products.VerseKey.Parse(start);
        var book = Essenthos.Core.Corpus.BookReferences.IsInCanon(key.Book)
            ? Essenthos.Core.Corpus.BookReferences.Abbreviation(key.Book)
            : key.Book.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var end = (endChapter, endVerse) switch
        {
            (null, null) => string.Empty,
            var (chapter, verse) when chapter == key.Chapter.ToString(System.Globalization.CultureInfo.InvariantCulture) =>
                verse == key.VerseOnly ? string.Empty : $"-{verse}",
            var (chapter, verse) => $"-{chapter}:{verse}",
        };
        var address = Essenthos.Core.Corpus.BookReferences.IsInCanon(key.Book)
            ? $"{Essenthos.Core.Corpus.BookReferences.Slug(key.Book)}:{key.Chapter}:{key.Verse}"
            : null;
        return ($"{book} {key.ChapterAndVerse}{end}", address);
    }

    public int Unanswered() => Exists ? Entries(JsonFiles.Read(File)).Count(e => e["decision"] is null) : 0;

    /// <summary>Answers one entry or takes its answer back. Null where the list holds no such record or the answer is neither.</summary>
    public async Task<KnownForEntry?> Answer(string slug, KnownForAnswerRequest request)
    {
        if ((request.Note?.Length ?? 0) > ChangeLog.LongestNote || !Exists
            || request.Answer is not (null or Approved or Rejected))
        {
            return null;
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var files = Shown();
        var change = await JsonFiles.Change(File, root =>
        {
            var entry = Entries(root).FirstOrDefault(e => e["slug"]?.GetValue<string>() == slug);
            if (entry is null)
            {
                return (false, ((KnownForEntry, KnownForEntry)?)null);
            }

            var before = Entry(entry, files);
            if (request.Answer is null)
            {
                entry.Remove("decision");
            }
            else
            {
                entry["decision"] = new JsonObject { ["answer"] = request.Answer, ["note"] = note, ["at"] = JsonFiles.Now() };
            }

            return (true, (before, Entry(entry, files)));
        });
        if (change is not { } changed)
        {
            return null;
        }

        var (was, now) = changed;

        await log.Append(
            Section,
            "verdict",
            $"{now.Kind}/{slug}",
            was.Decision?.Answer is { } before ? JsonValue.Create(before) : null,
            now.Decision?.Answer is { } after ? JsonValue.Create(after) : null,
            request.Note,
            OwnerChanges.Files,
            now.Name);
        return now;
    }

    private static IEnumerable<JsonObject> Entries(JsonNode root) =>
        (root["records"]?.AsArray() ?? []).OfType<JsonObject>();

    private static KnownForEntry Entry(JsonObject entry, KnownForFiles? files)
    {
        var slug = entry["slug"]?.GetValue<string>() ?? string.Empty;
        var shown = files?.Shown.GetValueOrDefault(slug);
        return new KnownForEntry(
            slug,
            entry["kind"]?.GetValue<string>() ?? string.Empty,
            entry["name"]?.GetValue<string>() ?? string.Empty,
            entry["sex"]?.GetValue<string>(),
            entry["verses"]?.GetValue<int>() ?? 0,
            shown ?? [.. (entry["clauses"]?.AsArray() ?? []).OfType<JsonObject>().Select(Clause)],
            entry["readings"]?.DeepClone(),
            entry["decision"] is JsonObject decision && decision["answer"]?.GetValue<string>() is { Length: > 0 } answer
                ? new KnownForAnswer(answer, decision["note"]?.GetValue<string>(), decision["at"]?.GetValue<string>())
                : null,
            files is null ? null : shown is not null,
            files?.Folds.GetValueOrDefault(slug));
    }

    /// <summary>What the build's files show, record by record, and the old slugs of records folded into another.</summary>
    private sealed record KnownForFiles(Dictionary<string, List<KnownForClauseView>> Shown, Dictionary<string, string> Folds);

    private static KnownForClauseView Clause(JsonObject clause)
    {
        var references = (clause["references"]?.AsArray() ?? []).Select(n => n?.GetValue<string>()).OfType<string>().ToList();
        return new KnownForClauseView(
            clause["english"]?.GetValue<string>() ?? string.Empty,
            clause["ukr"]?.GetValue<string>(),
            clause["deu"]?.GetValue<string>(),
            clause["spa"]?.GetValue<string>(),
            references,
            [.. references.Select(reference => Desk.Addresses.Of(reference.Split('-')[0]))],
            clause["renderingsChanged"]?.GetValue<string>());
    }
}
