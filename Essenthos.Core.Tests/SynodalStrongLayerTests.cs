using Essenthos.Core.Loading.Links;
using Essenthos.Core.XmlBible;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Laying the tagged Synodal, in its own numbering, onto the Synodal the corpus holds in the King
/// James's. The addresses are real ones — the places the two numberings part.
/// </summary>
public sealed class SynodalStrongLayerTests
{
    private const int Psalms = 19;

    private const int Acts = 44;

    private long _nextId;

    private readonly Dictionary<(int Book, int Chapter, int Verse), List<EditionWord>> _edition = [];

    private int _nextUnit;

    private void Tagged((int Book, int Chapter, int Verse) address, params (string Text, string? Number)[] words) =>
        _edition[address] =
        [
            .. words.Select(word => new EditionWord(
                word.Text, word.Number is null ? [] : [word.Number], ++_nextUnit)),
        ];

    private CorpusVerse Loaded(int book, int chapter, int verse, VerseAddress[] printed, params string[] words) =>
        new(book, chapter, verse, printed, [.. words.Select(word => new CorpusWord(++_nextId, word))]);

    private static IReadOnlyList<string> NumbersOf(LaidEdition laid, CorpusVerse verse, int position) =>
        laid.Tags.TryGetValue(verse.Words[position].Id, out var tag) ? tag.Numbers : [];

    /// <summary>Where the loaded file prints nothing, the two numberings are the same and the verse is found there.</summary>
    [Fact]
    public void AVerseThatPrintsNoAddressTakesTheTaggedVerseAtItsOwn()
    {
        Tagged((1, 1, 1), ("В", null), ("начале", "H7225"));
        var genesis = Loaded(1, 1, 1, [], "В", "начале");

        var laid = SynodalStrongLayer.Lay(_edition, [genesis]);

        NumbersOf(laid, genesis, 1).Should().Equal("H7225");
        NumbersOf(laid, genesis, 0).Should().BeEmpty();
        laid.Renumbered.Should().Be(0);
    }

    /// <summary>
    /// The shepherd psalm is the Synodal's 22 and the corpus's 23, and bible4u says so by printing
    /// <c>(22-1)</c> at its head. The tagged verse 22:1 goes there and not to the corpus's 22:1,
    /// which is a different psalm.
    /// </summary>
    [Fact]
    public void APrintedAddressPlacesTheTaggedVerse()
    {
        Tagged((Psalms, 21, 2), ("Боже", "H410"), ("мой", null));
        Tagged((Psalms, 22, 1), ("Господь", "H3068"), ("Пастырь", "H7462"), ("мой", null));
        var twentySecond = Loaded(Psalms, 22, 1, [new VerseAddress(21, 2)], "Боже", "мой");
        var twentyThird = Loaded(Psalms, 23, 1, [new VerseAddress(22, 1)], "Господь", "Пастырь", "мой");

        var laid = SynodalStrongLayer.Lay(_edition, [twentySecond, twentyThird]);

        NumbersOf(laid, twentyThird, 1).Should().Equal("H7462");
        NumbersOf(laid, twentySecond, 0).Should().Equal("H410");
        laid.Renumbered.Should().Be(2);
    }

    /// <summary>
    /// The Synodal numbers Psalm 3's title as verse one and the corpus's 3:1 holds it before a
    /// <c>(3-2)</c> marker. Nothing prints 3:1, so it goes to the verse whose first marker is the
    /// next verse, ahead of it.
    /// </summary>
    [Fact]
    public void ASuperscriptionGoesToTheVerseItsMarkerFollows()
    {
        Tagged((Psalms, 3, 1), ("Псалом", null), ("Давида", "H1732"));
        Tagged((Psalms, 3, 2), ("Господи", "H3068"));
        var verse = Loaded(Psalms, 3, 1, [new VerseAddress(3, 2)], "Псалом", "Давида", "Господи");

        var laid = SynodalStrongLayer.Lay(_edition, [verse]);

        laid.Unused.Should().Be(0);
        NumbersOf(laid, verse, 1).Should().Equal("H1732");
        NumbersOf(laid, verse, 2).Should().Equal("H3068");
    }

    /// <summary>
    /// The tagged edition does not divide Acts 19:40 from 19:41, so its 19:40 holds both and the
    /// corpus's 19:41 has nothing of its own to agree with. Laid as one stretch, both verses get
    /// their numbers.
    /// </summary>
    [Fact]
    public void AVerseTheEditionDoesNotDivideIsLaidWithItsNeighbour()
    {
        Tagged((Acts, 19, 40), ("быть", "G1458"), ("обвиненными", "G1458"), ("Сказав", "G2036"), ("это", "G5023"));
        var fortieth = Loaded(Acts, 19, 40, [], "быть", "обвиненными");
        var fortyFirst = Loaded(Acts, 19, 41, [], "Сказав", "это");

        var laid = SynodalStrongLayer.Lay(_edition, [fortieth, fortyFirst]);

        laid.Joined.Should().Be(2);
        laid.Refused.Should().Be(0);
        NumbersOf(laid, fortyFirst, 0).Should().Equal("G2036");
    }

    /// <summary>
    /// Two verses that share a number and not their words are not the same verse, and a number laid
    /// on the wrong word is worse than none.
    /// </summary>
    [Fact]
    public void AVerseWhoseWordsDoNotAgreeIsRefused()
    {
        Tagged((1, 1, 1), ("В", null), ("начале", "H7225"), ("сотворил", "H1254"));
        var other = Loaded(1, 1, 1, [], "Земля", "же", "была", "безвидна");

        var laid = SynodalStrongLayer.Lay(_edition, [other]);

        laid.Refused.Should().Be(1);
        laid.Tags.Should().BeEmpty();
    }
}
