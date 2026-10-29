using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Alternatives given their reason's renderings, or given new ones.</param>
/// <param name="Cleared">Alternatives whose reason no entry renders any more, so their renderings went.</param>
/// <param name="Unrendered">Alternatives whose reason no entry renders, which the page shows in English.</param>
internal sealed record AlternativeReasonOutcome(int Written, int Cleared, int Unrendered, TimeSpan Elapsed)
{
    public override string ToString() =>
        Written == 0 && Cleared == 0
            ? $"the reasons of every alternative are already in every language ({Unrendered} with none)"
            : $"{Written} alternatives' reasons given in every language, {Cleared} renderings of a reason no " +
              $"longer said taken off, {Unrendered} reasons with none, in {Elapsed}";
}

/// <summary>
/// Why a record may be another, in Ukrainian, German and Spanish: machine translations of the English
/// reasons, read from <c>AlternativeReasonTranslations.json</c> and written beside each alternative whose
/// reason is exactly an entry's English, whoever wrote the alternative (the rulings, their backs, the
/// titles).
///
/// <para>
/// An entry is found by its English and nothing else, so a reason rewritten since it was rendered finds
/// none and the page shows the English, which is what the record says. Idempotent: a row is written only
/// where what it holds differs, and all of it is one save.
/// </para>
/// </summary>
internal sealed class AlternativeReasonTranslationLoader(AppDbContext db, ILogger<AlternativeReasonTranslationLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.AlternativeReasonTranslations.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Task<AlternativeReasonOutcome> Load(CancellationToken cancellationToken = default) =>
        Load(Read(), cancellationToken);

    internal async Task<AlternativeReasonOutcome> Load(
        AlternativeReasonTranslations file,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var byEnglish = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in file.Entries)
        {
            byEnglish.TryAdd(entry.English, ReasonRenderings.Write(entry.Languages()));
        }

        var rows = await db.EntityAlternatives.ToListAsync(cancellationToken);
        int written = 0, cleared = 0, unrendered = 0;
        foreach (var row in rows)
        {
            var wanted = byEnglish.GetValueOrDefault(row.Reason);
            var by = wanted is null ? null : file.Source;
            if (wanted is null)
            {
                unrendered++;
            }

            if (ReasonRenderings.Same(row.ReasonRenderings, wanted) && row.ReasonRenderedBy == by)
            {
                continue;
            }

            if (wanted is null)
            {
                cleared++;
            }
            else
            {
                written++;
            }

            row.ReasonRenderings = wanted;
            row.ReasonRenderedBy = by;
        }

        await db.SaveChangesAsync(cancellationToken);
        var outcome = new AlternativeReasonOutcome(written, cleared, unrendered, started.Elapsed);
        logger.LogInformation("The alternatives' reasons in every language: {Outcome}", outcome);
        return outcome;
    }

    internal static AlternativeReasonTranslations Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<AlternativeReasonTranslations>(stream, Shape)
               ?? throw new InvalidDataException($"{Resource} is empty.");
    }
}

/// <summary>
/// The renderings, as the generation run writes them: who rendered them, and each English reason with
/// its Ukrainian, German and Spanish. The shape of <c>Resources/Essenthos/translations/record-texts.json</c>.
/// </summary>
internal sealed record AlternativeReasonTranslations(string Source, IReadOnlyList<AlternativeReasonTranslation> Entries);

internal sealed record AlternativeReasonTranslation(string English, string? Ukr, string? Deu, string? Spa)
{
    public IReadOnlyDictionary<string, string> Languages() =>
        new (string Language, string? Text)[] { ("ukr", Ukr), ("deu", Deu), ("spa", Spa) }
            .Where(one => !string.IsNullOrWhiteSpace(one.Text))
            .ToDictionary(one => one.Language, one => one.Text!.Trim(), StringComparer.Ordinal);
}
