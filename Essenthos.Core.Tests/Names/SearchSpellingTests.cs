using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// The spellings the rendering rule drops that a search still finds the record by: firm, mostly this
/// record where the text prints them, and no other record's name in that text.
/// </summary>
public sealed class SearchSpellingTests
{
    private const int Joseph = 1;
    private const int Aaron = 2;
    private const int Ur = 3;
    private const int Text = 10;

    private static NamedWord Word(
        int entity, string surface, bool firm = true, bool thing = false, int verse = 1, int position = 1) =>
        new(entity, Text, verse, position, surface, " ", null, "ukr", null, null, thing, surface.ToLowerInvariant(), firm);

    private static Rendering Held(int entity, string form) =>
        new(entity, Text, form, NameFolding.Fold(form), 3, true);

    private static (List<SearchSpelling> Spellings, SearchSpellingCount Count) Of(
        IReadOnlyList<NamedWord> words,
        IReadOnlyCollection<Rendering> renderings,
        params (string Word, int Printed)[] printed) =>
        SearchSpellings.Of(words, renderings, printed.ToDictionary(p => (Text, p.Word.ToLowerInvariant()), p => p.Printed));

    [Fact]
    public void AFirmNameThatOpensOtherwiseIsAdmitted()
    {
        var (spellings, count) = Of([Word(Joseph, "Цофнат-Панеах")], [Held(Joseph, "Йосип")], ("Цофнат-Панеах", 1));

        spellings.Should().Equal(new SearchSpelling(Joseph, "Цофнат-Панеах", NameFolding.Fold("Цофнат-Панеах")));
        count.Should().Be(new SearchSpellingCount(1, 1, 0, 0, 0));
    }

    [Fact]
    public void AWordPrintedMostlyAsSomethingElseStaysOut()
    {
        var (spellings, count) = Of([Word(Aaron, "Priester")], [Held(Aaron, "Aaron")], ("Priester", 300));

        spellings.Should().BeEmpty();
        count.CommonWord.Should().Be(1);
    }

    [Fact]
    public void AnUnsureAnnotationStaysOut()
    {
        var (spellings, count) = Of([Word(Joseph, "Цафнат", firm: false)], [Held(Joseph, "Йосип")], ("Цафнат", 1));

        spellings.Should().BeEmpty();
        count.Unsure.Should().Be(1);
    }

    [Fact]
    public void AnotherRecordsNameInTheSameTextStaysItsOwn()
    {
        var (spellings, count) = Of([Word(Joseph, "Ур")], [Held(Joseph, "Йосип"), Held(Ur, "Ур")], ("Ур", 1));

        spellings.Should().BeEmpty();
        count.AnotherName.Should().Be(1);
    }

    [Fact]
    public void AStretchOfChineseWithTheNameInsideItIsNotASearchSpelling()
    {
        var (spellings, count) = Of([Word(Joseph, "現在他")], [Held(Joseph, "約瑟")], ("現在他", 1));

        spellings.Should().BeEmpty();
        count.Unsegmented.Should().Be(1);
    }

    [Fact]
    public void ARecordNamedByEpithetsGainsNoSearchSpelling()
    {
        var (spellings, count) = SearchSpellings.Of(
            [Word(Joseph, "যীচুত")], [Held(Joseph, "Ісус")], new Dictionary<(int, string), int> { [(Text, "যীচুত")] = 1 },
            new HashSet<int> { Joseph });

        spellings.Should().BeEmpty();
        count.Epithet.Should().Be(1);
    }

    [Fact]
    public void ASpellingTheRenderingsHoldIsNotWrittenAgain() =>
        Of([Word(Joseph, "Йосип")], [Held(Joseph, "Йосип")], ("Йосип", 3)).Spellings.Should().BeEmpty();

    [Fact]
    public void AThingsCommonNounIsNotASearchSpelling() =>
        Of([Word(Joseph, "Ковчег", thing: true)], [], ("Ковчег", 1)).Spellings.Should().BeEmpty();

    [Fact]
    public void TheShareIsOfWhatTheTextPrintsThatWay() =>
        Of(
                [Word(Joseph, "Цафнат", verse: 1), Word(Joseph, "Цафнат", firm: false, verse: 2)],
                [Held(Joseph, "Йосип")],
                ("Цафнат", 4))
            .Spellings.Should().ContainSingle();
}
