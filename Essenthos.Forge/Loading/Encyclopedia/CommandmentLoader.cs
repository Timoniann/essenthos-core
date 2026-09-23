using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Frame;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record CommandmentOutcome(bool AlreadyLoaded, int Positive, int Negative, int References, TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the commandments are already loaded"
            : $"{Positive} positive and {Negative} negative commandments in Maimonides' count, resting on " +
              $"{References} runs of verses, in {Elapsed}";
}

/// <summary>
/// The 613 commandments as Maimonides counted them, from <c>Resources/Maimonides/commandments.tsv</c>.
///
/// <para>
/// The file is this project's curation of two public-domain works, written by
/// <c>scripts/maimonides.py</c> and carried in the repository; its <c>LICENCE.md</c> says what is
/// whose. It is read from the output folder, like the world history, so no fetch stands between a
/// clean checkout and the count. Its verses are already in the shared English numbering and say so
/// where they were moved, and that note is kept on the row.
/// </para>
///
/// <para>
/// Idempotent by table: a boot after the first writes nothing.
/// </para>
/// </summary>
internal sealed partial class CommandmentLoader(AppDbContext db, ILogger<CommandmentLoader> logger)
{
    public static readonly string[] FilePath = ["Resources", "Maimonides", "commandments.tsv"];

    /// <summary>How many of each kind the count has. A file with another number is not this count.</summary>
    internal static readonly IReadOnlyDictionary<string, int> Counts = new Dictionary<string, int>
    {
        [CommandmentKinds.Positive] = 248,
        [CommandmentKinds.Negative] = 365,
    };

    public async Task<CommandmentOutcome> Load(string path, CancellationToken cancellationToken = default)
    {
        if (await db.Commandments.AnyAsync(cancellationToken))
        {
            logger.LogInformation("The commandments are already loaded");
            return new CommandmentOutcome(true, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var commandments = Read(path);
        db.Commandments.AddRange(commandments);
        await db.SaveChangesAsync(cancellationToken);

        var outcome = new CommandmentOutcome(
            false,
            commandments.Count(c => c.Kind == CommandmentKinds.Positive),
            commandments.Count(c => c.Kind == CommandmentKinds.Negative),
            commandments.Sum(c => c.References.Count),
            started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }

    /// <summary>The file as rows, refused whole where any row does not read or the count is not Maimonides'.</summary>
    internal static List<Commandment> Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The commandments are read from {path}, which is not there. It is committed under " +
                "Resources/Maimonides and copied beside the build; rebuild Essenthos.Forge, or rewrite it " +
                "with scripts/maimonides.py.",
                path);
        }

        var lines = File.ReadAllLines(path);
        var header = lines[0].Split('\t');
        int Column(string name) => Array.IndexOf(header, name) is var at and >= 0
            ? at
            : throw new InvalidDataException($"{path} has no \"{name}\" column; its header is: {lines[0]}");

        var (kind, number, listed, title, verses, note) =
            (Column("kind"), Column("number"), Column("mishneh_torah"), Column("title"), Column("verses"), Column("note"));

        var commandments = new List<Commandment>();
        foreach (var (line, index) in lines.Skip(1).Select((line, index) => (line, index + 2)))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split('\t');
            var commandment = new Commandment
            {
                Kind = fields[kind],
                Number = int.Parse(fields[number], CultureInfo.InvariantCulture),
                MishnehTorahNumber = int.Parse(fields[listed], CultureInfo.InvariantCulture),
                Title = fields[title],
                Note = fields.Length > note && fields[note].Length > 0 ? fields[note] : null,
                Source = Sources.Maimonides,
            };
            if (!CommandmentKinds.IsKnown(commandment.Kind))
            {
                throw new InvalidDataException(
                    $"{path} line {index} is of kind \"{commandment.Kind}\"; the count has only " +
                    $"{CommandmentKinds.Positive} and {CommandmentKinds.Negative} commandments.");
            }

            commandment.References = References(fields[verses])
                ?? throw new InvalidDataException(
                    $"{path} line {index} cites \"{fields[verses]}\", which does not read as book code, chapter " +
                    "and verses separated by semicolons — EXO 20:2; DEU 5:6. Rewrite it with scripts/maimonides.py.");
            commandments.Add(commandment);
        }

        foreach (var (counted, expected) in Counts)
        {
            var numbers = commandments.Where(c => c.Kind == counted).Select(c => c.Number).Order().ToList();
            if (!numbers.SequenceEqual(Enumerable.Range(1, expected)))
            {
                throw new InvalidDataException(
                    $"{path} holds {numbers.Count} {counted} commandments where Maimonides counts {expected}, " +
                    "numbered from one without a gap. It is not the file scripts/maimonides.py writes.");
            }
        }

        return commandments;
    }

    /// <summary><c>EXO 20:2; LEV 16:3-34</c> as runs of verses, or null where any piece does not read.</summary>
    internal static List<CommandmentReference>? References(string verses)
    {
        var references = new List<CommandmentReference>();
        foreach (var piece in verses.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Piece().Match(piece);
            if (!match.Success || !BookCodes.TryGetOrdinal(match.Groups["book"].Value, out var book))
            {
                return null;
            }

            var first = int.Parse(match.Groups["first"].Value, CultureInfo.InvariantCulture);
            var last = match.Groups["last"].Success
                ? int.Parse(match.Groups["last"].Value, CultureInfo.InvariantCulture)
                : first;
            if (last < first)
            {
                return null;
            }

            references.Add(new CommandmentReference
            {
                CanonicalBook = book,
                CanonicalChapter = int.Parse(match.Groups["chapter"].Value, CultureInfo.InvariantCulture),
                FirstVerse = first,
                LastVerse = last,
            });
        }

        return references.Count == 0 ? null : references;
    }

    [GeneratedRegex(@"^(?<book>[1-3]?[A-Z]{2,3})\s+(?<chapter>\d+):(?<first>\d+)(?:-(?<last>\d+))?$")]
    private static partial Regex Piece();
}
