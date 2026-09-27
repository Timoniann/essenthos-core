using Essenthos.Core.Corpus;
﻿using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Strong;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// The encyclopedia as the loader builds it, read from the real BibleData files.
///
/// Everything here is a count over the whole dataset rather than a fixture, because every defect
/// these tests are about was a whole-dataset defect that each spot check passed: fourteen of
/// fifteen names had a right Strong number while a thousand Greek ones were being discarded, and
/// the disputed flag was right on the labels anyone thought to look at.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed partial class EncyclopediaTests : IClassFixture<BibleDataCorpus>
{
    private readonly BibleDataCorpus _corpus;
    private readonly ITestOutputHelper _output;

    public EncyclopediaTests(BibleDataCorpus corpus, ITestOutputHelper output)
    {
        _corpus = corpus;
        _output = output;
    }

    /// <summary>
    /// The whole load in numbers, printed so a change in any of them is visible rather than
    /// inferred, and asserted so that a change is deliberate. Every figure here was measured
    /// against BibleData 2026; a later version of the dataset moving one of them is a thing to
    /// look at, not a thing to have happened quietly.
    /// </summary>
    [Fact]
    public void TheLoadCounts()
    {
        var strongNumbers = _corpus.Names
            .SelectMany(n => Split(n.HebrewStrongNumber).Concat(Split(n.GreekStrongNumber)))
            .Count();

        _output.WriteLine($"names                    {_corpus.Names.Count}");
        _output.WriteLine($"  with a Hebrew number   {_corpus.Names.Count(n => n.HebrewStrongNumber is not null)}");
        _output.WriteLine($"  with a Greek number    {_corpus.Names.Count(n => n.GreekStrongNumber is not null)}");
        _output.WriteLine($"  Strong numbers in all  {strongNumbers}");
        _output.WriteLine($"  on places              {_corpus.PlaceNames.Count}");
        _output.WriteLine($"  on Jesus               {_corpus.NamesOf("jesus").Count}");
        _output.WriteLine($"relationships            {_corpus.Relationships.Count}");
        _output.WriteLine($"  duplicates dropped     {_corpus.Duplicates}");
        _output.WriteLine($"  without a reciprocal   {_corpus.Unpaired}");
        _output.WriteLine($"references               {_corpus.References.Count}");
        _output.WriteLine($"  disputed               {_corpus.Disputed}");
        _output.WriteLine($"  on Jesus               {_corpus.References.Count(r => r.EntityId == _corpus.Jesus.Id)}");
        _output.WriteLine($"prose still holding a row identifier {Leaked().Count}");

        // Two names of the 3,894 are this corpus's own and not the label file's: the divine name
        // pointed as Elohim, H3069, and Paul called Σαούλ. Talitha's and Jashar's are not loaded,
        // with their records.
        _corpus.Names.Should().HaveCount(3_894);
        _corpus.Names.Count(n => n.HebrewStrongNumber is not null).Should().Be(3_680);
        _corpus.Names.Count(n => n.GreekStrongNumber is not null).Should().Be(1_160);
        strongNumbers.Should().Be(5_850);
        _corpus.PlaceNames.Should().HaveCount(141);
        _corpus.NamesOf("jesus").Should().HaveCount(73);
        _corpus.Relationships.Should().HaveCount(5_446);
        _corpus.Duplicates.Should().Be(2);
        _corpus.Unpaired.Should().Be(7);
        _corpus.References.Should().HaveCount(28_223);
        _corpus.Disputed.Should().Be(1_417);
        _corpus.References.Count(r => r.EntityId == _corpus.Jesus.Id).Should().Be(1_477);
    }

    [Fact]
    public void EveryStrongNumberOnANameIsOneTheLexiconHolds()
    {
        var numbers = _corpus.Names
            .SelectMany(n => Split(n.HebrewStrongNumber).Concat(Split(n.GreekStrongNumber)))
            .ToList();

        numbers.Should().HaveCountGreaterThan(5_000);
        numbers.Where(number => !_corpus.Lexicon.Contains(number)).Should().BeEmpty();
    }

    /// <summary>
    /// The Greek numbers were the whole New Testament half of this table and three of them
    /// survived, because the dataset's word for "no Hebrew number" answered as a Hebrew number.
    /// </summary>
    [Fact]
    public void TheGreekStrongNumbersAreLoaded()
    {
        _corpus.Names.Count(n => n.GreekStrongNumber is not null).Should().BeGreaterThan(1_000);
    }

    /// <summary>
    /// <em>Talitha cumi</em> is what Jesus says to Jairus's daughter, and MRK 5:41 translates it:
    /// <em>Damsel, I say unto thee, arise</em>. The dataset holds the word as a girl called Talitha;
    /// no record is written for her, and nothing that hung off it is left pointing anywhere.
    /// </summary>
    [Fact]
    public void AWordSaidToAGirlIsNotHerName()
    {
        _corpus.Entities.Values.Should().NotContain(entity => entity.Name == "Talitha");
        _corpus.Entities.Values.Should().Contain(entity => entity.Slug == "jairus");

        var ids = _corpus.Entities.Values.Select(entity => entity.Id).ToHashSet();
        _corpus.Relationships.Should().OnlyContain(r => ids.Contains(r.FromEntityId) && ids.Contains(r.ToEntityId));
        _corpus.Names.Should().OnlyContain(name => ids.Contains(name.EntityId));
    }

    /// <summary>
    /// Tabitha of Joppa was reachable only through H6645 — Zibiah, the mother of Jehoash — because
    /// the dataset's Hebrew column for that label holds a back-translation of the meaning and the
    /// Greek column, which holds her actual name, was thrown away.
    /// </summary>
    [Fact]
    public void TabithaReachesHerGreekName()
    {
        var names = _corpus.NamesOf("Tabitha_1");

        names.Single(n => n.Label == "Tabitha").GreekStrongNumber.Should().Be("G5000");
        names.Single(n => n.Label == "Tabitha").HebrewStrongNumber.Should().BeNull();
        names.Single(n => n.Label == "Dorcas").GreekStrongNumber.Should().Be("G1393");
    }

    [Fact]
    public void ATitleKeepsTheStrongNumberOfEveryWordOfIt()
    {
        _corpus.Names
            .Where(n => n.Label == "King of Judah" && n.HebrewStrongNumber is not null)
            .Should().NotBeEmpty()
            .And.OnlyContain(n => n.HebrewStrongNumber == "H4428,H3063");
    }

    [Fact]
    public void APlaceIsNumberedByItsNameAndNotByTheWordsOfItsPhrase()
    {
        var garden = new EntityName
        {
            EntityId = 1, Label = "Garden of the LORD", Kind = BibleDataLoader.LabelKind,
            HebrewStrongNumber = "H1588,H3068", GreekStrongNumber = "G3588,G2316,G3857",
        };
        var siddim = new EntityName
        {
            EntityId = 2, Label = "Valley of Siddim", Kind = BibleDataLoader.LabelKind,
            HebrewStrongNumber = "H6009,H7708", GreekStrongNumber = "G5327,G3588",
        };
        var king = new EntityName
        {
            EntityId = 3, Label = "King of Judah", Kind = BibleDataLoader.LabelKind, HebrewStrongNumber = "H4428,H3063",
        };
        HashSet<string> placeNames = ["H7708", "H3063", "H3068", "G2446"];

        var taken = BibleDataLoader.NumberPlacesOnlyByTheirNames([garden, siddim, king], new HashSet<int> { 1, 2 }, placeNames);

        taken.Should().Be(8);
        garden.HebrewStrongNumber.Should().BeNull("the divine name is nobody's place");
        garden.GreekStrongNumber.Should().BeNull();
        siddim.HebrewStrongNumber.Should().Be("H7708");
        siddim.GreekStrongNumber.Should().BeNull();
        king.HebrewStrongNumber.Should().Be("H4428,H3063", "a title keeps the number of every word of it");

        BibleDataLoader.NumberPlacesOnlyByTheirNames([siddim], new HashSet<int> { 2 }, new HashSet<string>())
            .Should().Be(0, "nothing is taken off before the lexicon is loaded");
    }

    [Fact]
    public void APersonIsNumberedInGreekByHisNameAndNotByWhatItMeans()
    {
        var bendeker = new EntityName { EntityId = 1, Label = "Ben-deker", Kind = BibleDataLoader.LabelKind, GreekStrongNumber = "G5207" };
        var ichabod = new EntityName { EntityId = 2, Label = "Ichabod", Kind = BibleDataLoader.LabelKind, GreekStrongNumber = "G3759" };
        var legion = new EntityName { EntityId = 3, Label = "Legion", Kind = BibleDataLoader.LabelKind, GreekStrongNumber = "G3003" };
        var paul = new EntityName { EntityId = 4, Label = "Paul", Kind = BibleDataLoader.LabelKind, GreekStrongNumber = "G3972" };
        var lord = new EntityName { EntityId = 5, Label = "LORD", Kind = BibleDataLoader.LabelKind, GreekStrongNumber = "G2316" };
        var sea = new EntityName { EntityId = 6, Label = "Sea", Kind = BibleDataLoader.LabelKind, GreekStrongNumber = "G2281" };
        HashSet<string> greekNames = ["G3972"];
        var definitions = new Dictionary<string, string?>
        {
            ["G5207"] = "a \"son\" (sometimes of animals), used very widely",
            ["G3759"] = "\"woe\"",
            ["G3003"] = "a \"legion\", i.e. Roman regiment (figuratively)",
            ["G2316"] = "a deity",
            ["G2281"] = "the sea",
        };

        var taken = BibleDataLoader.NumberPeopleOnlyByTheirNames(
            [bendeker, ichabod, legion, paul, lord, sea], new HashSet<int> { 1, 2, 3, 4, 5 }, greekNames, definitions);

        taken.Should().Be(2);
        bendeker.GreekStrongNumber.Should().BeNull("υἱός is the word son, not Ben-deker");
        ichabod.GreekStrongNumber.Should().BeNull();
        legion.GreekStrongNumber.Should().Be("G3003", "the entry prints the name");
        paul.GreekStrongNumber.Should().Be("G3972");
        lord.GreekStrongNumber.Should().Be("G2316", "the words for God are God's");
        sea.GreekStrongNumber.Should().Be("G2281", "a place keeps its own rule");

        BibleDataLoader.NumberPeopleOnlyByTheirNames([sea], new HashSet<int> { 6 }, new HashSet<string>(), definitions)
            .Should().Be(0, "nothing is taken off before the lexicon is loaded");
    }

    [Theory]
    [InlineData("H1328A", 'H', "H1328")]
    [InlineData("H0911", 'H', "H911")]
    [InlineData("HH1583", 'H', "H1583")]
    [InlineData("H3068 H430", 'H', "H3068,H430")]
    [InlineData("H4807 & H4810 ", 'H', "H4807,H4810")]
    [InlineData("H1350, 3478", 'H', "H1350,H3478")]
    [InlineData("G935, none", 'G', "G935")]
    [InlineData("G2316, G3708, G2316", 'G', "G2316,G3708")]
    [InlineData("none", 'H', null)]
    [InlineData("[NONE], H5333", 'H', "H5333")]
    [InlineData("many", 'G', null)]
    [InlineData("", 'H', null)]
    [InlineData("G2492a", 'G', "G2455")]
    [InlineData("G935, G2492a", 'G', "G935,G2455")]
    [InlineData("G3102a", 'G', "G3158")]
    [InlineData("G2490a", 'G', "G2490")]
    [InlineData("G95a", 'G', null)]
    [InlineData("G5101a, G2459", 'G', "G2459")]
    [InlineData("G1234z", 'G', null)]
    public void AStrongNumberIsNormalisedToWhatTheLexiconIsKeyedBy(string written, char language, string? expected) =>
        BibleDataLoader.Strong(written, language).Should().Be(expected);

    /// <summary>
    /// The dataset's lettered Greek numbers are its own extension of Strong's, and the number before
    /// the letter is sometimes another word: G2492a is Judas, and G2492 is Job.
    /// </summary>
    [Fact]
    public void JudasIsNotFiledUnderJobsNumber()
    {
        _corpus.NamesOf("Judas_1").Where(n => n.Label == "Judas").Select(n => n.GreekStrongNumber)
            .Should().Equal("G2455");
        _corpus.Names.Where(n => n.GreekStrongNumber == "G2492")
            .Select(n => _corpus.Entities.Values.Single(e => e.Id == n.EntityId).SourceId)
            .Should().Equal("person:Job_1");
        _corpus.NamesOf("Admin_1").Should().OnlyContain(n => n.GreekStrongNumber == null,
            "Strong's has no number for Admin, and G95 is the adverb unjustly");
    }

    /// <summary>
    /// Σαούλ is the king's name and Paul's: the risen Christ calls him by it at Acts 9:4.
    /// </summary>
    [Fact]
    public void PaulAnswersToTheHebrewFormOfHisName()
    {
        _corpus.NamesOf("Saul_2").Select(n => n.GreekStrongNumber)
            .Should().Contain(["G4569", "G3972", BibleDataLoader.CalledSaoul]);
        _corpus.NamesOf("Saul_1").Select(n => n.GreekStrongNumber).Should().Contain(BibleDataLoader.CalledSaoul);
    }

    /// <summary>
    /// The dataset writes its own word for an empty cell, and it was reaching the page: 163 names
    /// offered <c>[none]</c> as their Greek spelling and 209 stored <c>NONE</c> where a Strong
    /// number goes.
    /// </summary>
    [Fact]
    public void TheDatasetsWordForNothingNeverBecomesAValue()
    {
        var written = _corpus.Names
            .SelectMany(n => new[]
            {
                n.Hebrew, n.HebrewTransliterated, n.Greek, n.GreekTransliterated, n.Meaning,
                n.HebrewStrongNumber, n.GreekStrongNumber, n.Kind,
            })
            .Where(value => value is not null);

        written.Should().NotContain(value => NullToken().IsMatch(value!));
    }

    [Fact]
    public void PlacesAreNamedInHebrewAndGreekLikePeople()
    {
        var places = _corpus.Entities.Values.Where(e => e.Kind == EntityKind.Place).Select(e => e.Id).ToHashSet();
        var named = _corpus.Names.Where(n => places.Contains(n.EntityId)).ToList();

        named.Should().HaveCountGreaterThan(100);
        named.Should().Contain(n => n.Hebrew != null && n.Greek != null && n.Meaning != null);
        _corpus.NamesOf("Ararat_1").Should().NotBeEmpty();
    }

    /// <summary>
    /// One verse for each way a list of the fourteen ambiguous words got it wrong. The first three
    /// are the God of Israel, named by an Old Testament title in a New Testament verse; the next
    /// three are unambiguous and must not be flagged as doubtful; the last two are doubtful and
    /// must be.
    /// </summary>
    [Theory]
    [InlineData("the G-d of Abraham, the G-d of Isaac, and the G-d of Jacob", false, false)]
    [InlineData("Lord G-d of Israel", false, false)]
    [InlineData("Lord of Sabaoth", false, false)]
    [InlineData("Father", false, false)]
    [InlineData("the Most High", false, false)]
    [InlineData("Creator", false, false)]
    [InlineData("G-d our Savior", false, true)]
    [InlineData("G-d", false, true)]
    [InlineData("Son of Man", true, false)]
    [InlineData("King of the Jews", true, false)]
    public void ANewTestamentLabelOnTheDivineNameIsReadOneWay(string label, bool jesus, bool disputed)
    {
        var reading = BibleDataLoader.Reading(label);

        (reading == BibleDataLoader.DivineReading.Jesus).Should().Be(jesus);
        (reading == BibleDataLoader.DivineReading.Contested).Should().Be(disputed);
    }

    /// <summary>
    /// A label nobody has decided is flagged rather than assigned, which is what makes the list
    /// safe to extend: a BibleData that adds a title next year shows up as a question and not as a
    /// claim about who the verse means.
    /// </summary>
    [Fact]
    public void ALabelTheListDoesNotNameIsDisputed() =>
        BibleDataLoader.Reading("Sword of the Everlasting Hills")
            .Should().Be(BibleDataLoader.DivineReading.Contested);

    [Fact]
    public void MatthewsGodOfAbrahamIsNotFiledUnderJesus()
    {
        var atTheBush = _corpus.References
            .Single(r => r.CanonicalBook == 40 && r.CanonicalChapter == 22 && r.CanonicalVerse == 32
                         && r.Label!.StartsWith("the G-d of Abraham"));

        atTheBush.EntityId.Should().NotBe(_corpus.Jesus.Id);
        atTheBush.Disputed.Should().BeFalse();
    }

    /// <summary>
    /// Genesis 45 has 28 verses. Four relationship rows dated Ephraim's parentage to its
    /// fifty-second, and nothing noticed because the loader checked that a citation parsed rather
    /// than that it named a verse.
    /// </summary>
    [Fact]
    public void ACitationToAVerseThatDoesNotExistIsDropped()
    {
        var frame = BibleDataLoader.ReferenceTable.Read(_corpus.Folder);

        frame.Resolve("GEN 45:52").Should().BeNull();
        frame.Resolve("GEN 41:52").Should().Be((1, 41, 52));
        frame.Dangling.Should().ContainKey("GEN 45:52");
    }

    [Fact]
    public void NoRelationshipCitesAVerseNobodyCanRead() =>
        _corpus.Relationships
            .Should().NotContain(r => r.CanonicalBook == 1 && r.CanonicalChapter == 45 && r.CanonicalVerse == 52);

    [Fact]
    public void ARelationshipTheSourceStatesTwiceIsListedOnce()
    {
        _corpus.Duplicates.Should().BeGreaterThan(0);

        _corpus.Relationships
            .GroupBy(r => (r.FromEntityId, r.Type, r.ToEntityId, r.CanonicalBook, r.CanonicalChapter,
                r.CanonicalVerse, r.Notes))
            .Should().OnlyContain(group => group.Count() == 1);
    }

    /// <summary>
    /// The rename pass ran over events only, so <em>"father of Shelemiah_6 (JER 36:26)"</em>
    /// reached the entity list, the search results and every relationship row on another person's
    /// page.
    /// </summary>
    [Fact]
    public void NoRowIdentifierIsLeftInProseTheReaderSees()
    {
        var known = _corpus.Entities.Keys
            .Select(key => key[(key.IndexOf(':') + 1)..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // What survives is an identifier the dataset itself does not define — a typo upstream —
        // and leaving one visible is better than replacing it with a name nobody said.
        Leaked().Should().OnlyContain(identifier => !known.Contains(identifier));
    }

    /// <summary>
    /// Three identifiers the dataset cites and does not define: two its own rows answer, and one no
    /// row does, which is printed as the name it spells.
    /// </summary>
    [Fact]
    public void AnIdentifierTheDatasetDoesNotDefineReadsAsTheRowItsOwnRowsName()
    {
        _corpus.Entities.Values.Should().Contain(e => e.Distinguisher == "son of Heman (1CH 25:4)");
        _corpus.Entities.Values.Select(e => e.Distinguisher)
            .Should().Contain(d => d != null && d.Contains("daughter of Uriel of Gibeah (2CH 13:2)"));
        _corpus.Entities.Values.Select(e => e.Distinguisher)
            .Should().Contain(d => d != null && d.Contains("son of Seraiah"));
        Leaked().Should().NotContain(["Heman_3", "Uriel_2", "Seriah_2"]);
    }

    [Fact]
    public void ADistinguisherReadsAsAName() =>
        _corpus.Entities["person:Abdeel_1"].Distinguisher.Should().Be("father of Shelemiah (JER 36:26)");

    /// <summary>
    /// The dataset names both of these YHVH and tells them apart by a sample of their titles —
    /// "Holy, Holy, Holy (ISA 6:3) and too many others to fit here" — which is true of the first and
    /// says nothing about which of the two it is. A search for the name returned two rows a reader
    /// had nothing to choose between, so the second is named rather than distinguished. Both keep
    /// the dataset's own words in their notes: a name it chose is a thing it said.
    /// </summary>
    [Fact]
    public void TheTwoEntitiesNamedYhvhAreToldApart()
    {
        var god = _corpus.Entities["person:YHVH_1"];
        var father = _corpus.Entities["person:YHVH_2"];

        god.Name.Should().Be("YHVH");
        father.Name.Should().Be("God the Father");
        god.Distinguisher.Should().NotBeNullOrWhiteSpace().And.NotBe(father.Distinguisher);
        father.Distinguisher.Should().NotBeNullOrWhiteSpace();
        god.Distinguisher.Should().NotContain("too many others");
        god.Notes.Should().NotBeNull().And.Contain("too many others");
        father.Notes.Should().NotBeNull().And.Contain("YHVH");
    }

    /// <summary>
    /// What the distinguisher on the second one rests on: the dataset cites it in the New Testament
    /// and nowhere else, so "whom the New Testament names" is a measure rather than a reading.
    /// </summary>
    [Fact]
    public void TheFatherIsNamedOnlyInTheNewTestament()
    {
        var father = _corpus.Entities["person:YHVH_2"];
        var references = _corpus.References.Where(r => r.EntityId == father.Id).ToList();

        references.Should().HaveCount(325);
        references.Should().OnlyContain(r => r.CanonicalBook > BookReferences.OldTestamentBookCount);
    }

    /// <summary>
    /// The dataset gives the Father his own label rows, anchored where the New Testament first
    /// says each — <em>Father</em> at Matthew 6:4. The divine name's own <em>Father</em> is a
    /// second row anchored at Deuteronomy 32:6, and the five New Testament verses that use it use
    /// that one. Moving them would overrule a distinction the source draws itself.
    /// </summary>
    [Fact]
    public void TheFatherKeepsHisOwnLabelsAndTakesNoneFromTheDivineName()
    {
        var father = _corpus.Entities["person:YHVH_2"];

        _corpus.NamesOf("YHVH_2").Select(n => n.Label)
            .Should().HaveCount(10).And.Contain(["Father", "Abba Father", "Righteous Father"]);

        _corpus.References
            .Where(r => r.EntityId == father.Id && r.Label == "Father")
            .Should().HaveCount(155);
    }

    /// <summary>
    /// The names are the third place the one entity had to come apart, and the last to be done:
    /// every title of the divine name stayed on it, so the encyclopedia listed Jesus, Christ and
    /// King of the Jews among the names of the God of Israel and gave Jesus of Nazareth none.
    /// </summary>
    [Fact]
    public void TheTitlesOfTheSonAreNamesOfJesus()
    {
        var his = _corpus.NamesOf("jesus").Select(n => n.Label).ToList();

        his.Should().Contain(["Jesus", "Christ", "Son of Man", "King of the Jews", "Rabboni", "Lamb"]);
        _corpus.NamesOf("YHVH_1").Select(n => n.Label)
            .Should().NotContain(["Jesus", "Christ", "Son of Man", "King of the Jews"]);
    }

    /// <summary>
    /// A title the dataset uses in both testaments is written on both entities, because its own
    /// verse rows call both of them that: Genesis 49:24 and Hebrews 13:20 are both "Shepherd".
    /// </summary>
    [Fact]
    public void ATitleUsedInBothTestamentsIsANameOfBoth()
    {
        foreach (var shared in new[] { "I AM", "King of Israel", "My Servant" })
        {
            _corpus.NamesOf("jesus").Select(n => n.Label).Should().Contain(shared);
            _corpus.NamesOf("YHVH_1").Select(n => n.Label).Should().Contain(shared);
        }
    }

    /// <summary>
    /// A name follows its namings, so a title whose New Testament uses are contested stays with
    /// the divine name exactly as those uses do. Nothing new is decided here.
    /// </summary>
    [Fact]
    public void AContestedTitleIsNotGivenToJesus() =>
        _corpus.NamesOf("jesus").Select(n => n.Label)
            .Should().NotContain(["Lord", "G-d", "Savior", "King of kings", "The Alpha and the Omega"]);

    /// <summary>
    /// The name is its letters. יהוה pointed with the vowels of Elohim is H3069, which the label
    /// file never numbers, so without this row 583 words of the divine name named nobody — and it
    /// is a name of the God of Israel only, never of the Father or of Jesus.
    /// </summary>
    [Fact]
    public void TheDivineNamePointedAsElohimIsANameOfTheGodOfIsrael()
    {
        _corpus.NamesOf("YHVH_1")
            .Should().ContainSingle(n => n.HebrewStrongNumber == BibleDataLoader.PointedAsElohim)
            .Which.Kind.Should().Be("proper name");

        _corpus.Names.Where(n => n.HebrewStrongNumber == BibleDataLoader.PointedAsElohim)
            .Should().ContainSingle();
    }

    [Fact]
    public void TheOldTestamentTitlesStayWithTheGodOfIsrael() =>
        _corpus.NamesOf("YHVH_1").Select(n => n.Label)
            .Should().Contain(["Father", "the Most High", "Almighty", "Lord of Sabaoth", "Creator"]);

    private IList<string> Leaked()
    {
        var prose = _corpus.Entities.Values.Select(e => e.Distinguisher)
            .Concat(_corpus.Entities.Values.Select(e => e.Notes))
            .Concat(_corpus.Relationships.Select(r => r.Notes))
            .Where(text => text is not null);

        return [.. prose.SelectMany(text => Identifier().Matches(text!).Select(match => match.Value))];
    }

    private static IEnumerable<string> Split(string? numbers) =>
        numbers is { Length: > 0 } ? numbers.Split(',') : [];

    [GeneratedRegex(@"^\[?none\]?$", RegexOptions.IgnoreCase)]
    private static partial Regex NullToken();

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9-]*(?:_[A-Za-z0-9-]+)+")]
    private static partial Regex Identifier();

    /// <summary>
    /// The dataset holds the God of Israel and Jesus as one entity, and the separation was applied
    /// to the references and not to these — so the twelve apostles were apostles of the God of
    /// Israel, Mary was his bearer, and the encyclopedia said "YHVH brother of James".
    /// </summary>
    [Fact]
    public void TheApostlesAreApostlesOfJesus()
    {
        var apostles = _corpus.Relationships
            .Where(r => r.ToEntityId == _corpus.Jesus.Id && r.Type == "apostle")
            .ToList();

        apostles.Should().HaveCount(12);
        apostles.Should().OnlyContain(r => r.CanonicalBook >= 40);
    }

    [Fact]
    public void MaryBearsJesusRatherThanTheGodOfIsrael()
    {
        var divine = _corpus.Entities.Values.Single(e => e.SourceId == "person:YHVH_1");

        _corpus.Relationships.Where(r => r.Type == "bearer")
            .Should().OnlyContain(r => r.FromEntityId != divine.Id && r.ToEntityId != divine.Id);
    }

    /// <summary>
    /// Both directions of one tie are separate rows in this dataset, so both have to move or the
    /// encyclopedia says one thing on his page and the other on hers.
    /// </summary>
    [Fact]
    public void BothDirectionsOfATieMoveTogether()
    {
        var jesus = _corpus.Jesus.Id;
        var brothers = _corpus.Relationships.Where(r => r.Type == "brother"
            && (r.FromEntityId == jesus || r.ToEntityId == jesus)).ToList();

        brothers.Where(r => r.FromEntityId == jesus).Should().HaveCount(4);
        brothers.Where(r => r.ToEntityId == jesus).Should().HaveCount(4);
    }

    /// <summary>
    /// A relation to the divine name outside the New Testament is a relation to the God of Israel,
    /// and stays. Abraham is not a servant of Jesus of Nazareth.
    /// </summary>
    [Fact]
    public void TheOldTestamentRelationsStayWithTheGodOfIsrael()
    {
        var divine = _corpus.Entities.Values.Single(e => e.SourceId == "person:YHVH_1");
        var servants = _corpus.Relationships
            .Where(r => r.ToEntityId == divine.Id && r.Type == "servant").ToList();

        servants.Should().NotBeEmpty();
        servants.Should().OnlyContain(r => r.CanonicalBook < 40);
    }
}

/// <summary>
/// One load of BibleData, held for every test in the file. It reads eleven files and fifty
/// thousand rows, which is a second once and a minute if each test does it.
/// </summary>
public sealed class BibleDataCorpus
{
    public BibleDataCorpus()
    {
        Folder = Path.GetDirectoryName(TestResources.Path("BibleData2026", "BibleData-Person.csv"))!;

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        Jesus = BibleDataLoader.Divide(Entities, slugs);
        BibleDataLoader.People(Folder, Entities, slugs);
        BibleDataLoader.Places(Folder, Entities, slugs);
        BibleDataLoader.Distinguish(Entities);

        // The database hands these out; here they are only needed to tell one entity from another.
        var id = 1;
        foreach (var entity in Entities.Values)
        {
            entity.Id = id++;
        }

        var frame = BibleDataLoader.ReferenceTable.Read(Folder);
        (Relationships, Duplicates, Unpaired) = BibleDataLoader.Relationships(Folder, Entities, frame, Jesus);
        (References, Disputed, var divided) = BibleDataLoader.References(Folder, Entities, frame, Jesus);
        Names = BibleDataLoader.Names(Folder, Entities, divided);
        var events = BibleDataLoader.Events(Folder, Entities, frame);
        BibleDataLoader.Name(Entities, events, Relationships);

        var parser = new StrongXmlParser();
        Lexicon =
        [
            .. parser.ParseHebrew(File.ReadAllText(TestResources.Path("Strong", "StrongHebrew.xml")))
                .Concat(parser.ParseGreek(File.ReadAllText(TestResources.Path("Strong", "StrongGreek.xml"))))
                .Select(entry => entry.StrongNumber),
        ];
    }

    public string Folder { get; }

    public Dictionary<string, Entity> Entities { get; } = new(StringComparer.Ordinal);

    public Entity Jesus { get; }

    public List<EntityName> Names { get; }

    public List<EntityRelationship> Relationships { get; }

    public int Duplicates { get; }

    public int Unpaired { get; }

    public List<EntityVerse> References { get; }

    public int Disputed { get; }

    public HashSet<string> Lexicon { get; }

    public IList<EntityName> PlaceNames
    {
        get
        {
            var places = Entities.Values.Where(e => e.Kind == EntityKind.Place).Select(e => e.Id).ToHashSet();
            return [.. Names.Where(n => places.Contains(n.EntityId))];
        }
    }

    public IList<EntityName> NamesOf(string sourceId)
    {
        var entity = Entities.Values.Single(e => e.SourceId.EndsWith($":{sourceId}", StringComparison.Ordinal));
        return [.. Names.Where(n => n.EntityId == entity.Id)];
    }
}
