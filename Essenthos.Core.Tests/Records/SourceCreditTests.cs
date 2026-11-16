using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Products.Tsv;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// What the credits on our own rows say, as the files that write them hold it. A relationship, a
/// record, an annotation and a descriptor clause each carry a source string, and the API serves it
/// on every response, so a credit that names a ticket or repeats what the owner or the lead was told
/// to do is in the JSON a reader's browser receives even where the page prints something else.
///
/// <para>
/// The provenance sentence stays — <c>on the project owner's ruling of 2026-10-12 that …</c> says
/// where a claim comes from. The ticket that settled it, and the instruction as it was given, do not.
/// </para>
/// </summary>
public sealed partial class SourceCreditTests
{
    /// <summary>A ticket of the project's board, in any of the shapes it takes.</summary>
    [GeneratedRegex(@"\b(?:PRB|TSK|FTR|NOT|DOC|MLS|MST|RUL|PLN|GDE|COM)-\d{3,4}\b")]
    private static partial Regex Ticket();

    /// <summary>An instruction quoted as it was given, which is how it was said and not where it came from.</summary>
    [GeneratedRegex(
        @"his answer on|her answer on|answered on |owner's word:|preceding instruction|ruling in chat|"
        + @"in chat \d|instruction of \d{4}-\d{2}-\d{2} to |instruction to |word of \d{4}-\d{2}-\d{2} to |, option [a-z]\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex QuotedInstruction();

    /// <summary>
    /// The owner's own words in a credit, quoted in his language: <c>('Так, це логічно')</c>. A note may quote a
    /// text's word the same way, so only a credit is held to it.
    /// </summary>
    [GeneratedRegex(@"(?:\(|: )['«‘][А-ЯІЇЄҐа-яіїєґ]")]
    private static partial Regex OwnersWords();

    /// <summary>The columns of a record file that say who decided a row.</summary>
    private static readonly string[] CreditColumns = ["by", "owner_ruled", "own_record"];

    /// <summary>What each presence was read from: kept for the review, never served.</summary>
    private const string Unserved = "presence-evidence.tsv";

    /// <summary>Record files whose <c>why</c> is written on a word as the note a reader's page can show.</summary>
    private static readonly string[] ReasonsServed =
        ["NamesReadPerVerseRecords.json", "NarrativeRecords.json"];

    /// <summary>
    /// The record files that write rows under a <c>source</c> of their own, and the reasons they put
    /// on words. The file-level <c>decidedBy</c> of these is not written on a row and is left alone.
    /// </summary>
    [Fact]
    public void No_record_file_credits_a_ticket_or_an_instruction()
    {
        var found = new List<string>();
        var loading = TestResources.Checkout("Essenthos.Forge", "Loading");
        foreach (var file in Directory.GetFiles(loading, "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var name = Path.GetFileName(file);
            var served = document.RootElement.ValueKind == JsonValueKind.Object
                         && (document.RootElement.TryGetProperty("rulings", out _) || ReasonsServed.Contains(name))
                ? new HashSet<string> { "source", "why" }
                : ["source"];
            Walk(found, Path.GetRelativePath(loading, file), document.RootElement, served);
        }

        found.Should().BeEmpty("the strings these properties hold become the source and the note of rows the API serves");
    }

    /// <summary>
    /// The record files the build reads, every column of every row: the credits become the source of the rows the API
    /// serves, and the lines, notes and reasons are served as they stand.
    /// </summary>
    [Fact]
    public void No_record_file_row_credits_a_ticket_or_an_instruction()
    {
        var found = new List<string>();
        var records = TestResources.Checkout("Resources", "Essenthos", "records");
        foreach (var file in Directory.GetFiles(records, "*.tsv", SearchOption.AllDirectories)
                     .Where(f => Path.GetFileName(f) != Unserved))
        {
            string[]? header = null;
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var cells = line.Split('	');
                if (header is null)
                {
                    header = cells;
                    continue;
                }

                for (var i = 0; i < cells.Length && i < header.Length; i++)
                {
                    if (TsvEscaping.Unescape(cells[i]) is { } text)
                    {
                        Check(found, $"{Path.GetRelativePath(records, file)} {header[i]}", text, CreditColumns.Contains(header[i]));
                    }
                }
            }
        }

        found.Should().BeEmpty("a record file's row is the source and the note of rows the API serves");
    }

    /// <summary>
    /// The owner's decided rows of the record files, which write those files at the next build: the credit their file
    /// gives (<c># by</c>) and every value they put.
    /// </summary>
    [Fact]
    public void No_decided_record_row_credits_a_ticket_or_an_instruction()
    {
        var found = new List<string>();
        var folder = TestResources.Checkout("Essenthos.Forge", "Records", "OwnerRows");
        foreach (var file in Directory.GetFiles(folder, "*.records.tsv"))
        {
            var name = Path.GetFileName(file);
            var values = -1;
            foreach (var line in File.ReadLines(file))
            {
                if (line.StartsWith("# by ", StringComparison.Ordinal))
                {
                    Check(found, $"{name} # by", line[5..].Trim(), credit: true);
                    continue;
                }

                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var cells = line.Split('	');
                if (values < 0)
                {
                    values = Array.IndexOf(cells, "values");
                    continue;
                }

                using var document = JsonDocument.Parse(TsvEscaping.Unescape(cells[values])!);
                foreach (var property in document.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    Check(found, $"{name} {property.Name}", property.Value.GetString()!, CreditColumns.Contains(property.Name));
                }
            }
        }

        found.Should().BeEmpty("a decided row's values and credit are written into the record files as they stand");
    }

    private static void Walk(List<string> found, string where, JsonElement element, HashSet<string> properties)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String && properties.Contains(property.Name))
                    {
                        Check(found, $"{where} {property.Name}", property.Value.GetString()!);
                    }
                    else
                    {
                        Walk(found, where, property.Value, properties);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(found, where, item, properties);
                }

                break;
        }
    }

    private static void Check(List<string> found, string where, string text, bool credit = false)
    {
        if (credit && OwnersWords().Match(text) is { Success: true } words)
        {
            found.Add($"{where}: quotes the owner's words: {Excerpt(text, words.Index)}");
        }

        if (Ticket().Match(text) is { Success: true } ticket)
        {
            found.Add($"{where}: names {ticket.Value}: {Excerpt(text, ticket.Index)}");
        }

        if (QuotedInstruction().Match(text) is { Success: true } quoted)
        {
            found.Add($"{where}: quotes an instruction ({quoted.Value}): {Excerpt(text, quoted.Index)}");
        }
    }

    private static string Excerpt(string text, int at) =>
        text.Substring(Math.Max(0, at - 40), Math.Min(text.Length - Math.Max(0, at - 40), 100));
}
