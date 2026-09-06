using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// How one relation reads in one language: what comes before the target's name, the case the name
/// goes in, and what comes after it.
/// </summary>
/// <remarks>
/// Three fields and not a format string, because the name is a link and the pieces on either side
/// of it are not. A template with a hole in it would have to be split again by whoever renders it,
/// and splitting a sentence to find a link is what this whole layer exists to stop.
/// </remarks>
internal sealed record Phrasing(string Before, string Case, string After = "");

/// <summary>
/// What each relation says, in each language the encyclopedia speaks.
///
/// A relation is in the vocabulary only if it has a phrasing here, and the test over this asserts
/// exactly that: the loader refuses a relation that is not in <see cref="DescriptorRelations"/>, and
/// nothing may be in <see cref="DescriptorRelations"/> that a language it claims to speak cannot
/// say. The alternative is a reader meeting <c>father-in-law-of</c> on a page.
///
/// <para>
/// **A language with no table here renders nothing, and that is the correct answer for it.** The
/// tables are English, Ukrainian and Russian. German is not here on purpose: <em>vom Volk der
/// Moabiter</em> and <em>Prophet für Israel</em> are choices somebody who reads German has to make,
/// and thirty-eight phrasings guessed by somebody who does not would reach a German reader looking
/// exactly as authoritative as the three that were checked. Showing them the English is worse
/// still — it is the failure this replaces, one language further on.
/// </para>
///
/// <para>
/// The Slavic phrasings avoid the locative wherever a genitive construction says the same thing —
/// <em>мешканець Вифлеєма</em> for <em>lived in Bethlehem</em> — because the genitive is the form
/// DOC-0191 asks a pass to produce. Where only a locative will do, the phrasing asks for one and
/// the name falls back to English until a pass carries it.
/// </para>
/// </summary>
internal static class DescriptorPhrasings
{
    public const string English = "eng";

    public const string Ukrainian = "ukr";

    public const string Russian = "rus";

    /// <summary>What separates two clauses of one description, in every language carried.</summary>
    public const string Separator = ", ";

    private const string Nominative = GrammaticalCases.Nominative;

    private const string Genitive = GrammaticalCases.Genitive;

    private const string Locative = GrammaticalCases.Locative;

    /// <summary>
    /// Which language to render in. Asking for nothing is not asking for a language, so it gets
    /// English; asking for one the encyclopedia does not speak gets that language and no
    /// description, which is what <see cref="For"/> then says.
    /// </summary>
    public static string Spoken(string? asked) =>
        string.IsNullOrWhiteSpace(asked) ? English : asked.Trim();

    public static IReadOnlyDictionary<string, Phrasing>? For(string? language) =>
        language is { Length: > 0 } && ByLanguage.TryGetValue(language, out var phrasings)
            ? phrasings
            : null;

    public static IEnumerable<string> Languages => ByLanguage.Keys;


    /// <summary>
    /// English, which needs no form but the name as it stands — and, because of that, no possessive
    /// either. <em>father-in-law of Moses</em> rather than <em>Moses' father-in-law</em>: the second
    /// reads better and puts an apostrophe inside the link, which makes the name a thing the client
    /// has to take apart again.
    /// </summary>
    private static IReadOnlyDictionary<string, Phrasing> Eng { get; } =
        new Dictionary<string, Phrasing>(StringComparer.Ordinal)
        {
            [DescriptorRelations.SonOf] = new("son of ", Nominative),
            [DescriptorRelations.DaughterOf] = new("daughter of ", Nominative),
            [DescriptorRelations.FatherOf] = new("father of ", Nominative),
            [DescriptorRelations.MotherOf] = new("mother of ", Nominative),
            [DescriptorRelations.BrotherOf] = new("brother of ", Nominative),
            [DescriptorRelations.SisterOf] = new("sister of ", Nominative),
            [DescriptorRelations.HusbandOf] = new("husband of ", Nominative),
            [DescriptorRelations.WifeOf] = new("wife of ", Nominative),
            [DescriptorRelations.GrandfatherOf] = new("grandfather of ", Nominative),
            [DescriptorRelations.GrandmotherOf] = new("grandmother of ", Nominative),
            [DescriptorRelations.AncestorOf] = new("ancestor of ", Nominative),
            [DescriptorRelations.DescendantOf] = new("descendant of ", Nominative),
            [DescriptorRelations.FatherInLawOf] = new("father-in-law of ", Nominative),
            [DescriptorRelations.MotherInLawOf] = new("mother-in-law of ", Nominative),
            [DescriptorRelations.SonInLawOf] = new("son-in-law of ", Nominative),
            [DescriptorRelations.DaughterInLawOf] = new("daughter-in-law of ", Nominative),
            [DescriptorRelations.KingOf] = new("king of ", Nominative),
            [DescriptorRelations.QueenOf] = new("queen of ", Nominative),
            [DescriptorRelations.ProphetTo] = new("prophet to ", Nominative),
            [DescriptorRelations.PriestOf] = new("priest of ", Nominative),
            [DescriptorRelations.JudgeOf] = new("judge of ", Nominative),
            [DescriptorRelations.HighPriestOf] = new("high priest of ", Nominative),
            [DescriptorRelations.CommanderOf] = new("commander of ", Nominative),
            [DescriptorRelations.ServantOf] = new("servant of ", Nominative),
            [DescriptorRelations.DiscipleOf] = new("disciple of ", Nominative),
            [DescriptorRelations.ApostleOf] = new("apostle of ", Nominative),
            [DescriptorRelations.ScribeOf] = new("scribe of ", Nominative),
            [DescriptorRelations.OfTribe] = new("of the tribe of ", Nominative),
            [DescriptorRelations.OfPeople] = new("one of the ", Nominative),
            [DescriptorRelations.FromPlace] = new("from ", Nominative),
            [DescriptorRelations.LivedIn] = new("lived in ", Nominative),
            [DescriptorRelations.BuriedIn] = new("buried in ", Nominative),
            [DescriptorRelations.DescendantsOf] = new("descendants of ", Nominative),
            [DescriptorRelations.CityIn] = new("a city in ", Nominative),
            [DescriptorRelations.RegionOf] = new("a region of ", Nominative),
            [DescriptorRelations.RiverOf] = new("a river of ", Nominative),
            [DescriptorRelations.MountainIn] = new("a mountain in ", Nominative),
            [DescriptorRelations.Near] = new("near ", Nominative),
        };

    /// <summary>
    /// Ukrainian, which is the language this whole layer was asked for in: <em>тесть Мойсея</em>,
    /// not <em>тесть Мойсей</em>, and <em>нащадки Моава</em> with Moab a link.
    /// </summary>
    private static IReadOnlyDictionary<string, Phrasing> Ukr { get; } =
        new Dictionary<string, Phrasing>(StringComparer.Ordinal)
        {
            [DescriptorRelations.SonOf] = new("син ", Genitive),
            [DescriptorRelations.DaughterOf] = new("дочка ", Genitive),
            [DescriptorRelations.FatherOf] = new("батько ", Genitive),
            [DescriptorRelations.MotherOf] = new("мати ", Genitive),
            [DescriptorRelations.BrotherOf] = new("брат ", Genitive),
            [DescriptorRelations.SisterOf] = new("сестра ", Genitive),
            [DescriptorRelations.HusbandOf] = new("чоловік ", Genitive),
            [DescriptorRelations.WifeOf] = new("дружина ", Genitive),
            [DescriptorRelations.GrandfatherOf] = new("дід ", Genitive),
            [DescriptorRelations.GrandmotherOf] = new("бабуся ", Genitive),
            [DescriptorRelations.AncestorOf] = new("предок ", Genitive),
            [DescriptorRelations.DescendantOf] = new("нащадок ", Genitive),
            [DescriptorRelations.FatherInLawOf] = new("тесть ", Genitive),
            [DescriptorRelations.MotherInLawOf] = new("теща ", Genitive),
            [DescriptorRelations.SonInLawOf] = new("зять ", Genitive),
            [DescriptorRelations.DaughterInLawOf] = new("невістка ", Genitive),
            [DescriptorRelations.KingOf] = new("цар ", Genitive),
            [DescriptorRelations.QueenOf] = new("цариця ", Genitive),
            [DescriptorRelations.ProphetTo] = new("пророк ", Genitive),
            [DescriptorRelations.PriestOf] = new("священник ", Genitive),
            [DescriptorRelations.JudgeOf] = new("суддя ", Genitive),
            [DescriptorRelations.HighPriestOf] = new("первосвященник ", Genitive),
            [DescriptorRelations.CommanderOf] = new("воєначальник ", Genitive),
            [DescriptorRelations.ServantOf] = new("слуга ", Genitive),
            [DescriptorRelations.DiscipleOf] = new("учень ", Genitive),
            [DescriptorRelations.ApostleOf] = new("апостол ", Genitive),
            [DescriptorRelations.ScribeOf] = new("писар ", Genitive),
            [DescriptorRelations.OfTribe] = new("з племені ", Genitive),
            [DescriptorRelations.OfPeople] = new("з народу ", Genitive),
            [DescriptorRelations.FromPlace] = new("з ", Genitive),
            [DescriptorRelations.LivedIn] = new("мешканець ", Genitive),
            [DescriptorRelations.BuriedIn] = new("похований у ", Locative),
            [DescriptorRelations.DescendantsOf] = new("нащадки ", Genitive),
            [DescriptorRelations.CityIn] = new("місто в ", Locative),
            [DescriptorRelations.RegionOf] = new("область ", Genitive),
            [DescriptorRelations.RiverOf] = new("річка ", Genitive),
            [DescriptorRelations.MountainIn] = new("гора в ", Locative),
            [DescriptorRelations.Near] = new("біля ", Genitive),
        };

    private static IReadOnlyDictionary<string, Phrasing> Rus { get; } =
        new Dictionary<string, Phrasing>(StringComparer.Ordinal)
        {
            [DescriptorRelations.SonOf] = new("сын ", Genitive),
            [DescriptorRelations.DaughterOf] = new("дочь ", Genitive),
            [DescriptorRelations.FatherOf] = new("отец ", Genitive),
            [DescriptorRelations.MotherOf] = new("мать ", Genitive),
            [DescriptorRelations.BrotherOf] = new("брат ", Genitive),
            [DescriptorRelations.SisterOf] = new("сестра ", Genitive),
            [DescriptorRelations.HusbandOf] = new("муж ", Genitive),
            [DescriptorRelations.WifeOf] = new("жена ", Genitive),
            [DescriptorRelations.GrandfatherOf] = new("дед ", Genitive),
            [DescriptorRelations.GrandmotherOf] = new("бабушка ", Genitive),
            [DescriptorRelations.AncestorOf] = new("предок ", Genitive),
            [DescriptorRelations.DescendantOf] = new("потомок ", Genitive),
            [DescriptorRelations.FatherInLawOf] = new("тесть ", Genitive),
            [DescriptorRelations.MotherInLawOf] = new("теща ", Genitive),
            [DescriptorRelations.SonInLawOf] = new("зять ", Genitive),
            [DescriptorRelations.DaughterInLawOf] = new("невестка ", Genitive),
            [DescriptorRelations.KingOf] = new("царь ", Genitive),
            [DescriptorRelations.QueenOf] = new("царица ", Genitive),
            [DescriptorRelations.ProphetTo] = new("пророк ", Genitive),
            [DescriptorRelations.PriestOf] = new("священник ", Genitive),
            [DescriptorRelations.JudgeOf] = new("судья ", Genitive),
            [DescriptorRelations.HighPriestOf] = new("первосвященник ", Genitive),
            [DescriptorRelations.CommanderOf] = new("военачальник ", Genitive),
            [DescriptorRelations.ServantOf] = new("слуга ", Genitive),
            [DescriptorRelations.DiscipleOf] = new("ученик ", Genitive),
            [DescriptorRelations.ApostleOf] = new("апостол ", Genitive),
            [DescriptorRelations.ScribeOf] = new("писец ", Genitive),
            [DescriptorRelations.OfTribe] = new("из колена ", Genitive),
            [DescriptorRelations.OfPeople] = new("из народа ", Genitive),
            [DescriptorRelations.FromPlace] = new("из ", Genitive),
            [DescriptorRelations.LivedIn] = new("житель ", Genitive),
            [DescriptorRelations.BuriedIn] = new("похоронен в ", Locative),
            [DescriptorRelations.DescendantsOf] = new("потомки ", Genitive),
            [DescriptorRelations.CityIn] = new("город в ", Locative),
            [DescriptorRelations.RegionOf] = new("область ", Genitive),
            [DescriptorRelations.RiverOf] = new("река ", Genitive),
            [DescriptorRelations.MountainIn] = new("гора в ", Locative),
            [DescriptorRelations.Near] = new("около ", Genitive),
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Phrasing>>
        ByLanguage = new Dictionary<string, IReadOnlyDictionary<string, Phrasing>>(StringComparer.Ordinal)
        {
            [English] = Eng,
            [Ukrainian] = Ukr,
            [Russian] = Rus,
        };
}
