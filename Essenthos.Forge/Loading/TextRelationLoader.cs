using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

internal sealed record TextRelationOutcome(int Written, int Removed, int Kept, int Skipped, TimeSpan Elapsed)
{
    public override string ToString() => Written == 0 && Removed == 0
        ? $"the {Kept} relations between texts are already as the list states them"
        : $"{Written} relations between texts written and {Removed} removed, {Kept} kept, {Skipped} naming a " +
          $"text this corpus does not hold, in {Elapsed}";
}

/// <param name="From">The text the claim is about, by its slug.</param>
/// <param name="To">The text it stands in that relation to, by its slug.</param>
/// <param name="Relation">translated-from, revised-from, same-family-as or collated-against.</param>
/// <param name="Scope">Canonical book ranges, <c>1-39</c> or <c>67-79,80</c>; null for every book the text holds.</param>
/// <param name="Source">The work the claim rests on. Required: a relation nobody can check is not one.</param>
internal sealed record TextRelationEntry(
    string From,
    string To,
    string Relation,
    string? Scope,
    string? Note,
    string Source);

/// <summary>
/// What each text was translated from, revised from, compared with or shares a tradition with, from
/// the list this project keeps in <c>TextRelations.json</c>. Each claim names its texts by slug and
/// the work it rests on, and says only what that work or the text's own record establishes.
///
/// <para>
/// The list is the only thing that writes the table, so the table is made to match it: a claim no
/// longer listed is removed, one listed and missing is written, and a second run changes nothing.
/// A claim naming a text the corpus does not hold is passed over rather than refused, so a corpus
/// loaded without that text still gets the rest.
/// </para>
/// </summary>
internal sealed class TextRelationLoader(AppDbContext db, ILogger<TextRelationLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.TextRelations.json";

    /// <summary>The last book of the widest canon the corpus numbers, the Ge'ez one.</summary>
    internal const int LastCanonicalBook = 92;

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    public static readonly IReadOnlyList<TextRelationEntry> All = ReadList();

    public async Task<TextRelationOutcome> Load(CancellationToken cancellationToken = default) =>
        await Load(All, cancellationToken);

    internal async Task<TextRelationOutcome> Load(
        IReadOnlyList<TextRelationEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var texts = await db.Texts.ToDictionaryAsync(t => t.Slug, t => t.Id, StringComparer.OrdinalIgnoreCase,
            cancellationToken);

        var wanted = new List<TextRelation>();
        var skipped = 0;
        foreach (var entry in entries)
        {
            if (!texts.TryGetValue(entry.From, out var from) || !texts.TryGetValue(entry.To, out var to))
            {
                skipped++;
                continue;
            }

            wanted.Add(new TextRelation
            {
                FromTextId = from,
                ToTextId = to,
                Relation = EnumSpelling.ToTextRelationKind(entry.Relation),
                Scope = entry.Scope,
                Note = entry.Note,
                Source = entry.Source,
            });
        }

        var existing = await db.TextRelations.ToListAsync(cancellationToken);
        var stale = existing.Where(row => !wanted.Any(want => Same(row, want))).ToList();
        var missing = wanted.Where(want => !existing.Any(row => Same(row, want))).ToList();

        if (stale.Count > 0 || missing.Count > 0)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.TextRelations.RemoveRange(stale);
            db.TextRelations.AddRange(missing);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var outcome = new TextRelationOutcome(
            missing.Count, stale.Count, existing.Count - stale.Count, skipped, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }

    private static bool Same(TextRelation left, TextRelation right) =>
        left.FromTextId == right.FromTextId
        && left.ToTextId == right.ToTextId
        && left.Relation == right.Relation
        && left.Scope == right.Scope
        && left.Note == right.Note
        && left.Source == right.Source;

    private static List<TextRelationEntry> ReadList()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                $"{Resource} is not embedded in the Forge assembly. It is Loading/TextRelations.json, listed as an " +
                "EmbeddedResource in Essenthos.Forge.csproj; rebuild after restoring it.");
        var list = JsonSerializer.Deserialize<TextRelationList>(stream, Shape)
            ?? throw new InvalidDataException($"{Resource} holds no list of relations.");
        return Validate(list.Relations);
    }

    /// <summary>The list as it may be written, refused whole where any claim does not read.</summary>
    internal static List<TextRelationEntry> Validate(IReadOnlyList<TextRelationEntry> entries)
    {
        var seen = new HashSet<(string, string, string, string?)>();
        foreach (var entry in entries)
        {
            var claim = $"{entry.From} {entry.Relation} {entry.To}";
            if (string.IsNullOrWhiteSpace(entry.From) || string.IsNullOrWhiteSpace(entry.To) ||
                string.Equals(entry.From, entry.To, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The relation \"{claim}\" must name two different texts by slug.");
            }

            _ = EnumSpelling.ToTextRelationKind(entry.Relation);

            if (string.IsNullOrWhiteSpace(entry.Source))
            {
                throw new InvalidDataException(
                    $"The relation \"{claim}\" names no source. Name the work it rests on, or leave the claim out.");
            }

            if (entry.Scope is not null && Ranges(entry.Scope) is null)
            {
                throw new InvalidDataException(
                    $"The relation \"{claim}\" has the scope \"{entry.Scope}\", which does not read as canonical " +
                    $"book ranges between 1 and {LastCanonicalBook} separated by commas — 1-39, or 67-79,80.");
            }

            if (!seen.Add((entry.From.ToUpperInvariant(), entry.Relation, entry.To.ToUpperInvariant(), entry.Scope)))
            {
                throw new InvalidDataException($"The relation \"{claim}\" is listed twice for the same books.");
            }
        }

        return [.. entries];
    }

    /// <summary><c>1-39</c> or <c>67-79,80</c> as ranges of canonical books, or null where any piece does not read.</summary>
    internal static List<(int First, int Last)>? Ranges(string scope)
    {
        var ranges = new List<(int, int)>();
        foreach (var piece in scope.Split(','))
        {
            var ends = piece.Split('-');
            if (ends.Length is < 1 or > 2 ||
                !int.TryParse(ends[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
                !int.TryParse(ends[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var last) ||
                first < 1 || last < first || last > LastCanonicalBook)
            {
                return null;
            }

            ranges.Add((first, last));
        }

        return ranges;
    }

    private sealed record TextRelationList(IReadOnlyList<TextRelationEntry> Relations);
}
