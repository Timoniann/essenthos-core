using System.Text.Json;
using System.Text.RegularExpressions;
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
        + @"in chat \d|instruction of \d{4}-\d{2}-\d{2} to |word of \d{4}-\d{2}-\d{2} to |, option [a-z]\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex QuotedInstruction();

    /// <summary>Record files whose <c>why</c> is written on a word as the note a reader's page can show.</summary>
    private static readonly string[] ReasonsServed =
        ["NamesReadPerVerseRecords.json", "EponymReadings.json", "NarrativeRecords.json"];

    /// <summary>
    /// The lines that credit a decision in their own <c>decidedBy</c>: a descriptor clause, a passage
    /// reading and a kin statement each write it into the source of the row it makes.
    /// </summary>
    [Fact]
    public void No_line_that_credits_a_decision_names_a_ticket_or_an_instruction()
    {
        var found = new List<string>();
        foreach (var folder in new[] { "descriptors", "references", "kin" })
        {
            var essenthos = TestResources.Shipped(Path.Combine("Essenthos", folder));
            foreach (var file in Directory.GetFiles(essenthos, "*.jsonl"))
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(file).Where(line => !string.IsNullOrWhiteSpace(line)))
                {
                    lineNumber++;
                    using var record = JsonDocument.Parse(line);
                    Walk(found, $"{folder}/{Path.GetFileName(file)}:{lineNumber}", record.RootElement, ["decidedBy"]);
                }
            }
        }

        found.Should().BeEmpty("a decision's credit is served with the row, and a reader is owed where it comes from, not our board");
    }

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

    private static void Check(List<string> found, string where, string text)
    {
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
