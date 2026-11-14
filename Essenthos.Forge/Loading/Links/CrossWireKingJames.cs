namespace Essenthos.Core.Loading.Links;

/// <param name="Numbers">The Strong numbers of the element the word came from, in the order the module writes them.</param>
/// <param name="Unit">The element, so that the words of one element are one rendering and find its positions.</param>
/// <param name="Supplied">The edition prints the word as one its translators supplied, and its element names no Greek word.</param>
/// <param name="Number">The number the word carries as its own (<see cref="CrossWireKingJames.Own"/>), null in an element naming none.</param>
internal readonly record struct KingJamesTag(IReadOnlyList<string> Numbers, int Unit, bool Supplied, string? Number);

/// <param name="Tags">What each corpus word of a laid verse was given, by word id. A word not here is in no element.</param>
/// <param name="Positions">The Received Text positions each element states, by its unit.</param>
/// <param name="Unrendered">
/// The positions of the elements that print no word, by the verse they stand in: Greek words the King James leaves
/// untranslated, most of them the article.
/// </param>
/// <param name="Verses">New Testament verses the module was laid onto.</param>
/// <param name="Refused">Verses whose words the module does not write the same way, which get nothing.</param>
internal sealed record LaidKingJames(
    IReadOnlyDictionary<long, KingJamesTag> Tags,
    IReadOnlyDictionary<int, StatedPositions> Positions,
    IReadOnlyDictionary<(int Book, int Chapter, int Verse), List<IReadOnlyList<StatedPosition>>> Unrendered,
    int Verses,
    int Refused)
{
    public override string ToString() =>
        $"CrossWire's KJV laid onto {Verses} verses, {Refused} refused; {Tags.Values.Count(tag => tag.Numbers.Count > 0)} words "
        + $"in a numbered element, {Tags.Values.Count(tag => tag.Supplied)} supplied, {Positions.Count} elements stating "
        + $"positions, {Unrendered.Values.Sum(verse => verse.Count)} that print no word";
}

/// <summary>
/// CrossWire's King James Version 3.1, laid onto the King James the corpus loads from bible4u. In the New Testament
/// every element of the module names the Greek words it renders twice over: by Strong number, and by their places in
/// the verse of the KJV2003 project's Received Text (<see cref="ReceivedTextPositions"/>). An element that prints no
/// word is a Greek word the English leaves untranslated, and <c>transChange type="added"</c> marks the words the
/// translators supplied, which the King James prints in italics.
///
/// <para>
/// **The two digitisations are paired by their letters, verse by verse** (<see cref="TaggedEdition"/>): both number
/// the verses as the King James does, so a verse is laid onto the verse of its own address, and one whose words the
/// two do not write the same way gets nothing rather than a neighbour's. The words a correction adds to the loaded
/// text (the epistles' subscriptions) are the caller's to leave out: the module does not print them as verse words.
/// </para>
///
/// <para>
/// **A word takes its element's numbers whole.** <em>the world</em> is one element over τὸν κόσμον, so both words carry
/// G3588 and G2889, and the element's link names both Greek words for both English ones. The number a word shows as its
/// own is one of the element's: the article's for the English article, which renders nothing else, and otherwise the
/// first that is not the article, since the element names the article beside the word it belongs to (<em>God</em> over
/// ὁ θεός). Without the first, every <em>the</em> of a phrase would be counted among the renderings of its noun.
/// </para>
/// </summary>
internal static class CrossWireKingJames
{
    public const string Module = "KJV";

    public const int FirstBook = 40;

    public const int LastBook = 66;

    /// <summary>Whether a book is the New Testament's, the only part of the module that is read.</summary>
    public static bool InNewTestament(int book) => book is >= FirstBook and <= LastBook;

    private const string Article = "G3588";

    /// <summary>The module's root under Resources.</summary>
    public static string Folder => SwordTextSource.Numberings[Module].Folder.Replace('\\', '/');

    /// <summary>The number a word of an element carries as its own: the article's for <em>the</em>, else <see cref="Primary"/>.</summary>
    public static string? Own(IReadOnlyList<string> numbers, string word) =>
        word.Equals("the", StringComparison.OrdinalIgnoreCase) && numbers.Contains(Article) ? Article : Primary(numbers);

    /// <summary>The element's first number that is not the article, or the article where it names nothing else.</summary>
    public static string? Primary(IReadOnlyList<string> numbers)
    {
        foreach (var number in numbers)
        {
            if (number != Article)
            {
                return number;
            }
        }

        return numbers.Count > 0 ? numbers[0] : null;
    }

    /// <param name="folder">The module's root: the folder holding <c>mods.d</c>.</param>
    /// <param name="verses">The corpus's verses, each with the words the module may print; only the New Testament's are laid.</param>
    public static LaidKingJames Lay(string folder, IEnumerable<CorpusVerse> verses) => Lay(SwordTextSource.Elements(folder), verses);

    /// <param name="elements">The module's verses as <see cref="SwordTextSource.Elements"/> reads them.</param>
    /// <param name="verses">The corpus's verses, each with the words the module may print; only the New Testament's are laid.</param>
    public static LaidKingJames Lay(
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), (List<SwordWord> Words, List<IReadOnlyList<StatedPosition>> Unrendered)> elements,
        IEnumerable<CorpusVerse> verses)
    {
        var tags = new Dictionary<long, KingJamesTag>(190_000);
        var positions = new Dictionary<int, StatedPositions>(130_000);
        var unrendered = new Dictionary<(int, int, int), List<IReadOnlyList<StatedPosition>>>(6_000);
        int laid = 0, refused = 0;
        foreach (var verse in verses)
        {
            var address = (verse.Book, verse.Chapter, verse.Verse);
            if (!InNewTestament(verse.Book) || verse.Words.Count == 0 || !elements.TryGetValue(address, out var element))
            {
                continue;
            }

            var printed = element.Words.Where(word => word.Surface.Length > 0).ToList();
            var spans = TaggedEdition.Align([.. printed.Select(word => word.Surface)], [.. verse.Words.Select(word => word.Text)]);
            if (TaggedEdition.Agreement(spans, verse.Words.Count) < SynodalStrongLayer.SameVerse)
            {
                refused++;
                continue;
            }

            laid++;
            foreach (var span in spans)
            {
                SwordWord? numbered = null;
                var supplied = true;
                for (var i = span.TaggedFrom; i < span.TaggedTo; i++)
                {
                    if (numbered is null && printed[i].Numbers.Count > 0)
                    {
                        numbered = printed[i];
                    }

                    supplied &= printed[i].Added;
                }

                IReadOnlyList<string> numbers = numbered?.Numbers ?? [];
                var unit = (numbered ?? printed[span.TaggedFrom]).Unit;
                for (var i = span.CorpusFrom; i < span.CorpusTo; i++)
                {
                    tags[verse.Words[i].Id] = new KingJamesTag(numbers, unit, supplied && numbered is null, Own(numbers, verse.Words[i].Text));
                }
            }

            foreach (var word in printed)
            {
                if (word.Positions is { Count: > 0 } stated)
                {
                    positions.TryAdd(word.Unit, new StatedPositions(verse.Book, verse.Chapter, verse.Verse, stated));
                }
            }

            if (element.Unrendered.Count > 0)
            {
                unrendered[address] = element.Unrendered;
            }
        }

        return new LaidKingJames(tags, positions, unrendered, laid, refused);
    }
}
