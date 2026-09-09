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
/// **The tables are the languages the interface is written in**, which FTR-0280 settled as English,
/// Ukrainian, German and Spanish. Russian had a table before that and no longer does: the client
/// carries no Russian catalogue, so a Russian line had nothing around it to be read in, and a
/// phrasing table for a language the product does not offer is a sentence nobody can reach. The
/// Russian forms in <see cref="Database.Entities.EntityNameForm"/> are left where they are — they
/// cost nothing to keep and a table is cheap to write again — but nothing renders them.
/// </para>
///
/// <para>
/// **A language with no table here is answered in English, and the answer says so.**
/// <see cref="Spoken"/> is where that is decided, and it is what <c>descriptor.language</c>
/// carries, so a client never puts one language's grammar around another language's name.
/// </para>
///
/// <para>
/// **The four clauses a place page is made of ask for the locative, and no genitive stands in.**
/// <c>lived-in</c>, <c>buried-in</c>, <c>city-in</c> and <c>mountain-in</c> put their target in it
/// in Ukrainian — <em>жив у Вифлеємі</em>, not <em>жив у Вифлеєма</em> — so each phrasing asks for
/// the case its own phrase puts the name in rather than for the genitive the
/// other thirty-four take. <c>lived-in</c> was phrased around the gap once, as
/// <em>мешканець Вифлеєма</em>, while nothing produced a locative; that says something else — an
/// inhabitant rather than someone who lived there — and it is no longer needed.
///
/// A form the generation pass has not produced falls back to the English name and never to another
/// case: <em>жив у Hebron</em> is visibly a gap, and <em>жив у Хеврона</em> would read as Ukrainian
/// and be wrong.
/// </para>
/// </summary>
internal static class DescriptorPhrasings
{
    public const string English = "eng";

    public const string Ukrainian = "ukr";

    public const string German = "deu";

    public const string Spanish = "spa";

    /// <summary>What separates two clauses of one description, in every language carried.</summary>
    public const string Separator = ", ";

    private const string Nominative = GrammaticalCases.Nominative;

    private const string Genitive = GrammaticalCases.Genitive;

    private const string Locative = GrammaticalCases.Locative;

    /// <summary>
    /// Which language a description is actually rendered in, given the one asked for. Asking for
    /// nothing is not asking for a language, and asking for one the encyclopedia does not speak is
    /// answered in English rather than refused.
    ///
    /// <para>
    /// **This, and not the parameter, is what the answer reports.** A client that is told the
    /// language it asked for and handed the language it got puts Ukrainian grammar around an English
    /// name and prints <em>син Reuel</em> — the sentence the fallback exists to prevent. So the
    /// fallback is decided in one place and travels on the wire.
    /// </para>
    /// </summary>
    public static string Spoken(string? asked)
    {
        var wanted = string.IsNullOrWhiteSpace(asked) ? English : asked.Trim();
        return ByLanguage.ContainsKey(wanted) ? wanted : English;
    }

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
            [DescriptorRelations.LivedIn] = new("жив у ", Locative),
            [DescriptorRelations.BuriedIn] = new("похований у ", Locative),
            [DescriptorRelations.DescendantsOf] = new("нащадки ", Genitive),
            [DescriptorRelations.CityIn] = new("місто в ", Locative),
            [DescriptorRelations.RegionOf] = new("область ", Genitive),
            [DescriptorRelations.RiverOf] = new("річка ", Genitive),
            [DescriptorRelations.MountainIn] = new("гора в ", Locative),
            [DescriptorRelations.Near] = new("біля ", Genitive),
        };

    /// <summary>
    /// German, whose genitive is the one thing about it that is regular here: a Biblical name takes
    /// <em>-s</em> and stands without an article — <em>Sohn Aarons</em>, <em>Knecht Gottes</em> —
    /// which is the form <c>deu</c> already holds beside the nominative.
    ///
    /// <para>
    /// Where the German phrase does not want a genitive it asks for the nominative rather than
    /// bending the sentence to use one. <em>vom Stamm Levi</em> names the tribe plainly and
    /// <em>vom Stamm Levis</em> would be wrong; a place after a preposition is the dative, which
    /// for a proper name is the nominative unchanged, so <em>begraben in Hebron</em> needs no
    /// fourth case in the table.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, Phrasing> Deu { get; } =
        new Dictionary<string, Phrasing>(StringComparer.Ordinal)
        {
            [DescriptorRelations.SonOf] = new("Sohn ", Genitive),
            [DescriptorRelations.DaughterOf] = new("Tochter ", Genitive),
            [DescriptorRelations.FatherOf] = new("Vater ", Genitive),
            [DescriptorRelations.MotherOf] = new("Mutter ", Genitive),
            [DescriptorRelations.BrotherOf] = new("Bruder ", Genitive),
            [DescriptorRelations.SisterOf] = new("Schwester ", Genitive),
            [DescriptorRelations.HusbandOf] = new("Mann ", Genitive),
            [DescriptorRelations.WifeOf] = new("Frau ", Genitive),
            [DescriptorRelations.GrandfatherOf] = new("Großvater ", Genitive),
            [DescriptorRelations.GrandmotherOf] = new("Großmutter ", Genitive),
            [DescriptorRelations.AncestorOf] = new("Vorfahr ", Genitive),
            [DescriptorRelations.DescendantOf] = new("Nachkomme ", Genitive),
            [DescriptorRelations.FatherInLawOf] = new("Schwiegervater ", Genitive),
            [DescriptorRelations.MotherInLawOf] = new("Schwiegermutter ", Genitive),
            [DescriptorRelations.SonInLawOf] = new("Schwiegersohn ", Genitive),
            [DescriptorRelations.DaughterInLawOf] = new("Schwiegertochter ", Genitive),
            [DescriptorRelations.KingOf] = new("König ", Genitive),
            [DescriptorRelations.QueenOf] = new("Königin ", Genitive),
            [DescriptorRelations.ProphetTo] = new("Prophet für ", Nominative),
            [DescriptorRelations.PriestOf] = new("Priester ", Genitive),
            [DescriptorRelations.JudgeOf] = new("Richter ", Genitive),
            [DescriptorRelations.HighPriestOf] = new("Hoherpriester ", Genitive),
            [DescriptorRelations.CommanderOf] = new("Feldhauptmann ", Genitive),
            [DescriptorRelations.ServantOf] = new("Knecht ", Genitive),
            [DescriptorRelations.DiscipleOf] = new("Jünger ", Genitive),
            [DescriptorRelations.ApostleOf] = new("Apostel ", Genitive),
            [DescriptorRelations.ScribeOf] = new("Schreiber ", Genitive),
            [DescriptorRelations.OfTribe] = new("vom Stamm ", Nominative),
            [DescriptorRelations.OfPeople] = new("aus dem Volk der ", Nominative),
            [DescriptorRelations.FromPlace] = new("aus ", Nominative),
            [DescriptorRelations.LivedIn] = new("wohnte in ", Nominative),
            [DescriptorRelations.BuriedIn] = new("begraben in ", Nominative),
            [DescriptorRelations.DescendantsOf] = new("Nachkommen ", Genitive),
            [DescriptorRelations.CityIn] = new("eine Stadt in ", Nominative),
            [DescriptorRelations.RegionOf] = new("eine Landschaft in ", Nominative),
            [DescriptorRelations.RiverOf] = new("ein Fluss in ", Nominative),
            [DescriptorRelations.MountainIn] = new("ein Berg in ", Nominative),
            [DescriptorRelations.Near] = new("bei ", Nominative),
        };

    /// <summary>
    /// Spanish, which inflects a name for nothing and says <em>de</em>. Every phrasing takes the
    /// nominative, and <c>spa</c> is asked for that one form for the same reason.
    /// </summary>
    private static IReadOnlyDictionary<string, Phrasing> Spa { get; } =
        new Dictionary<string, Phrasing>(StringComparer.Ordinal)
        {
            [DescriptorRelations.SonOf] = new("hijo de ", Nominative),
            [DescriptorRelations.DaughterOf] = new("hija de ", Nominative),
            [DescriptorRelations.FatherOf] = new("padre de ", Nominative),
            [DescriptorRelations.MotherOf] = new("madre de ", Nominative),
            [DescriptorRelations.BrotherOf] = new("hermano de ", Nominative),
            [DescriptorRelations.SisterOf] = new("hermana de ", Nominative),
            [DescriptorRelations.HusbandOf] = new("marido de ", Nominative),
            [DescriptorRelations.WifeOf] = new("mujer de ", Nominative),
            [DescriptorRelations.GrandfatherOf] = new("abuelo de ", Nominative),
            [DescriptorRelations.GrandmotherOf] = new("abuela de ", Nominative),
            [DescriptorRelations.AncestorOf] = new("antepasado de ", Nominative),
            [DescriptorRelations.DescendantOf] = new("descendiente de ", Nominative),
            [DescriptorRelations.FatherInLawOf] = new("suegro de ", Nominative),
            [DescriptorRelations.MotherInLawOf] = new("suegra de ", Nominative),
            [DescriptorRelations.SonInLawOf] = new("yerno de ", Nominative),
            [DescriptorRelations.DaughterInLawOf] = new("nuera de ", Nominative),
            [DescriptorRelations.KingOf] = new("rey de ", Nominative),
            [DescriptorRelations.QueenOf] = new("reina de ", Nominative),
            [DescriptorRelations.ProphetTo] = new("profeta de ", Nominative),
            [DescriptorRelations.PriestOf] = new("sacerdote de ", Nominative),
            [DescriptorRelations.JudgeOf] = new("juez de ", Nominative),
            [DescriptorRelations.HighPriestOf] = new("sumo sacerdote de ", Nominative),
            [DescriptorRelations.CommanderOf] = new("capitán del ejército de ", Nominative),
            [DescriptorRelations.ServantOf] = new("siervo de ", Nominative),
            [DescriptorRelations.DiscipleOf] = new("discípulo de ", Nominative),
            [DescriptorRelations.ApostleOf] = new("apóstol de ", Nominative),
            [DescriptorRelations.ScribeOf] = new("escriba de ", Nominative),
            [DescriptorRelations.OfTribe] = new("de la tribu de ", Nominative),
            [DescriptorRelations.OfPeople] = new("del pueblo de los ", Nominative),
            [DescriptorRelations.FromPlace] = new("de ", Nominative),
            [DescriptorRelations.LivedIn] = new("habitó en ", Nominative),
            [DescriptorRelations.BuriedIn] = new("sepultado en ", Nominative),
            [DescriptorRelations.DescendantsOf] = new("descendientes de ", Nominative),
            [DescriptorRelations.CityIn] = new("una ciudad en ", Nominative),
            [DescriptorRelations.RegionOf] = new("una región de ", Nominative),
            [DescriptorRelations.RiverOf] = new("un río de ", Nominative),
            [DescriptorRelations.MountainIn] = new("un monte en ", Nominative),
            [DescriptorRelations.Near] = new("cerca de ", Nominative),
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Phrasing>>
        ByLanguage = new Dictionary<string, IReadOnlyDictionary<string, Phrasing>>(StringComparer.Ordinal)
        {
            [English] = Eng,
            [Ukrainian] = Ukr,
            [German] = Deu,
            [Spanish] = Spa,
        };
}
