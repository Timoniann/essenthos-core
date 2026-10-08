using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;

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

internal static partial class DescriptorPhrasings
{
    /// <summary>
    /// The Ukrainian preposition that has to agree with the word after it.
    ///
    /// <para>
    /// §11 of the orthography decides one direction and only one: <em>у</em> stands before a word
    /// beginning with <em>в</em> or <em>ф</em>, whatever the word before it ended in. So
    /// <em>місто в Вавилоні</em> is wrong and <em>місто у Вавилоні</em> is right, and 32 of the
    /// corpus's locatives are that.
    /// </para>
    ///
    /// <para>
    /// The other direction is deliberately left alone. A consonant before a vowel — <em>жив у
    /// Авані</em> — is not one of the cases §11 settles, both are written, and changing it would be
    /// taste rather than orthography.
    /// </para>
    ///
    /// <para>
    /// It cannot live in the table beside the phrase, because the right word depends on the name
    /// that follows and a phrasing is written before the name is known.
    /// </para>
    /// </summary>
    public static string AgreeWithWhatFollows(string before, string next)
    {
        var first = next.AsSpan().TrimStart();
        if (first.IsEmpty || (first[0] != 'в' && first[0] != 'В'
            && first[0] != 'ф' && first[0] != 'Ф'))
        {
            return before;
        }

        var lead = before.AsSpan().TrimEnd();
        return lead.Length > 1 && lead[^1] == 'в' && char.IsWhiteSpace(lead[^2])
            ? string.Concat(before.AsSpan(0, lead.Length - 1), "у", before.AsSpan(lead.Length))
            : before;
    }
}

/// <summary>
/// What each relation says, in each language the encyclopedia speaks.
///
/// A relation is in the vocabulary only if it has a phrasing here, and the test over this asserts
/// exactly that: the loader refuses a relation that is not in <see cref="DescriptorRelations"/>, and
/// nothing may be in <see cref="DescriptorRelations"/> that a language it claims to speak cannot
/// say. The alternative is a reader meeting <c>father-in-law-of</c> on a page.
///
/// <para>
/// **The tables are the languages the interface is written in**, which the owner settled as
/// English, Ukrainian, German and Spanish. Russian had a table before that and no longer does: the
/// client carries no Russian catalogue, so a Russian line had nothing around it to be read in,
/// and a phrasing table for a language the product does not offer is a sentence nobody can reach.
/// The Russian forms in <see cref="Database.Entities.EntityNameForm"/> are left where they are —
/// they cost nothing to keep and a table is cheap to write again — but nothing renders them.
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
/// the case its own phrase puts the name in rather than for the genitive the other fifty-three
/// take. <c>gate-of</c> is the near miss: it names a place and still takes the genitive, because
/// <em>брама Єрусалима</em> says whose the gate is rather than where it stands. <c>lived-in</c> was
/// phrased around the gap once, as <em>мешканець Вифлеєма</em>, while nothing produced a locative;
/// that says something else — an inhabitant rather than someone who lived there — and it is no
/// longer needed.
///
/// A form the generation pass has not produced falls back to the English name and never to another
/// case: <em>жив у Hebron</em> is visibly a gap, and <em>жив у Хеврона</em> would read as Ukrainian
/// and be wrong.
/// </para>
/// </summary>
internal static partial class DescriptorPhrasings
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
            [DescriptorRelations.HalfBrotherOf] = new("half-brother of ", Nominative),
            [DescriptorRelations.HalfSisterOf] = new("half-sister of ", Nominative),
            [DescriptorRelations.GrandfatherOf] = new("grandfather of ", Nominative),
            [DescriptorRelations.GrandmotherOf] = new("grandmother of ", Nominative),
            [DescriptorRelations.GrandsonOf] = new("grandson of ", Nominative),
            [DescriptorRelations.GranddaughterOf] = new("granddaughter of ", Nominative),
            [DescriptorRelations.UncleOf] = new("uncle of ", Nominative),
            [DescriptorRelations.AuntOf] = new("aunt of ", Nominative),
            [DescriptorRelations.NephewOf] = new("nephew of ", Nominative),
            [DescriptorRelations.NieceOf] = new("niece of ", Nominative),
            [DescriptorRelations.AncestorOf] = new("ancestor of ", Nominative),
            [DescriptorRelations.DescendantOf] = new("descendant of ", Nominative),
            [DescriptorRelations.FatherInLawOf] = new("father-in-law of ", Nominative),
            [DescriptorRelations.MotherInLawOf] = new("mother-in-law of ", Nominative),
            [DescriptorRelations.SonInLawOf] = new("son-in-law of ", Nominative),
            [DescriptorRelations.DaughterInLawOf] = new("daughter-in-law of ", Nominative),
            [DescriptorRelations.BrotherInLawOf] = new("brother-in-law of ", Nominative),
            [DescriptorRelations.SisterInLawOf] = new("sister-in-law of ", Nominative),
            [DescriptorRelations.ConcubineOf] = new("concubine of ", Nominative),
            [DescriptorRelations.CousinOf] = new("cousin of ", Nominative),
            [DescriptorRelations.KingOf] = new("king of ", Nominative),
            [DescriptorRelations.QueenOf] = new("queen of ", Nominative),
            [DescriptorRelations.ProphetTo] = new("prophet to ", Nominative),
            [DescriptorRelations.PriestOf] = new("priest of ", Nominative),
            [DescriptorRelations.JudgeOf] = new("judge of ", Nominative),
            [DescriptorRelations.HighPriestOf] = new("high priest of ", Nominative),
            [DescriptorRelations.CommanderOf] = new("commander of ", Nominative),
            [DescriptorRelations.GovernorOf] = new("governor of ", Nominative),
            [DescriptorRelations.TetrarchOf] = new("tetrarch of ", Nominative),
            [DescriptorRelations.ServantOf] = new("servant of ", Nominative),
            [DescriptorRelations.MasterOf] = new("master of ", Nominative),
            [DescriptorRelations.DiscipleOf] = new("disciple of ", Nominative),
            [DescriptorRelations.ApostleOf] = new("apostle of ", Nominative),
            [DescriptorRelations.ScribeOf] = new("scribe of ", Nominative),
            [DescriptorRelations.CompanionOf] = new("companion of ", Nominative),
            [DescriptorRelations.TeacherOf] = new("teacher of ", Nominative),
            [DescriptorRelations.AllyOf] = new("ally of ", Nominative),
            [DescriptorRelations.FounderOf] = new("founder of ", Nominative),
            [DescriptorRelations.KilledBy] = new("killed by ", Nominative),
            [DescriptorRelations.KillerOf] = new("killer of ", Nominative),
            [DescriptorRelations.RapedBy] = new("raped by ", Nominative),
            [DescriptorRelations.RaperOf] = new("raped ", Nominative),
            [DescriptorRelations.SupporterOf] = new("supporter of ", Nominative),
            [DescriptorRelations.SupportedBy] = new("supported by ", Nominative),
            [DescriptorRelations.CreatorOf] = new("creator of ", Nominative),
            [DescriptorRelations.CreatedBy] = new("created by ", Nominative),
            [DescriptorRelations.HeirOf] = new("heir of ", Nominative),
            [DescriptorRelations.InheritedBy] = new("whose heir was ", Nominative),
            [DescriptorRelations.ExilerOf] = new("carried into exile ", Nominative),
            [DescriptorRelations.ExiledBy] = new("carried into exile by ", Nominative),
            [DescriptorRelations.AngelOf] = new("angel of ", Nominative),
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
            [DescriptorRelations.GateOf] = new("a gate of ", Nominative),
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
            [DescriptorRelations.HalfBrotherOf] = new("зведений брат ", Genitive),
            [DescriptorRelations.HalfSisterOf] = new("зведена сестра ", Genitive),
            [DescriptorRelations.GrandfatherOf] = new("дід ", Genitive),
            [DescriptorRelations.GrandmotherOf] = new("бабуся ", Genitive),
            [DescriptorRelations.GrandsonOf] = new("онук ", Genitive),
            [DescriptorRelations.GranddaughterOf] = new("онука ", Genitive),
            [DescriptorRelations.UncleOf] = new("дядько ", Genitive),
            [DescriptorRelations.AuntOf] = new("тітка ", Genitive),
            [DescriptorRelations.NephewOf] = new("племінник ", Genitive),
            [DescriptorRelations.NieceOf] = new("племінниця ", Genitive),
            [DescriptorRelations.AncestorOf] = new("предок ", Genitive),
            [DescriptorRelations.DescendantOf] = new("нащадок ", Genitive),
            [DescriptorRelations.FatherInLawOf] = new("тесть ", Genitive),
            [DescriptorRelations.MotherInLawOf] = new("теща ", Genitive),
            [DescriptorRelations.SonInLawOf] = new("зять ", Genitive),
            [DescriptorRelations.DaughterInLawOf] = new("невістка ", Genitive),

            // Свояк and своячка are the members of the sibling-in-law set that do not say which
            // side of the marriage the tie runs through; шурин and дівер both do, and the claim
            // does not.
            [DescriptorRelations.BrotherInLawOf] = new("свояк ", Genitive),
            [DescriptorRelations.SisterInLawOf] = new("своячка ", Genitive),
            [DescriptorRelations.ConcubineOf] = new("наложниця ", Genitive),
            [DescriptorRelations.CousinOf] = new("двоюрідний родич ", Genitive),
            [DescriptorRelations.KingOf] = new("цар ", Genitive),
            [DescriptorRelations.QueenOf] = new("цариця ", Genitive),
            [DescriptorRelations.ProphetTo] = new("пророк ", Genitive),
            [DescriptorRelations.PriestOf] = new("священник ", Genitive),
            [DescriptorRelations.JudgeOf] = new("суддя ", Genitive),
            [DescriptorRelations.HighPriestOf] = new("первосвященник ", Genitive),
            [DescriptorRelations.CommanderOf] = new("воєначальник ", Genitive),
            [DescriptorRelations.GovernorOf] = new("намісник ", Genitive),
            [DescriptorRelations.TetrarchOf] = new("тетрарх ", Genitive),
            [DescriptorRelations.ServantOf] = new("слуга ", Genitive),
            [DescriptorRelations.MasterOf] = new("господар ", Genitive),
            [DescriptorRelations.DiscipleOf] = new("учень ", Genitive),
            [DescriptorRelations.ApostleOf] = new("апостол ", Genitive),
            [DescriptorRelations.ScribeOf] = new("писар ", Genitive),
            [DescriptorRelations.CompanionOf] = new("товариш ", Genitive),
            [DescriptorRelations.TeacherOf] = new("учитель ", Genitive),
            [DescriptorRelations.AllyOf] = new("союзник ", Genitive),
            [DescriptorRelations.FounderOf] = new("засновник ", Genitive),

            // "Загинув від руки X" rather than the instrumental "убитий X" the phrase would
            // otherwise want: the instrumental is a case no pass has produced a form in, and a
            // clause asking for one would render the English name for every entity in the corpus.
            [DescriptorRelations.KilledBy] = new("загинув від руки ", Genitive),
            [DescriptorRelations.KillerOf] = new("вбивця ", Genitive),
            [DescriptorRelations.RapedBy] = new("зазнала насильства від ", Genitive),
            [DescriptorRelations.RaperOf] = new("насильник ", Genitive),
            [DescriptorRelations.SupporterOf] = new("покровитель ", Genitive),
            [DescriptorRelations.SupportedBy] = new("мав підтримку від ", Genitive),
            [DescriptorRelations.CreatorOf] = new("творець ", Genitive),
            [DescriptorRelations.CreatedBy] = new("творіння ", Genitive),
            [DescriptorRelations.HeirOf] = new("спадкоємець ", Genitive),

            // "Аврам, чий спадкоємець — Еліезер": a dash and the nominative, because the direct
            // phrasing wants the instrumental, a case no pass has produced a form in.
            [DescriptorRelations.InheritedBy] = new("чий спадкоємець — ", Nominative),
            [DescriptorRelations.ExilerOf] = new("полонитель ", Genitive),
            [DescriptorRelations.ExiledBy] = new("у полоні в ", Genitive),
            [DescriptorRelations.AngelOf] = new("ангел ", Genitive),
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
            [DescriptorRelations.GateOf] = new("брама ", Genitive),
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
    /// <em>vom Stamm Levis</em> would be wrong. A place after a preposition is the dative, which
    /// the pass produced as the locative where the name is not bare — <em>im alten Teich</em> — and
    /// which for a proper name is the nominative unchanged, so <em>begraben in Hebron</em> falls
    /// back to it (<see cref="Say"/>). The two homicide clauses lean on that twice more: the agent of a
    /// passive takes <em>von</em> and the dative, and the object of <em>töten</em> takes an
    /// accusative, and a proper name marks neither.
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
            [DescriptorRelations.HalfBrotherOf] = new("Halbbruder ", Genitive),
            [DescriptorRelations.HalfSisterOf] = new("Halbschwester ", Genitive),
            [DescriptorRelations.GrandfatherOf] = new("Großvater ", Genitive),
            [DescriptorRelations.GrandmotherOf] = new("Großmutter ", Genitive),
            [DescriptorRelations.GrandsonOf] = new("Enkel ", Genitive),
            [DescriptorRelations.GranddaughterOf] = new("Enkelin ", Genitive),
            [DescriptorRelations.UncleOf] = new("Onkel ", Genitive),
            [DescriptorRelations.AuntOf] = new("Tante ", Genitive),
            [DescriptorRelations.NephewOf] = new("Neffe ", Genitive),
            [DescriptorRelations.NieceOf] = new("Nichte ", Genitive),
            [DescriptorRelations.AncestorOf] = new("Vorfahr ", Genitive),
            [DescriptorRelations.DescendantOf] = new("Nachkomme ", Genitive),
            [DescriptorRelations.FatherInLawOf] = new("Schwiegervater ", Genitive),
            [DescriptorRelations.MotherInLawOf] = new("Schwiegermutter ", Genitive),
            [DescriptorRelations.SonInLawOf] = new("Schwiegersohn ", Genitive),
            [DescriptorRelations.DaughterInLawOf] = new("Schwiegertochter ", Genitive),

            // Schwager and Schwägerin keep the same silence the Ukrainian pair does: they say a
            // marriage stands between the two and not which side of it the tie runs through, which
            // is all the claim says.
            [DescriptorRelations.BrotherInLawOf] = new("Schwager ", Genitive),
            [DescriptorRelations.SisterInLawOf] = new("Schwägerin ", Genitive),
            [DescriptorRelations.ConcubineOf] = new("Nebenfrau ", Genitive),
            [DescriptorRelations.CousinOf] = new("Vetter ", Genitive),
            [DescriptorRelations.KingOf] = new("König ", Genitive),
            [DescriptorRelations.QueenOf] = new("Königin ", Genitive),
            [DescriptorRelations.ProphetTo] = new("Prophet für ", Nominative),
            [DescriptorRelations.PriestOf] = new("Priester ", Genitive),
            [DescriptorRelations.JudgeOf] = new("Richter ", Genitive),
            [DescriptorRelations.HighPriestOf] = new("Hoherpriester ", Genitive),
            [DescriptorRelations.CommanderOf] = new("Feldhauptmann ", Genitive),
            [DescriptorRelations.GovernorOf] = new("Statthalter ", Genitive),
            [DescriptorRelations.TetrarchOf] = new("Tetrarch ", Genitive),
            [DescriptorRelations.ServantOf] = new("Knecht ", Genitive),
            [DescriptorRelations.MasterOf] = new("Herr ", Genitive),
            [DescriptorRelations.DiscipleOf] = new("Jünger ", Genitive),
            [DescriptorRelations.ApostleOf] = new("Apostel ", Genitive),
            [DescriptorRelations.ScribeOf] = new("Schreiber ", Genitive),
            [DescriptorRelations.CompanionOf] = new("Gefährte ", Genitive),
            [DescriptorRelations.TeacherOf] = new("Lehrer ", Genitive),
            [DescriptorRelations.AllyOf] = new("Verbündeter ", Genitive),
            [DescriptorRelations.FounderOf] = new("Gründer ", Genitive),

            // Neither homicide clause calls the killing a crime, because the relation does not:
            // Mörder would convict David of Goliath. The plain verb states what happened and
            // leaves the rest to the verse the clause carries.
            [DescriptorRelations.KilledBy] = new("getötet von ", Nominative),
            [DescriptorRelations.KillerOf] = new("tötete ", Nominative),
            [DescriptorRelations.RapedBy] = new("vergewaltigt von ", Nominative),
            [DescriptorRelations.RaperOf] = new("vergewaltigte ", Nominative),
            [DescriptorRelations.SupporterOf] = new("Förderer ", Genitive),
            [DescriptorRelations.SupportedBy] = new("unterstützt von ", Nominative),
            [DescriptorRelations.CreatorOf] = new("Schöpfer ", Genitive),
            [DescriptorRelations.CreatedBy] = new("geschaffen von ", Nominative),
            [DescriptorRelations.HeirOf] = new("Erbe ", Genitive),
            [DescriptorRelations.InheritedBy] = new("dessen Erbe war ", Nominative),
            [DescriptorRelations.ExilerOf] = new("führte in die Verbannung ", Nominative),
            [DescriptorRelations.ExiledBy] = new("in die Verbannung geführt von ", Nominative),
            [DescriptorRelations.AngelOf] = new("Engel ", Genitive),
            [DescriptorRelations.OfTribe] = new("vom Stamm ", Nominative),
            [DescriptorRelations.OfPeople] = new("aus dem Volk der ", Nominative),
            [DescriptorRelations.FromPlace] = new("aus ", Locative),
            [DescriptorRelations.LivedIn] = new("wohnte in ", Locative),
            [DescriptorRelations.BuriedIn] = new("begraben in ", Locative),
            [DescriptorRelations.DescendantsOf] = new("Nachkommen ", Genitive),
            [DescriptorRelations.CityIn] = new("eine Stadt in ", Locative),
            [DescriptorRelations.RegionOf] = new("eine Landschaft in ", Locative),
            [DescriptorRelations.RiverOf] = new("ein Fluss in ", Locative),
            [DescriptorRelations.MountainIn] = new("ein Berg in ", Locative),
            [DescriptorRelations.GateOf] = new("ein Tor ", Genitive),
            [DescriptorRelations.Near] = new("bei ", Locative),
        };

    /// <summary>
    /// Spanish, which inflects a name for nothing and says <em>de</em>. Every phrasing takes the
    /// nominative, and <c>spa</c> is asked for that one form for the same reason.
    ///
    /// <para>
    /// <c>killer-of</c> is the one clause that does not say <em>de</em>. Spanish has no neutral
    /// agent noun for it — <em>matador</em> belongs to a bullring and <em>asesino</em> convicts —
    /// so it is the verb, and the verb takes the personal <em>a</em>: <em>dio muerte a Goliat</em>.
    /// </para>
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
            [DescriptorRelations.HalfBrotherOf] = new("medio hermano de ", Nominative),
            [DescriptorRelations.HalfSisterOf] = new("media hermana de ", Nominative),
            [DescriptorRelations.GrandfatherOf] = new("abuelo de ", Nominative),
            [DescriptorRelations.GrandmotherOf] = new("abuela de ", Nominative),
            [DescriptorRelations.GrandsonOf] = new("nieto de ", Nominative),
            [DescriptorRelations.GranddaughterOf] = new("nieta de ", Nominative),
            [DescriptorRelations.UncleOf] = new("tío de ", Nominative),
            [DescriptorRelations.AuntOf] = new("tía de ", Nominative),
            [DescriptorRelations.NephewOf] = new("sobrino de ", Nominative),
            [DescriptorRelations.NieceOf] = new("sobrina de ", Nominative),
            [DescriptorRelations.AncestorOf] = new("antepasado de ", Nominative),
            [DescriptorRelations.DescendantOf] = new("descendiente de ", Nominative),
            [DescriptorRelations.FatherInLawOf] = new("suegro de ", Nominative),
            [DescriptorRelations.MotherInLawOf] = new("suegra de ", Nominative),
            [DescriptorRelations.SonInLawOf] = new("yerno de ", Nominative),
            [DescriptorRelations.DaughterInLawOf] = new("nuera de ", Nominative),
            [DescriptorRelations.BrotherInLawOf] = new("cuñado de ", Nominative),
            [DescriptorRelations.SisterInLawOf] = new("cuñada de ", Nominative),
            [DescriptorRelations.ConcubineOf] = new("concubina de ", Nominative),
            [DescriptorRelations.CousinOf] = new("primo de ", Nominative),
            [DescriptorRelations.KingOf] = new("rey de ", Nominative),
            [DescriptorRelations.QueenOf] = new("reina de ", Nominative),
            [DescriptorRelations.ProphetTo] = new("profeta de ", Nominative),
            [DescriptorRelations.PriestOf] = new("sacerdote de ", Nominative),
            [DescriptorRelations.JudgeOf] = new("juez de ", Nominative),
            [DescriptorRelations.HighPriestOf] = new("sumo sacerdote de ", Nominative),
            [DescriptorRelations.CommanderOf] = new("capitán del ejército de ", Nominative),
            [DescriptorRelations.GovernorOf] = new("gobernador de ", Nominative),
            [DescriptorRelations.TetrarchOf] = new("tetrarca de ", Nominative),
            [DescriptorRelations.ServantOf] = new("siervo de ", Nominative),
            [DescriptorRelations.MasterOf] = new("amo de ", Nominative),
            [DescriptorRelations.DiscipleOf] = new("discípulo de ", Nominative),
            [DescriptorRelations.ApostleOf] = new("apóstol de ", Nominative),
            [DescriptorRelations.ScribeOf] = new("escriba de ", Nominative),
            [DescriptorRelations.CompanionOf] = new("compañero de ", Nominative),
            [DescriptorRelations.TeacherOf] = new("maestro de ", Nominative),
            [DescriptorRelations.AllyOf] = new("aliado de ", Nominative),
            [DescriptorRelations.FounderOf] = new("fundador de ", Nominative),
            [DescriptorRelations.KilledBy] = new("muerto a manos de ", Nominative),
            [DescriptorRelations.KillerOf] = new("dio muerte a ", Nominative),
            [DescriptorRelations.RapedBy] = new("violada por ", Nominative),
            [DescriptorRelations.RaperOf] = new("violó a ", Nominative),
            [DescriptorRelations.SupporterOf] = new("benefactor de ", Nominative),
            [DescriptorRelations.SupportedBy] = new("sostenido por ", Nominative),
            [DescriptorRelations.CreatorOf] = new("creador de ", Nominative),
            [DescriptorRelations.CreatedBy] = new("creado por ", Nominative),
            [DescriptorRelations.HeirOf] = new("heredero de ", Nominative),
            [DescriptorRelations.InheritedBy] = new("cuyo heredero era ", Nominative),
            [DescriptorRelations.ExilerOf] = new("llevó al exilio a ", Nominative),
            [DescriptorRelations.ExiledBy] = new("llevado al exilio por ", Nominative),
            [DescriptorRelations.AngelOf] = new("ángel de ", Nominative),
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
            [DescriptorRelations.GateOf] = new("una puerta de ", Nominative),
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

/// <summary>
/// What German and Spanish put between a phrase and a name that a Biblical proper name does not
/// need: the article. <em>Sohn Isais</em> takes none, <em>Knecht des HERRN</em>, <em>König der
/// Israeliten</em> and <em>rey de los israelitas</em> do, and leaving it out is <em>Knecht HERRN</em>.
/// </summary>
internal static partial class DescriptorPhrasings
{
    private static readonly string[] GermanArticles = ["der ", "die ", "das ", "des ", "dem ", "den "];

    /// <summary>
    /// A German preposition and the dative article after it as German writes them together:
    /// <em>wohnte im alten Teich</em>, never <em>wohnte in dem alten Teich</em>.
    /// </summary>
    private static readonly Dictionary<(string Preposition, string Article), string> Contracted = new()
    {
        [("in", "dem")] = "im",
        [("an", "dem")] = "am",
        [("bei", "dem")] = "beim",
        [("von", "dem")] = "vom",
        [("zu", "dem")] = "zum",
        [("zu", "der")] = "zur",
    };

    /// <summary>
    /// The words before the name and the name, in the case the phrasing wants, for one target.
    ///
    /// <para>
    /// Every language takes the form of that case the pass produced, and the English name where it
    /// produced none — a gap a reader can see, never an inflection guessed here. German and Spanish
    /// add only what their grammar fixes whatever the name: a people is a plural, so its genitive is
    /// <em>der Israeliten</em> and its Spanish <em>de los israelitas</em> — where the name is a plural;
    /// the Reina Valera's own <em>rey de Israel</em> takes none; a name with no German
    /// genitive is said with <em>von</em> and the name as it stands, which is its dative — <em>Sohn
    /// von Isai</em>; a German place clause takes the dative the pass produced as the locative, or
    /// a bare name unchanged, and joins the preposition to its article. An article the form already
    /// carries — <em>des HERRN</em>, <em>der breiten Mauer</em> — is the form's and is kept.
    /// </para>
    /// </summary>
    public static (string Before, string Name) Say(
        string language,
        Phrasing phrasing,
        EntityKind kind,
        IReadOnlyDictionary<string, string>? cases,
        string englishName)
    {
        var before = phrasing.Before;
        var form = cases?.GetValueOrDefault(phrasing.Case);

        if (language == German && form is null && cases is not null)
        {
            if (phrasing.Case == Genitive && cases.GetValueOrDefault(Nominative) is { } nominative)
            {
                return (before + "von ", nominative);
            }

            if (phrasing.Case == Locative)
            {
                form = cases.GetValueOrDefault(Nominative);
            }
        }

        if (form is null)
        {
            return (before, englishName);
        }

        if (language == German && kind == EntityKind.People && phrasing.Case == Genitive && !Articled(form))
        {
            return (before + "der ", form);
        }

        if (language == German && phrasing.Case == Locative)
        {
            var space = form.IndexOf(' ');
            var lead = before.TrimEnd();
            var last = lead.LastIndexOf(' ') + 1;
            if (space > 0 && Contracted.TryGetValue((lead[last..], form[..space]), out var joined))
            {
                return (lead[..last] + joined + " ", form[(space + 1)..]);
            }
        }

        if (language == Spanish && kind == EntityKind.People && before.EndsWith(" de ", StringComparison.Ordinal)
            && form.EndsWith('s'))
        {
            return (before + "los ", form);
        }

        return (AgreeWithWhatFollows(before, form), form);
    }

    private static bool Articled(string form) =>
        GermanArticles.Any(article => form.StartsWith(article, StringComparison.Ordinal));
}
