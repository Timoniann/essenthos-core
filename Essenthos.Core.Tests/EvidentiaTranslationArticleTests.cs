using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// German and Spanish write an article the original does not: <em>der HERR</em> for יְהוָה, <em>la
/// ciudad</em> for πόλις. The parse finds the article, and what it misreads as one - a demonstrative,
/// a relative, a clitic, the numeral <em>one</em> - is not said to be supplied, nor is an article the
/// original does write a few words from its noun.
/// </summary>
public class EvidentiaTranslationArticleTests
{
    private static readonly EvidentiaAddress Genesis27 = new(1, 2, 7);
    private static readonly EvidentiaAddress Genesis28 = new(1, 2, 8);
    private static readonly EvidentiaAddress Matthew514 = new(40, 5, 14);

    private static readonly LanguagePackRegistry Packs =
        new([new GermanLanguagePack(), new SpanishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void AGermanArticleIsSuppliedWhereTheOriginalWritesNoneForItsNoun()
    {
        var der = German(1, 1, "der", "DET", Article("Masc", "Sing"));
        var herr = German(2, 2, "HERR", "PROPN");
        var die = German(3, 3, "die", "DET", Article("Fem", "Sing"));
        var erde = German(4, 4, "Erde", "NOUN", ("Gender", "Fem"), ("Number", "Sing"));
        var yhwh = Hebrew(11, 1, "יְהוָה", "H3068", "nmpr");
        var article = Hebrew(12, 2, "הָ", "H9009", "art", joined: true);
        var earth = Hebrew(13, 3, "אָרֶץ", "H776", "subs");

        var absences = Resolve([der, herr, die, erde], [yhwh, article, earth], (herr, yhwh), (erde, earth));

        absences.Should().ContainSingle().Which.Should().Be((1L, EvidentiaAbsenceRule.GermanUnwrittenArticle));
    }

    [Fact]
    public void ADemonstrativeOrRelativeTheParseTookForAnArticleIsNotSupplied()
    {
        var das = German(1, 1, "das", "DET", Article("Neut", "Sing"));
        var brueder = German(2, 2, "Brüder", "NOUN", ("Gender", "Masc"), ("Number", "Plur"));
        var der = German(3, 3, "der", "DET", Article("Masc", "Sing"));
        var wohnte = German(4, 4, "wohnte", "VERB");
        var zu = German(5, 5, "zu", "ADP");
        var aroer = German(6, 6, "Aroer", "PROPN");
        var brothers = Hebrew(11, 1, "אַחִים", "H251", "subs");
        var town = Hebrew(12, 2, "עֲרֹעֵר", "H6177", "nmpr");

        Resolve([das, brueder, der, wohnte, zu, aroer], [brothers, town], (brueder, brothers), (aroer, town))
            .Should().BeEmpty();
    }

    [Fact]
    public void AnIndefiniteArticleIsNotSuppliedWhereItIsTheNumeralOrTheOriginalWritesAnArticle()
    {
        var ein = German(1, 1, "ein", "DET", Article("Neut", "Sing"));
        var fleisch = German(2, 2, "Fleisch", "NOUN", ("Gender", "Neut"), ("Number", "Sing"));
        var einem = German(3, 3, "einem", "DET", Article("Neut", "Sing"));
        var haus = German(4, 4, "Haus", "NOUN", ("Gender", "Neut"), ("Number", "Sing"));
        var nicht = German(5, 5, "nicht", "PART", ("Polarity", "Neg"));
        var einWeiser = German(6, 6, "ein", "DET", Article("Masc", "Sing"));
        var weiser = German(7, 7, "Weiser", "NOUN", ("Gender", "Masc"), ("Number", "Sing"));
        var eines = German(8, 1, "ein", "DET", Article("Neut", "Sing"), Genesis28);
        var lamm = German(9, 2, "Lamm", "NOUN", Genesis28, ("Gender", "Neut"), ("Number", "Sing"));
        var flesh = Hebrew(11, 1, "בָשָׂר", "H1320", "subs");
        var one = Hebrew(12, 2, "אֶחָד", "H259", "subs");
        var to = Hebrew(13, 3, "לַ", "H9005", "prep", joined: true);
        var article = Hebrew(14, 4, "", "H9009", "art", joined: true);
        var house = Hebrew(15, 5, "בָּיִת", "H1004", "subs");
        var wise = Hebrew(16, 6, "חָכָם", "H2450", "adjv");
        var lamb = Hebrew(17, 1, "שֶׂה", "H7716", "subs") with { Address = Genesis28 };

        var absences = Resolve(
            [ein, fleisch, einem, haus, nicht, einWeiser, weiser, eines, lamm],
            [flesh, one, to, article, house, wise, lamb],
            (fleisch, flesh), (haus, house), (weiser, wise), (lamm, lamb));

        absences.Should().ContainSingle().Which.Should().Be((8L, EvidentiaAbsenceRule.GermanIndefiniteArticle));
    }

    [Fact]
    public void ASpanishCliticBeforeAVerbTheParseReadAsANounIsNotAnArticle()
    {
        var los = Spanish(1, 1, "los", "DET", Article("Masc", "Plur"));
        var santifico = Spanish(2, 2, "santifico", "NOUN", ("Gender", "Masc"), ("Number", "Plur"));
        var sanctify = Hebrew(11, 1, "אֲקַדְּשֵׁם", "H6942", "verb", morphology: ("person", "p1"));

        Resolve([los, santifico], [sanctify], (santifico, sanctify)).Should().BeEmpty();
    }

    [Fact]
    public void AGreekArticleStandingAwayFromItsNounIsStillItsArticle()
    {
        var el = Spanish(1, 1, "el", "DET", Article("Masc", "Sing"));
        var cuerpo = Spanish(2, 2, "cuerpo", "NOUN", ("Gender", "Masc"), ("Number", "Sing"));
        var laDolor = Spanish(3, 3, "el", "DET", Article("Masc", "Sing"));
        var dolor = Spanish(4, 4, "dolor", "NOUN", ("Gender", "Masc"), ("Number", "Sing"));
        var una = Spanish(5, 5, "la", "DET", Article("Fem", "Sing"));
        var ciudad = Spanish(6, 6, "ciudad", "NOUN", ("Gender", "Fem"), ("Number", "Sing"));
        var to = Greek(11, 1, "τὸ", "G3588", "det", "nominative", "singular", "neuter");
        var de = Greek(12, 2, "δὲ", "G1161", "conj");
        var body = Greek(13, 3, "σῶμα", "G4983", "noun", "nominative", "singular", "neuter");
        var he = Greek(14, 4, "ἡ", "G3588", "det", "nominative", "singular", "feminine");
        var gar = Greek(15, 5, "γὰρ", "G1063", "conj");
        var kata = Greek(16, 6, "κατὰ", "G2596", "prep");
        var theon = Greek(17, 7, "Θεὸν", "G2316", "noun", "accusative", "singular", "masculine");
        var grief = Greek(18, 8, "λύπη", "G3077", "noun", "nominative", "singular", "feminine");
        var city = Greek(19, 9, "πόλις", "G4172", "noun", "nominative", "singular", "feminine");

        var absences = Resolve(
            [el, cuerpo, laDolor, dolor, una, ciudad],
            [to, de, body, he, gar, kata, theon, grief, city],
            (cuerpo, body), (dolor, grief), (ciudad, city));

        absences.Should().ContainSingle().Which.Should().Be((5L, EvidentiaAbsenceRule.SpanishUnwrittenArticle));
    }

    [Fact]
    public void EachRuleIsHeldAsSurelyAsItWasMeasured()
    {
        EvidentiaAbsenceRule.GermanUnwrittenArticle.OutranksTheAligner.Should().BeTrue();
        EvidentiaAbsenceRule.SpanishUnwrittenArticle.OutranksTheAligner.Should().BeTrue();
        EvidentiaAbsenceRule.Named("supplied-article-spa").Should().Be(EvidentiaAbsenceRule.SpanishUnwrittenArticle);
        EvidentiaAbsenceRule.GermanUnwrittenArticle.Rationale.Should().Contain("UDPipe");
    }

    [Theory]
    [InlineData("der", "deu", true)]
    [InlineData("Haus", "deu", false)]
    [InlineData("los", "spa", true)]
    [InlineData("á", "spa", true)]
    [InlineData("casa", "spa", false)]
    [InlineData("seine", "deu", false)]
    [InlineData("sondern", "deu", false)]
    [InlineData("weil", "deu", false)]
    [InlineData("nuestro", "spa", false)]
    [InlineData("sino", "spa", false)]
    public void EachLanguageIsFilteredByItsOwnFunctionWords(string surface, string language, bool function)
    {
        Packs.TryAnalyse(new EvidentiaToken(1, Genesis27, 1, surface, language), out var analysis).Should().BeTrue();

        analysis.WordClass.Should().Be(function ? EvidentiaWordClass.Function : EvidentiaWordClass.Content);
    }

    private static (long, EvidentiaAbsenceRule)[] Resolve(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (EvidentiaToken Source, EvidentiaToken Target)[] placed)
    {
        var proposals = placed.Select(pair => new EvidentiaProposal(
            Analysis(pair.Source), Analysis(pair.Target), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.9)).ToList();
        return [.. EvidentiaAbsences.Resolve([.. source.Select(Analysis)], [.. target.Select(Analysis)], proposals)
            .Select(absence => (absence.Word.Token.Id, absence.Rule))];
    }

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static (string, string)[] Article(string gender, string number) =>
        [("PronType", "Art"), ("Gender", gender), ("Number", number)];

    private static EvidentiaToken German(long id, int position, string surface, string partOfSpeech,
        params (string Name, string Value)[] features) =>
        Translation(id, Genesis27, position, surface, "deu", partOfSpeech, features);

    private static EvidentiaToken German(long id, int position, string surface, string partOfSpeech,
        (string Name, string Value)[] features, EvidentiaAddress address) =>
        Translation(id, address, position, surface, "deu", partOfSpeech, features);

    private static EvidentiaToken German(long id, int position, string surface, string partOfSpeech,
        EvidentiaAddress address, params (string Name, string Value)[] features) =>
        Translation(id, address, position, surface, "deu", partOfSpeech, features);

    private static EvidentiaToken Spanish(long id, int position, string surface, string partOfSpeech,
        params (string Name, string Value)[] features) =>
        Translation(id, Matthew514, position, surface, "spa", partOfSpeech, features);

    private static EvidentiaToken Translation(long id, EvidentiaAddress address, int position, string surface,
        string language, string partOfSpeech, (string Name, string Value)[] features) =>
        new(id, address, position, surface, language, Trailer: " ", PartOfSpeech: partOfSpeech,
            Morphology: features.Length == 0 ? null : features.ToDictionary(feature => feature.Name, feature => feature.Value));

    private static EvidentiaToken Hebrew(
        long id, int position, string surface, string strong, string partOfSpeech, bool joined = false,
        params (string Name, string Value)[] morphology) =>
        new(id, Genesis27, position, surface, "hbo", Trailer: joined ? string.Empty : " ", StrongNumber: strong,
            PartOfSpeech: partOfSpeech,
            Morphology: morphology.Append(("pos", partOfSpeech)).ToDictionary(feature => feature.Item1, feature => feature.Item2));

    private static EvidentiaToken Greek(long id, int position, string surface, string strong, string partOfSpeech,
        string? grammaticalCase = null, string? number = null, string? gender = null)
    {
        var morphology = new Dictionary<string, string> { ["pos"] = partOfSpeech };
        if (grammaticalCase is not null) morphology["case"] = grammaticalCase;
        if (number is not null) morphology["number"] = number;
        if (gender is not null) morphology["gender"] = gender;
        return new(id, Matthew514, position, surface, "grc", Trailer: " ", StrongNumber: strong,
            PartOfSpeech: partOfSpeech, Morphology: morphology);
    }
}
