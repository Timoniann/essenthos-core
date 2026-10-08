using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Publishing;

/// <summary>
/// The corpus as files a person can take, instead of a verse at a time through the API. The first slice
/// is the texts whose recorded redistribution lets them travel without a condition on the whole: public
/// domain, permitted, or permitted with attribution — one file per text, each with a file saying who
/// made it and on what terms, and a manifest naming every file with its checksum.
///
/// What the file holds is the text as the corpus prints it, verse by verse, and nothing a different
/// source added to it. The lemmas, Strong's numbers, glosses and morphology on a text's words, the
/// links between texts, the lexicon and the encyclopedia each come from datasets with terms of their
/// own, and they join the export one source at a time once each source's terms have been read. A text
/// recorded as ShareAlike or non-commercial is not in it at all, and neither is one whose licence the
/// corpus does not state.
///
/// An export replaces the folder it is written to whole, and only once every file is written, so a run
/// that fails leaves the previous export in place.
/// </summary>
internal sealed partial class TextExporter(
    AppDbContext db,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<TextExporter> logger)
{
    /// <summary>The redistribution values whose texts this slice puts in the export.</summary>
    internal static readonly IReadOnlySet<Redistribution> Included = new HashSet<Redistribution>
    {
        Redistribution.PublicDomain,
        Redistribution.Permitted,
        Redistribution.PermittedWithAttribution,
    };

    private static readonly JsonWriterOptions LineOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A slug becomes a folder name, so only the characters a slug is made of are let through.</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$")]
    private static partial Regex SafeSlug();

    private string Resources => ResourcePaths.Read(configuration, environment.ContentRootPath);

    /// <summary>
    /// Where an export goes when none is named: <c>Export:Path</c>, otherwise <c>.exports</c> in the
    /// checkout. The folder to host the files from is the owner's to choose at release; nothing here
    /// knows where that is.
    /// </summary>
    internal string DefaultFolder => configuration["Export:Path"] is { Length: > 0 } configured
        ? Path.GetFullPath(configured)
        : Path.Combine(Path.GetDirectoryName(Resources)!, ".exports");

    /// <summary>
    /// What an export would hold and what it would leave out, and why, from the texts' records alone.
    /// </summary>
    internal async Task<(IReadOnlyList<Text> Included, IReadOnlyList<WithheldText> Withheld)> Choose(CancellationToken cancellationToken)
    {
        var included = new List<Text>();
        var withheld = new List<WithheldText>();
        foreach (var text in await db.Texts.AsNoTracking().OrderBy(t => t.Slug).ToListAsync(cancellationToken))
        {
            var recorded = EnumSpelling.Of(text.Redistribution);
            if (!Included.Contains(text.Redistribution))
            {
                withheld.Add(new WithheldText(text.Slug, recorded, WhyNot(text.Redistribution)));
            }
            else if (string.IsNullOrWhiteSpace(text.Licence))
            {
                withheld.Add(new WithheldText(text.Slug, recorded, "no licence is recorded for it, and the file has to carry one"));
            }
            else if (!SafeSlug().IsMatch(text.Slug))
            {
                withheld.Add(new WithheldText(text.Slug, recorded, "its identifier cannot name a folder"));
            }
            else
            {
                included.Add(text);
            }
        }

        return (included, withheld);
    }

    private static string WhyNot(Redistribution redistribution) => redistribution switch
    {
        Redistribution.ShareAlike => "ShareAlike: a later slice, once what binds an adaptation of it is settled",
        Redistribution.NonCommercialOnly => "non-commercial only: a later slice, with its own terms stated beside it",
        Redistribution.Unknown => "its redistribution has not been recorded",
        _ => "it may not be redistributed",
    };

    /// <summary>Writes the export to <paramref name="folder"/>, or says what it would write.</summary>
    public async Task<int> Export(string? folder, bool dryRun, CancellationToken cancellationToken)
    {
        var target = folder is { Length: > 0 } ? Path.GetFullPath(folder) : DefaultFolder;
        var (included, withheld) = await Choose(cancellationToken);

        logger.LogInformation(
            "{Included} texts can be exported; {Withheld} stay out: {Reasons}",
            included.Count,
            withheld.Count,
            string.Join("; ", withheld.GroupBy(w => w.Reason).Select(g => $"{g.Key} ({string.Join(", ", g.Select(w => w.Slug))})")));

        if (dryRun)
        {
            logger.LogInformation("Dry run: would write {Texts} into {Folder}. Nothing was written", string.Join(", ", included.Select(t => t.Slug)), target);
            return 0;
        }

        if (included.Count == 0)
        {
            logger.LogError("No text can be exported, so nothing was written and {Folder} is as it was", target);
            return 1;
        }

        var writing = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".writing";
        if (Directory.Exists(writing))
        {
            Directory.Delete(writing, recursive: true);
        }

        try
        {
            var manifest = await Write(writing, included, withheld, cancellationToken);
            Replace(target, writing);
            logger.LogInformation(
                "Exported {Texts} texts, {Verses} verses, into {Folder}; fingerprint {Fingerprint}",
                manifest.Texts.Count, manifest.Texts.Sum(t => t.Verses), target, manifest.Fingerprint);
            return 0;
        }
        catch
        {
            if (Directory.Exists(writing))
            {
                Directory.Delete(writing, recursive: true);
            }

            throw;
        }
    }

    /// <summary>The finished folder takes the place of the old one in two moves, so a failure between them can be put right by hand.</summary>
    private void Replace(string target, string writing)
    {
        var previous = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".previous";
        if (Directory.Exists(previous))
        {
            Directory.Delete(previous, recursive: true);
        }

        if (Directory.Exists(target))
        {
            Directory.Move(target, previous);
        }

        Directory.Move(writing, target);
        if (Directory.Exists(previous))
        {
            Directory.Delete(previous, recursive: true);
        }
    }

    internal async Task<DownloadsManifest> Write(
        string folder,
        IReadOnlyList<Text> texts,
        IReadOnlyList<WithheldText> withheld,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var exported = new List<ExportedText>();
        foreach (var text in texts)
        {
            exported.Add(await WriteText(folder, text, cancellationToken));
        }

        var manifest = new DownloadsManifest(
            DownloadsManifest.CurrentFormat,
            DateTimeOffset.UtcNow,
            Fingerprint(exported),
            await Corpus(cancellationToken),
            exported,
            withheld);
        await File.WriteAllTextAsync(
            Path.Combine(folder, DownloadsManifest.FileName), manifest.ToJson(), new UTF8Encoding(false), cancellationToken);
        return manifest;
    }

    private async Task<ExportedText> WriteText(string folder, Text text, CancellationToken cancellationToken)
    {
        var relative = $"texts/{text.Slug}";
        Directory.CreateDirectory(Path.Combine(folder, "texts", text.Slug));

        var dataPath = $"{relative}/{text.Slug}.jsonl.gz";
        var (books, verses) = await WriteVerses(Path.Combine(folder, dataPath), text, cancellationToken);

        var attributionPath = $"{relative}/ATTRIBUTION.txt";
        await File.WriteAllTextAsync(
            Path.Combine(folder, attributionPath), Attribution(text, dataPath), new UTF8Encoding(false), cancellationToken);

        logger.LogInformation("{Slug}: {Books} books, {Verses} verses", text.Slug, books, verses);
        return new ExportedText(
            text.Slug, text.Name, text.NameNative, text.Language, EnumSpelling.Of(text.Kind), EnumSpelling.Of(text.Redistribution),
            text.Licence, text.LicenceUrl, text.RightsHolder, text.Citation,
            books, verses,
            await Describe(folder, dataPath, cancellationToken),
            await Describe(folder, attributionPath, cancellationToken));
    }

    /// <summary>
    /// One line a verse, in the text's own order and numbering, and each address by what names a book in
    /// every text — never by a row of the database, which every rebuild numbers again.
    /// </summary>
    private async Task<(int Books, int Verses)> WriteVerses(string path, Text text, CancellationToken cancellationToken)
    {
        var books = new HashSet<string>(StringComparer.Ordinal);
        var verses = 0;
        await using (var file = File.Create(path))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
        {
            var lines = db.Database.SqlQuery<VerseLine>($"""
                SELECT b.slug AS book, b.canonical_ordinal AS book_number, v.chapter_number AS chapter,
                       v.number AS verse, v.label AS label,
                       btrim(coalesce(string_agg(w.text || w.trailer, '' ORDER BY w.position) FILTER (WHERE NOT w.elided), '')) AS text
                FROM verse v
                JOIN book b ON b.id = v.book_id
                LEFT JOIN word w ON w.verse_id = v.id
                WHERE v.text_id = {text.Id}
                GROUP BY v.id, b.id
                ORDER BY b.position, v.chapter_number, v.sequence, v.number, v.label
                """).AsAsyncEnumerable();

            var skipped = 0;
            await foreach (var line in lines.WithCancellation(cancellationToken))
            {
                if (line.Text.Length == 0)
                {
                    skipped++;
                    continue;
                }

                books.Add(line.Book);
                verses++;
                await Line(gzip, line, cancellationToken);
            }

            if (skipped > 0)
            {
                logger.LogInformation("{Slug}: {Skipped} verses hold no words and are left out", text.Slug, skipped);
            }
        }

        return (books.Count, verses);
    }

    private static async Task Line(Stream output, VerseLine line, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(buffer, LineOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("book", line.Book);
            writer.WriteNumber("bookNumber", line.BookNumber);
            writer.WriteNumber("chapter", line.Chapter);
            writer.WriteNumber("verse", line.Verse);
            if (line.Label.Length > 0)
            {
                writer.WriteString("label", line.Label);
            }

            writer.WriteString("text", line.Text);
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        buffer.Position = 0;
        await buffer.CopyToAsync(output, cancellationToken);
    }

    private static async Task<ExportedFile> Describe(string folder, string relative, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(Path.Combine(folder, relative));
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        return new ExportedFile(relative, stream.Length, hash);
    }

    /// <summary>The export's fingerprint: every file's path and checksum, in path order, hashed once.</summary>
    internal static string Fingerprint(IEnumerable<ExportedText> texts)
    {
        var text = string.Concat(texts
            .SelectMany(t => new[] { t.File, t.Attribution })
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .Select(f => $"{f.Path}\t{f.Sha256}\n"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private async Task<ExportedCorpus> Corpus(CancellationToken cancellationToken)
    {
        var release = await db.CorpusReleases.AsNoTracking()
            .OrderByDescending(r => r.BuiltAt).Select(r => r.Name).FirstOrDefaultAsync(cancellationToken);
        var manifest = Path.Combine(Resources, "MANIFEST.json");
        return new ExportedCorpus(
            release,
            File.Exists(manifest) ? Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(manifest, cancellationToken))) : null,
            (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault() ?? "none",
            Publisher.ForgeVersion());
    }

    /// <summary>
    /// The file that goes beside a text: who made it and under what terms, taken from what the corpus
    /// records, in the words the licence asks to be kept with the text wherever it goes.
    /// </summary>
    internal static string Attribution(Text text, string dataPath)
    {
        var lines = new List<string>
        {
            text.Name,
            new string('=', text.Name.Length),
            string.Empty,
        };

        void Field(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add($"{name}: {value.Trim()}");
            }
        }

        Field("Name in its own language", text.NameNative);
        Field("Identifier", text.Slug);
        Field("Language", text.Language);
        Field("Kind", EnumSpelling.Of(text.Kind));
        Field("Edition", text.Edition);
        Field("First published", text.PublishedYear?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Field("Edition of", text.EditionYear?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Field("Translators", text.Translators);
        Field("Editors", text.Editors);
        Field("Rights holder", text.RightsHolder);

        lines.Add(string.Empty);
        lines.Add("Terms");
        lines.Add("-----");
        Field("Licence", text.Licence);
        Field("Licence text", text.LicenceUrl);
        lines.Add(Terms(text.Redistribution));
        Field("How to cite", text.Citation);
        Field("About the rights", text.RightsNote);
        Field("Obtained from", text.SourceUrl);

        var parts = TextPartSources.Of(text);
        if (parts.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Parts of this text come from other sources, under their own terms");
            lines.Add("---------------------------------------------------------------");
            foreach (var part in parts)
            {
                lines.Add($"* {part.Name}, {part.Author}");
                lines.Add($"  Covers: {part.Covers}");
                lines.Add($"  Licence: {part.Licence}" + (part.LicenceUrl is { Length: > 0 } url ? $" ({url})" : string.Empty));
                lines.Add($"  From: {part.Url}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("The file");
        lines.Add("--------");
        lines.Add($"{Path.GetFileName(dataPath)} holds the text verse by verse, one JSON object to a line, compressed with gzip.");
        lines.Add("A line has: book (the book's identifier in this text), bookNumber (its place in the canon, the same in every text),");
        lines.Add("chapter and verse (as this text numbers them), label (a letter the text prints after the verse number, where it prints one) and text.");
        lines.Add("The words are as the Essenthos corpus holds them from the source above. Keep this file with the text wherever it goes.");
        lines.Add(string.Empty);
        return string.Join('\n', lines);
    }

    private static string Terms(Redistribution redistribution) => redistribution switch
    {
        Redistribution.PublicDomain => "Terms of reuse: in the public domain; it may be copied and passed on freely.",
        Redistribution.Permitted => "Terms of reuse: the rights holder permits it to be copied and passed on.",
        Redistribution.PermittedWithAttribution =>
            "Terms of reuse: it may be copied and passed on if it is credited as the licence above requires.",
        _ => string.Empty,
    };

    public sealed class VerseLine
    {
        public string Book { get; set; } = string.Empty;

        public int BookNumber { get; set; }

        public int Chapter { get; set; }

        public int Verse { get; set; }

        public string Label { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;
    }
}
