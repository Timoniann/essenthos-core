using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// A period's name in the reader's language, said from its record where the English says no more
/// than the record does: <em>Jacob</em> (a life) is the person's name, <em>Jehoshaphat's reign as king
/// over Judah</em> is a set phrase around the person's name and a land. Every other period takes the
/// rendering of its English name held with the other texts' renderings (<c>records/i18n/&lt;language&gt;/texts</c>),
/// and keeps its English where none is held.
/// </summary>
internal static partial class PeriodNames
{
    /// <summary>The set phrases, each with where the person's name goes: the genitive where the language declines it.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Phrases = new(StringComparer.Ordinal)
    {
        ["ukr"] = new() { ["reign"] = "Царювання {0}", ["prophet"] = "Пророче служіння {0}", ["judge"] = "Суддівство {0}" },
        ["rus"] = new() { ["reign"] = "Царствование {0}", ["prophet"] = "Пророческое служение {0}", ["judge"] = "Судейство {0}" },
        ["deu"] = new() { ["reign"] = "Herrschaft von {0}", ["prophet"] = "Prophetisches Wirken von {0}", ["judge"] = "Richteramt von {0}" },
        ["spa"] = new() { ["reign"] = "Reinado de {0}", ["prophet"] = "Ministerio profético de {0}", ["judge"] = "Judicatura de {0}" },
    };

    /// <summary>The lands a reign is over, in each language.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Lands = new(StringComparer.Ordinal)
    {
        ["ukr"] = new() { ["Judah"] = "над Юдою", ["Israel"] = "над Ізраїлем", ["Persia"] = "над Персією", ["Babylon"] = "над Вавилоном" },
        ["rus"] = new() { ["Judah"] = "над Иудой", ["Israel"] = "над Израилем", ["Persia"] = "над Персией", ["Babylon"] = "над Вавилоном" },
        ["deu"] = new() { ["Judah"] = "über Juda", ["Israel"] = "über Israel", ["Persia"] = "über Persien", ["Babylon"] = "über Babylon" },
        ["spa"] = new() { ["Judah"] = "sobre Judá", ["Israel"] = "sobre Israel", ["Persia"] = "sobre Persia", ["Babylon"] = "sobre Babilonia" },
    };

    /// <summary>The languages that put the person in the genitive; the others name him as he is named.</summary>
    private static readonly HashSet<string> Declined = new(StringComparer.Ordinal) { "ukr", "rus" };

    // "Jehoshaphat's reign as king over Judah", "David's reign over Judah", "Ahab's reign as king".
    [GeneratedRegex(@"^[^']+'s (?:sole )?reign(?: as king)?(?: over (?<land>Judah|Israel|Persia|Babylon))?$")]
    private static partial Regex Reign();

    [GeneratedRegex(@"^[^']+'s? time as prophet$")]
    private static partial Regex Prophet();

    [GeneratedRegex(@"^[^']+'s? time as judge of Israel$")]
    private static partial Regex Judge();

    /// <summary>The phrase a period's English name is, and the land it names; null where it is none of them.</summary>
    internal static (string Phrase, string? Land)? Phrase(string name, string? kind)
    {
        if (kind == "reign" && Reign().Match(name) is { Success: true } reign)
        {
            return ("reign", reign.Groups["land"].Success ? reign.Groups["land"].Value : null);
        }

        if (kind == "ministry" && Prophet().IsMatch(name))
        {
            return ("prophet", null);
        }

        return kind == "judgeship" && Judge().IsMatch(name) ? ("judge", null) : null;
    }

    /// <summary>
    /// Each period's name in <paramref name="language"/> where it can be said from its record or a rendering
    /// of its English is held, keyed by slug. Three reads: the people's names, where the language declines
    /// them their genitives, and the renderings of the names the records cannot say.
    /// </summary>
    public static async Task<Dictionary<string, string>> Of(
        AppDbContext db,
        IReadOnlyList<(string Slug, string Name, string? Kind, string? Person)> periods,
        string? language,
        CancellationToken cancellationToken)
    {
        var local = EntityNames.Local(language);
        if (local is null || periods.Count == 0)
        {
            return [];
        }

        var people = periods.Select(p => p.Person).OfType<string>().Distinct().ToList();
        var ids = await db.Entities.Where(e => people.Contains(e.Slug)).Select(e => new { e.Id, e.Slug })
            .ToListAsync(cancellationToken);
        var names = await EntityNames.Of(db, [.. ids.Select(i => i.Id)], local, cancellationToken);
        var forms = Declined.Contains(local)
            ? await Relationships.Forms(db, [.. ids.Select(i => i.Id)], local, cancellationToken)
            : [];
        var bySlug = ids.ToDictionary(i => i.Slug, i => i.Id, StringComparer.Ordinal);

        var said = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var period in periods)
        {
            if (period.Person is not { } person || !bySlug.TryGetValue(person, out var id)
                || !names.TryGetValue(id, out var named))
            {
                continue;
            }

            if (period.Kind == "life")
            {
                said[period.Slug] = named;
                continue;
            }

            if (Phrase(period.Name, period.Kind) is not { } phrase
                || !Phrases.TryGetValue(local, out var phrases)
                || !phrases.TryGetValue(phrase.Phrase, out var pattern))
            {
                continue;
            }

            string? shown = Declined.Contains(local)
                ? forms.GetValueOrDefault(person)?.GetValueOrDefault(GrammaticalCases.Genitive)
                : named;
            if (shown is null)
            {
                continue;
            }

            var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, shown);
            if (phrase.Land is { } land)
            {
                text += " " + Lands[local][land];
            }

            said[period.Slug] = text;
        }

        var rendered = await TextTranslations.Of(
            db, periods.Where(p => !said.ContainsKey(p.Slug)).Select(p => p.Name), local, cancellationToken);
        foreach (var period in periods)
        {
            if (!said.ContainsKey(period.Slug) && TextTranslations.In(rendered, period.Name) is { } text)
            {
                said[period.Slug] = text;
            }
        }

        return said;
    }
}
