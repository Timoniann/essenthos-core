using System.Text.Json;
using Essenthos.Core.Loading;
using Essenthos.Core.MorphGnt;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// MorphGNT's parsing of the SBLGNT, and the join that puts it on the Nestle 1904 words this corpus
/// holds.
///
/// The join is the part worth checking and the part nothing else would catch. It pairs two editions
/// that differ in more than 540 variation units and share no identifier — no Strong number, no word
/// id, nothing but the letters on the page — so a mistake in it does not fail, it produces a corpus
/// where some words carry the parse of the word next to them. Every number below was measured over
/// all 137,779 Nestle words and all 137,554 MorphGNT words, and they are here so that a change to
/// the fold or to the gap rule says what it cost.
/// </summary>
public class MorphGntTests(ITestOutputHelper output)
{
    private const int Books = 27;

    /// <summary>Every word of the SBLGNT as MorphGNT parses it, version 6.12.</summary>
    private const int ParsedWords = 137_554;

    private const int NestleWords = 137_779;

    /// <summary>Words the two editions print identically, once accents and case are set aside.</summary>
    private const int PrintedAlike = 135_960;

    /// <summary>
    /// Words they spell differently and that are the same word. Almost all proper names: Ἰωάνης
    /// against Ἰωάννης, Πειλᾶτος against Πιλᾶτος, Δαυείδ against Δαυίδ.
    /// </summary>
    private const int SpelledOtherwise = 444;

    /// <summary>
    /// Nestle words the SBLGNT does not reach. 298 of them are in the six passages one edition
    /// prints and the other does not; the rest are ordinary textual variation, scattered.
    /// </summary>
    private const int Unreached = 1_375;

    private const int Unused = 1_150;

    private static bool Fetched => Directory.Exists(Path.Combine(TestResources.MorphGntFolder, "parsing"));

    private static IReadOnlyList<MorphGntWord> Parsing() =>
        MorphGntReader.ReadAll(TestResources.MorphGntFolder);

    /// <summary>
    /// Nestle 1904 read from its own file rather than from a database, so that this measures the
    /// two sources against each other and not the state of somebody's load.
    /// </summary>
    private static IReadOnlyList<JoinWord> Nestle() =>
    [
        .. NestleTextSource.Read(TestResources.Nestle1904).Books
            .SelectMany(book => book.Chapters
                .SelectMany(chapter => chapter.Verses
                    .SelectMany(verse => verse.Words.Select(word => new JoinWord(
                        book.CanonicalOrdinal,
                        chapter.Number,
                        verse.Number,
                        word.Surface,
                        Features(word.Morphology))))))
    ];

    private static IReadOnlyDictionary<string, string> Features(string? morphology)
    {
        if (morphology is null)
        {
            return new Dictionary<string, string>(0);
        }

        using var document = JsonDocument.Parse(morphology);
        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);
    }

    [Fact]
    public void ReadsEveryWordOfTheNewTestament()
    {
        if (!Fetched)
        {
            output.WriteLine("MorphGNT is not on disk; run scripts/fetch-morphgnt.ps1");
            return;
        }

        var words = Parsing();

        words.Should().HaveCount(ParsedWords);
        words.Select(word => word.Book).Distinct().Should().HaveCount(Books);
        words[0].Book.Should().Be(40, "the address counts the New Testament from 1 and Matthew is the 40th book");
        words[^1].Book.Should().Be(66);
        words.Should().OnlyContain(word => word.Parse.Length == MorphGntParsing.Length);
    }

    /// <summary>
    /// The premise this dataset was fetched under, checked rather than believed: MorphGNT was
    /// expected to state a proper-noun class where Nestle does not, and it does not either. Thirteen
    /// part-of-speech codes, one noun among them.
    /// </summary>
    [Fact]
    public void StatesThirteenPartsOfSpeechAndOnlyOneKindOfNoun()
    {
        if (!Fetched)
        {
            output.WriteLine("MorphGNT is not on disk; run scripts/fetch-morphgnt.ps1");
            return;
        }

        var kinds = Parsing().Select(word => word.PartOfSpeech).Distinct().Order(StringComparer.Ordinal);

        kinds.Should().Equal(
            "A-", "C-", "D-", "I-", "N-", "P-", "RA", "RD", "RI", "RP", "RR", "V-", "X-");
    }

    /// <summary>
    /// The README documents four cases and the files write five. What makes the undocumented letter
    /// a gap in the documentation rather than a defect in the data is that Nestle agrees about the
    /// words it stands on: of the 597 words Nestle calls vocative, 592 are reached by this join and
    /// MorphGNT calls 589 of them vocative too, disagreeing on three. A reader that dropped the
    /// letter it had not been told about would lose the whole vocative from the second morphology
    /// and would read those 668 words as having no case at all.
    /// </summary>
    [Fact]
    public void WritesAVocativeTheReadmeDoesNotDocument()
    {
        if (!Fetched)
        {
            output.WriteLine("MorphGNT is not on disk; run scripts/fetch-morphgnt.ps1");
            return;
        }

        var nestle = Nestle();
        var parsing = Parsing();
        var join = MorphGntJoin.Of(nestle, parsing);

        parsing.Count(word => MorphGntParsing.Case(word.Parse) == "vocative").Should().Be(668);

        var agreed = join.Rows.Count(row =>
            nestle[row.Witness].Morphology.GetValueOrDefault("case") == "vocative"
            && MorphGntParsing.Case(parsing[row.Parsing].Parse) == "vocative");

        agreed.Should().Be(589);
    }

    [Theory]
    [InlineData("N-", "----NSF-", "nominative", "singular", "feminine")]
    [InlineData("V-", "3AAI-S--", null, "singular", null)]
    [InlineData("RA", "----ASM-", "accusative", "singular", "masculine")]
    [InlineData("N-", "----VSM-", "vocative", "singular", "masculine")]
    public void ReadsTheParsingCodeByPosition(
        string partOfSpeech,
        string parse,
        string? expectedCase,
        string? expectedNumber,
        string? expectedGender)
    {
        partOfSpeech.Should().NotBeEmpty();
        MorphGntParsing.Case(parse).Should().Be(expectedCase);
        MorphGntParsing.Number(parse).Should().Be(expectedNumber);
        MorphGntParsing.Gender(parse).Should().Be(expectedGender);
    }

    [Fact]
    public void ReadsPersonTenseVoiceAndMood()
    {
        MorphGntParsing.Person("3AAI-S--").Should().Be("third");
        MorphGntParsing.Tense("3AAI-S--").Should().Be("aorist");
        MorphGntParsing.Voice("3AAI-S--").Should().Be("active");
        MorphGntParsing.Mood("3AAI-S--").Should().Be("indicative");
        MorphGntParsing.Degree("----NSMC").Should().Be("comparative");
        MorphGntParsing.Degree("----NSM-").Should().BeNull();
    }

    [Fact]
    public void RefusesAParsingCodeOfTheWrongLength()
    {
        var reading = () => MorphGntParsing.Case("----NSF");

        reading.Should().Throw<ArgumentException>().WithMessage("*8 characters*");
    }

    /// <summary>
    /// The fold joins the two editions' spellings of a name and must not join two different words.
    /// The second half of this is what stops the fold being itacism: by sound, ἡμᾶς and ὑμᾶς are
    /// one string, and joining them would put a first-person parse on the word for *you*.
    /// </summary>
    [Theory]
    [InlineData("ιωανησ", "ιωαννησ")]
    [InlineData("πειλατοσ", "πιλατοσ")]
    [InlineData("δαυειδ", "δαυιδ")]
    [InlineData("ηλειασ", "ηλιασ")]
    [InlineData("σαμαριασ", "σαμαρειασ")]
    [InlineData("μυρρα", "μυρα")]
    [InlineData("ερυσθην", "ερρυσθην")]
    public void JoinsTheSpellingsOfOneWord(string nestle, string sblgnt)
    {
        MorphGntSpelling.Folded(nestle).Should().Be(MorphGntSpelling.Folded(sblgnt));
    }

    [Theory]
    [InlineData("ημασ", "υμασ")]
    [InlineData("ημων", "υμων")]
    [InlineData("ημετερον", "υμετερον")]
    [InlineData("εχωμεν", "εχομεν")]
    [InlineData("γινωσκομεν", "γινωσκωμεν")]
    [InlineData("αδικησει", "αδικηση")]
    [InlineData("αβρααμ", "αβραμ")]
    public void LeavesTwoDifferentWordsApart(string nestle, string sblgnt)
    {
        MorphGntSpelling.Folded(nestle).Should().NotBe(MorphGntSpelling.Folded(sblgnt));
    }

    /// <summary>
    /// The whole join, over both editions in full. The four counts add up to the two word totals,
    /// which is the property that says nothing was paired twice and nothing was dropped on the
    /// floor — a join that silently lost a verse would still produce plausible-looking coverage.
    /// </summary>
    [Fact]
    public void ReachesNinetyNinePercentOfNestleAndSaysWhatItMisses()
    {
        if (!Fetched)
        {
            output.WriteLine("MorphGNT is not on disk; run scripts/fetch-morphgnt.ps1");
            return;
        }

        var nestle = Nestle();
        var join = MorphGntJoin.Of(nestle, Parsing());

        nestle.Should().HaveCount(NestleWords);
        join.Rows.Count(row => row.Match == MorphGntMatch.Printed).Should().Be(PrintedAlike);
        join.Rows.Count(row => row.Match == MorphGntMatch.Spelling).Should().Be(SpelledOtherwise);
        join.Unmatched.Should().HaveCount(Unreached);
        join.Unused.Should().HaveCount(Unused);

        (join.Rows.Count + join.Unmatched.Count).Should().Be(NestleWords);
        (join.Rows.Count + join.Unused.Count).Should().Be(ParsedWords);
        join.Rows.Select(row => row.Witness).Should().OnlyHaveUniqueItems();
        join.Rows.Select(row => row.Parsing).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// What the second morphology adds and where it contradicts the first, counted rather than
    /// resolved. Nothing here is a defect to fix — the disagreements are two editors reading the
    /// same syntax differently.
    /// </summary>
    [Fact]
    public void SaysWhatNestleCannotAndDisagreesWithItRarely()
    {
        if (!Fetched)
        {
            output.WriteLine("MorphGNT is not on disk; run scripts/fetch-morphgnt.ps1");
            return;
        }

        var nestle = Nestle();
        var parsing = Parsing();
        var join = MorphGntJoin.Of(nestle, parsing);

        Agreement(nestle, parsing, join, "case", MorphGntParsing.Case)
            .Should().Be((Agree: 77_653, Disagree: 92, Added: 1_538));
        Agreement(nestle, parsing, join, "number", MorphGntParsing.Number)
            .Should().Be((Agree: 88_599, Disagree: 7, Added: 9_537));
        Agreement(nestle, parsing, join, "gender", MorphGntParsing.Gender)
            .Should().Be((Agree: 64_167, Disagree: 70, Added: 9_534));
        Agreement(nestle, parsing, join, "tense", MorphGntParsing.Tense)
            .Should().Be((Agree: 27_617, Disagree: 29, Added: 3));
        Agreement(nestle, parsing, join, "mood", MorphGntParsing.Mood)
            .Should().Be((Agree: 27_632, Disagree: 14, Added: 3));
        Agreement(nestle, parsing, join, "person", MorphGntParsing.Person)
            .Should().Be((Agree: 18_857, Disagree: 0, Added: 3));

        // Nestle has no degree at all, so every one of these is a feature the corpus could not say.
        Agreement(nestle, parsing, join, "degree", MorphGntParsing.Degree)
            .Should().Be((Agree: 0, Disagree: 0, Added: 303));

        // The voice looks like the worst disagreement in the corpus and is the best thing here:
        // 4,602 of the 4,638 are a form Nestle can only call middle-or-passive and MorphGNT calls.
        var voice = Agreement(nestle, parsing, join, "voice", MorphGntParsing.Voice);
        voice.Disagree.Should().Be(4_638);
        Undecided(nestle, parsing, join).Should().Be(4_602);
    }

    /// <summary>
    /// An indeclinable numeral, <c>A-NUI</c>, has no case group, and the reader must not find one in
    /// the N of NUI. MorphGNT parses 342 of them into a case from the syntax; this corpus states none.
    /// </summary>
    [Fact]
    public void GivesTheIndeclinableNumeralsNoCase()
    {
        if (!Fetched)
        {
            output.WriteLine("MorphGNT is not on disk; run scripts/fetch-morphgnt.ps1");
            return;
        }

        var numerals = Nestle()
            .Where(word => word.Morphology.GetValueOrDefault("form") == "A-NUI")
            .ToList();

        numerals.Should().HaveCount(476);
        numerals.Should().OnlyContain(word => !word.Morphology.ContainsKey("case"));
    }

    private static (int Agree, int Disagree, int Added) Agreement(
        IReadOnlyList<JoinWord> nestle,
        IReadOnlyList<MorphGntWord> parsing,
        MorphGntJoinOutcome join,
        string feature,
        Func<string, string?> read)
    {
        int agree = 0, disagree = 0, added = 0;

        foreach (var row in join.Rows)
        {
            var stated = nestle[row.Witness].Morphology.GetValueOrDefault(feature);
            var other = read(parsing[row.Parsing].Parse);

            if (other is null)
            {
                continue;
            }

            if (stated is null)
            {
                added++;
            }
            else if (stated == other)
            {
                agree++;
            }
            else
            {
                disagree++;
            }
        }

        return (agree, disagree, added);
    }

    private static int Undecided(
        IReadOnlyList<JoinWord> nestle,
        IReadOnlyList<MorphGntWord> parsing,
        MorphGntJoinOutcome join) =>
        join.Rows.Count(row =>
            nestle[row.Witness].Morphology.GetValueOrDefault("voice") == "middlepassive"
            && MorphGntParsing.Voice(parsing[row.Parsing].Parse) is not null);
}
