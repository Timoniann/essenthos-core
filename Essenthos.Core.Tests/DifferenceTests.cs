using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Two texts told apart word by word: what the other does not have, what it has and says otherwise,
/// and what it has and writes otherwise — from the links and the dictionary forms the corpus holds.
/// </summary>
public sealed class DifferenceDegreeTests
{
    private static readonly HashSet<string> None = [];

    private static DifferenceWord Hebrew(long id, string letters, string lexeme) =>
        new(id, letters, lexeme, null, null, None);

    private static VersePair Pair(
        DifferenceWord[] a,
        DifferenceWord[] b,
        (long[] A, long[] B)[] joined,
        long[]? recorded = null,
        long[]? reaching = null) =>
        new(a, b, [.. joined.Select(j => ((IReadOnlyList<long>)j.A, (IReadOnlyList<long>)j.B))],
            new HashSet<long>(recorded ?? []), new HashSet<long>(reaching ?? []));

    /// <summary>
    /// The three degrees on one verse: the same letters, one lexeme written two ways, two lexemes —
    /// and a word only one of the two has, as a link records it.
    /// </summary>
    [Fact]
    public void AVerseOfTwoWitnessesComesOutInTheThreeDegrees()
    {
        var a = new[] { Hebrew(1, "אלה", "אלה"), Hebrew(2, "תולדות", "תולדה"), Hebrew(3, "שביעי", "שביעי"), Hebrew(4, "ארצ", "ארצ") };
        var b = new[] { Hebrew(11, "אלה", "אלה"), Hebrew(12, "תולדת", "תולדה"), Hebrew(13, "ששי", "ששי") };

        var (left, right) = Differences.Compare(
            Pair(a, b, [([1], [11]), ([2], [12]), ([3], [13])], recorded: [4]),
            DifferenceBasis.Links,
            sameLanguage: true);

        left.Values.Select(d => (d.Degree, d.By)).Should().Equal(
            (Degree.Same, Decision.Letters),
            (Degree.Form, Decision.Lexeme),
            (Degree.Lemma, Decision.Lexeme),
            (Degree.Absent, Decision.Recorded));
        right[13].With.Should().Equal(3);
    }

    /// <summary>
    /// Genesis 2:4: the Masoretic has <em>the heavens and the earth ... earth and heavens</em>, the
    /// Samaritan <em>... heavens and earth</em>. The alignment keeps order, so the links record the
    /// earth missing from each; it is one word moved, and its conjunction goes with it.
    /// </summary>
    [Fact]
    public void AWordMissingHereAndThereWithTheSameLettersMoved()
    {
        var a = new[] { Hebrew(1, "יהוה", "יהוה"), Hebrew(2, "ארצ", "ארצ"), Hebrew(3, "ו", "ו"), Hebrew(4, "שמימ", "שמימ") };
        var b = new[] { Hebrew(11, "יהוה", "יהוה"), Hebrew(12, "שמימ", "שמימ"), Hebrew(13, "ו", "ו"), Hebrew(14, "ארצ", "ארצ") };

        var (left, right) = Differences.Compare(
            Pair(a, b, [([1], [11]), ([4], [12])], recorded: [2, 3, 13, 14]),
            DifferenceBasis.Links,
            sameLanguage: true);

        left[2].Should().BeEquivalentTo(new WordDegree(Degree.Moved, Decision.Position, [14]), because: "the letters are the same");
        left[3].Degree.Should().Be(Degree.Moved, "a conjunction goes with the word it stands beside in both");
        right[13].Degree.Should().Be(Degree.Moved);
    }

    /// <summary>
    /// Exodus 20:8: the Samaritan says <em>keep</em> where the Masoretic says <em>remember</em>. Their
    /// letters are too unlike for the alignment to pair them, so each is recorded missing from the
    /// other — at the same place, one each, which is one text saying something else.
    /// </summary>
    [Fact]
    public void OneWordMissingFromEachAtTheSamePlaceIsAnotherWord()
    {
        var a = new[] { Hebrew(1, "זכור", "זכר"), Hebrew(2, "את", "את"), Hebrew(3, "ו", "ו") };
        var b = new[] { Hebrew(11, "שמור", "שמר"), Hebrew(12, "את", "את"), Hebrew(13, "שדהו", "שדה") };

        var (left, right) = Differences.Compare(
            Pair(a, b, [([2], [12])], recorded: [1, 11, 3, 13]),
            DifferenceBasis.Links,
            sameLanguage: true);

        left[1].Should().BeEquivalentTo(new WordDegree(Degree.Lemma, Decision.Lexeme, [11]));
        right[11].Degree.Should().Be(Degree.Lemma);
        left[3].Degree.Should().Be(Degree.Absent, "a conjunction is not another text's way of saying a noun");
        right[13].Degree.Should().Be(Degree.Absent);
    }

    /// <summary>
    /// A Hebrew word and the Greek linked to it correspond; nothing independent of that link says
    /// whether they mean the same, so the answer is that it cannot be told — not "the same".
    /// </summary>
    [Fact]
    public void AcrossTwoLanguagesALinkAloneCannotTellWhetherTheWordsAgree()
    {
        var a = new[] { Hebrew(1, "ברא", "ברא"), Hebrew(2, "את", "את") };
        var b = new[] { new DifferenceWord(11, "εποιησεν", null, "ποιεω", null, None) };

        var (left, _) = Differences.Compare(Pair(a, b, [([1], [11])]), DifferenceBasis.Links, sameLanguage: false);

        left[1].Should().BeEquivalentTo(new WordDegree(Degree.Linked, Decision.Link, [11]));
        left[2].Should().BeEquivalentTo(new WordDegree(Degree.Unlinked, Decision.Nothing, []));
    }

    /// <summary>
    /// Two translations' words carry no dictionary form; the Hebrew words they are linked to do,
    /// and that is what tells <em>idol</em> for the same word from <em>idol</em> for another.
    /// </summary>
    [Fact]
    public void TwoTranslationsAreComparedByTheOriginalWordsBehindThem()
    {
        DifferenceWord English(long id, string letters, params string[] originals) =>
            new(id, letters, null, null, null, originals.ToHashSet());
        var a = new[] { English(1, "image", "H6459"), English(2, "thou", "H6213") };
        var b = new[] { English(11, "idol", "H6459"), English(12, "you", "H3808") };

        var (left, _) = Differences.Compare(
            Pair(a, b, [([1], [11]), ([2], [12])]), DifferenceBasis.Links, sameLanguage: true);

        left[1].Should().BeEquivalentTo(new WordDegree(Degree.Form, Decision.Original, [11]));
        left[2].Should().BeEquivalentTo(new WordDegree(Degree.Lemma, Decision.Original, [12]));
    }

    [Fact]
    public void ThroughAThirdTextAWordNothingOfTheOtherMeetsIsUnmatchedNotAbsent()
    {
        var a = new[] { new DifferenceWord(1, "бог", null, null, null, None), new DifferenceWord(2, "же", null, null, null, None) };
        var b = new[] { new DifferenceWord(11, "бог", null, null, null, None) };

        var (left, _) = Differences.Compare(
            Pair(a, b, [([1], [11])], reaching: [1, 2, 11]), DifferenceBasis.Through, sameLanguage: false);

        left[1].Should().BeEquivalentTo(new WordDegree(Degree.Same, Decision.Through, [11]));
        left[2].Should().BeEquivalentTo(new WordDegree(Degree.Unmatched, Decision.Through, []));
    }

    [Theory]
    [InlineData("H0776", "H776")]
    [InlineData("g26", "G26")]
    [InlineData("H9000", "H9000")]
    public void AStrongNumberIsOneEntryHoweverItIsPadded(string number, string key) =>
        DifferenceEndpoints.StrongKey(number).Should().Be(key);

    [Theory]
    [InlineData("LORD,", "eng", "lord")]
    [InlineData("Ёлка", "rus", "елка")]
    [InlineData("שָּׁמַ֛יִם", "hbo", "שמימ")]
    [InlineData("Ἐν", "grc", "εν")]
    public void AWordIsComparedByItsLetters(string surface, string language, string letters) =>
        DifferenceEndpoints.Letters(surface, language).Should().Be(letters);
}

/// <summary>The same comparison read from the database: the links, the dictionary forms, and the counts.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class DifferenceEndpointTests : IDisposable
{
    private const int Genesis = 1;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public DifferenceEndpointTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// The Masoretic lemma column is the occurrence's spelling; the dictionary form is in its
    /// morphology, and the Samaritan's is its lemma. Compared through those, <em>generations</em>
    /// written plene is the same word and <em>seventh</em> against <em>sixth</em> is not.
    /// </summary>
    [Fact]
    public async Task TwoWitnessesAreComparedByTheirLinksAndTheirDictionaryForms()
    {
        var masoretic = Corpus.Add(_db, "test-mt", TextKind.CriticalEdition, "hbo",
            (2, 2, ["תֹולְדֹות", "הַ", "שְּׁבִיעִי", "יֹום"]),
            (2, 3, ["וַ", "יְבָרֶךְ"]));
        var samaritan = Corpus.Add(_db, "test-sp", TextKind.ManuscriptTradition, "hbo",
            (2, 2, ["תולדת", "ה", "ששי"]),
            (2, 3, ["ו", "יברך"]));
        await _db.SaveChangesAsync();

        Lexemes(masoretic, 2, 2, "תּוֹלֵדָה", "הַ", "שְׁבִיעִי", "יוֹם");
        Lexemes(masoretic, 2, 3, "וְ", "ברך");
        Lemmas(samaritan, 2, 2, "תולדה/", "ה", "ששׁי/");
        Lemmas(samaritan, 2, 3, "ו", "ברכ[");
        var equals = (LinkRelation.Equals, 0.95);
        Link(samaritan, masoretic, equals, (2, 2, 2), (2, 2, 2));
        Link(samaritan, masoretic, (LinkRelation.Renders, 0.85), (2, 2, 1), (2, 2, 1));
        Link(samaritan, masoretic, (LinkRelation.Renders, 0.75), (2, 2, 3), (2, 2, 3));
        Link(samaritan, masoretic, (LinkRelation.Omits, 0.85), null, (2, 2, 4));
        Link(samaritan, masoretic, equals, (2, 3, 1), (2, 3, 1));
        Link(samaritan, masoretic, equals, (2, 3, 2), (2, 3, 2));
        await _db.SaveChangesAsync();

        var pair = await Pair(masoretic, samaritan);
        var chapter = await DifferenceEndpoints.Chapter(_db, pair, Genesis, 2, default);

        chapter.Basis.Should().Be("links");
        chapter.Verses.Single(v => v.Number == 2).A.Select(w => (w.Degree, w.By)).Should().Equal(
            ("form", "lexeme"), ("same", "letters"), ("lemma", "lexeme"), ("absent", "recorded"));
        chapter.CountsA.Should().BeEquivalentTo(new { Words = 6, Same = 3, Form = 1, Lemma = 1, Absent = 1 });
        chapter.CountsB.Words.Should().Be(5);

        var book = await DifferenceEndpoints.Book(_db, pair, Genesis, default);
        book.Chapters.Single().CountsA.Should().BeEquivalentTo(chapter.CountsA, "a chapter counts the same in its book");
    }

    /// <summary>
    /// Two translations are read through the original both are linked to, never through the
    /// aligner's links between them; a word the other translation has nothing for at that Hebrew
    /// word is unmatched, and one with no link at all is unlinked.
    /// </summary>
    [Fact]
    public async Task TwoTranslationsAreReadThroughTheOriginal()
    {
        var hebrew = Corpus.Add(_db, "test-hebrew", TextKind.CriticalEdition, "hbo", (1, 1, ["ברא", "אלהים", "את"]));
        var english = Corpus.Add(_db, "test-english", TextKind.Translation, "eng", (1, 1, ["God", "created", "the", "so"]));
        var other = Corpus.Add(_db, "test-other", TextKind.Translation, "eng", (1, 1, ["God", "made"]));
        await _db.SaveChangesAsync();

        var renders = (LinkRelation.Renders, 0.9);
        Link(english, hebrew, renders, (1, 1, 1), (1, 1, 2));
        Link(english, hebrew, renders, (1, 1, 2), (1, 1, 1));
        Link(english, hebrew, renders, (1, 1, 3), (1, 1, 3));
        Link(other, hebrew, renders, (1, 1, 1), (1, 1, 2));
        Link(other, hebrew, renders, (1, 1, 2), (1, 1, 1));
        Link(english, other, renders, (1, 1, 1), (1, 1, 2));
        await _db.SaveChangesAsync();

        var chapter = await DifferenceEndpoints.Chapter(_db, await Pair(english, other), Genesis, 1, default);

        chapter.Basis.Should().Be("through");
        chapter.Through.Should().Be("test-hebrew");
        chapter.Verses.Single().A.Select(w => w.Degree).Should().Equal("same", "form", "unmatched", "unlinked");
    }

    private void Lexemes(Text text, int chapter, int verse, params string[] lexemes)
    {
        for (var i = 0; i < lexemes.Length; i++)
        {
            _db.WordAt(text, chapter, verse, i + 1).Morphology =
                JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string> { ["vocalizedLexeme"] = lexemes[i] }));
        }
    }

    private void Lemmas(Text text, int chapter, int verse, params string[] lemmas)
    {
        for (var i = 0; i < lemmas.Length; i++)
        {
            _db.WordAt(text, chapter, verse, i + 1).Lemma = lemmas[i];
        }
    }

    private void Link(
        Text from,
        Text to,
        (LinkRelation Relation, double Confidence) how,
        (int Chapter, int Verse, int Position)? fromWord,
        (int Chapter, int Verse, int Position)? toWord)
    {
        var link = new Link
        {
            FromTextId = from.Id,
            ToTextId = to.Id,
            Relation = how.Relation,
            Method = LinkMethod.Lexical,
            Confidence = how.Confidence,
            Source = "test",
        };
        if (fromWord is { } f)
        {
            link.Words.Add(new LinkWord { WordId = _db.WordAt(from, f.Chapter, f.Verse, f.Position).Id, Side = LinkSide.From });
        }

        if (toWord is { } t)
        {
            link.Words.Add(new LinkWord { WordId = _db.WordAt(to, t.Chapter, t.Verse, t.Position).Id, Side = LinkSide.To });
        }

        _db.Links.Add(link);
    }

    private async Task<DifferenceEndpoints.ComparedPair> Pair(Text a, Text b)
    {
        await _db.SaveChangesAsync();
        return new DifferenceEndpoints.ComparedPair(
            new DifferenceEndpoints.ComparedText(a.Id, a.Slug, a.Language, a.Kind),
            new DifferenceEndpoints.ComparedText(b.Id, b.Slug, b.Language, b.Kind));
    }
}
