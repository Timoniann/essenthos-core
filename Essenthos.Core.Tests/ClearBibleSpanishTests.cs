using Essenthos.Core.ClearBible;
using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// That Clear Bible's Reina-Valera alignment is keyed to the Reina-Valera this corpus loads, proved
/// from the two files rather than from the metadata that says so.
///
/// The metadata does say so — <c>identifier = "RV09"</c>, <c>url =
/// ebible.org/find/details.php?id=spaRV1909</c> — and that is exactly the kind of claim this corpus
/// was taught not to trust: the same repository's Russian set names a token file its records do not
/// correspond to, and nothing about the label showed it. A hand-made alignment keyed to a different
/// edition would put real names on the wrong words, which is the worst thing this corpus can do, and
/// it would look like data.
///
/// So the check is the text itself. Every verse of both files, word for word.
/// </summary>
public class ClearBibleSpanishTests(Ebible ebible, ITestOutputHelper output) : IClassFixture<Ebible>
{
    /// <summary>
    /// How much of the Bible the two have to write identically. It sits well below what they in fact
    /// do — 97.9% of verses match word for word — because the number to fail on is *a different
    /// edition*, and the 1960 revision differs from the 1909 in tens of thousands of verses.
    /// </summary>
    private const double SameEdition = 0.95;

    private static Dictionary<(int, int, int), List<string>> Theirs()
    {
        var verses = new Dictionary<(int, int, int), List<string>>(31_100);

        foreach (var file in new[] { "ot_RV09.tsv", "nt_RV09.tsv" })
        {
            var path = Path.Combine(
                TestResources.ClearBibleFolder, "data", "spa", "targets", "RV09", file);

            foreach (var token in ClearBibleAlignment.Tokens(path))
            {
                if (token.Excluded || !ClearBibleAlignment.Address(token.Id, out var b, out var c, out var v))
                {
                    continue;
                }

                if (!verses.TryGetValue((b, c, v), out var words))
                {
                    words = [];
                    verses[(b, c, v)] = words;
                }

                words.Add(Letters(token.Text));
            }
        }

        return verses;
    }

    private Dictionary<(int, int, int), List<string>> Ours() =>
        ebible.ReinaValera.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse =>
                (Address: (book.CanonicalOrdinal, chapter.Number, verse.Number),
                 Words: verse.Words.Select(word => Letters(word.Surface)).ToList()))))
            .ToDictionary(row => row.Address, row => row.Words);

    /// <summary>
    /// A word's own letters. Their tokeniser makes a token of punctuation and this reader hangs it
    /// on the word before, so the two cannot be compared with it in place — and an inverted question
    /// mark is not a disagreement about the text.
    /// </summary>
    private static string Letters(string word) =>
        new([.. word.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    [Fact]
    public void TheAlignmentIsKeyedToTheReinaValeraThisCorpusLoads()
    {
        var theirs = Theirs();
        var ours = Ours();

        var shared = ours.Keys.Intersect(theirs.Keys).ToList();
        var identical = shared.Count(address => ours[address].SequenceEqual(theirs[address]));

        output.WriteLine(
            $"{shared.Count} verses in both, {identical} word for word, " +
            $"{ours.Count - shared.Count} only ours, {theirs.Count - shared.Count} only theirs");

        shared.Should().HaveCountGreaterThan(30_000);
        ((double)identical / shared.Count).Should().BeGreaterThan(SameEdition);
    }

    /// <summary>
    /// Their tokeniser counts punctuation as a word and marks it out of the alignment. Read with the
    /// flag, what is left is the same number of words this corpus stores — which is the other half
    /// of the identity, and the reason the target ids can be placed at all.
    /// </summary>
    [Fact]
    public void TheirWordsAndOursAreTheSameWordsOnceTheirPunctuationIsDropped()
    {
        var theirs = Theirs().Sum(verse => verse.Value.Count);
        var ours = Ours().Sum(verse => verse.Value.Count);

        theirs.Should().BeCloseTo(ours, 10);
    }

    /// <summary>
    /// Genesis 1:1 in the 1909 reads <em>crió</em> where every modern Reina-Valera reads
    /// <em>creó</em>. If the alignment were keyed to the 1960 this is the first word that would say
    /// so, and it is worth asserting by name rather than only inside a percentage.
    /// </summary>
    [Fact]
    public void TheirGenesisIsTheEditionThatWritesCrio() =>
        Theirs()[(1, 1, 1)].Should().Equal("en", "el", "principio", "crió", "dios", "los", "cielos",
            "y", "la", "tierra");
}
