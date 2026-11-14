namespace Essenthos.Core.Loading.Links;

/// <param name="Numbers">The Strong numbers of the element the word came from, in the order the module writes them.</param>
/// <param name="Unit">The element, so that the words of one element are one rendering and find its positions.</param>
/// <param name="Supplied">The edition prints the word as one its translators supplied, and its element names no Greek word.</param>
/// <param name="Number">
/// The number the word carries as its own (<see cref="CrossWireKingJames.Own"/>): null in an element naming none, and for a
/// function word whose element names no Greek word of its class.
/// </param>
/// <param name="Closed">The word is an English function word (<see cref="CrossWireKingJames.Closed"/>), which renders its own number or nothing.</param>
internal readonly record struct KingJamesTag(IReadOnlyList<string> Numbers, int Unit, bool Supplied, string? Number, bool Closed = false);

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
/// own (<see cref="Own"/>) is one of the element's, and an English function word shows only the number of a Greek word of
/// its own class: <em>the</em> the article's, <em>of</em> or <em>unto</em> a preposition's, <em>he</em> or <em>it</em> a
/// pronoun's (or the article's, which Greek uses as one), <em>and</em> or <em>when</em> a conjunction's or a particle's.
/// Otherwise it shows none and stays in the element's link: <em>he</em> in <em>he gave</em> renders the person ending of
/// ἔδωκεν, not the lexeme, <em>of</em> in <em>of God</em> the genitive of θεοῦ, <em>shall</em> in <em>shall come</em> the
/// future, and <em>a</em> or <em>an</em> nothing Greek writes. An auxiliary shows the verb's number only where it is the
/// verb (<em>it was</em> over ἐγένετο, <em>they have</em> over ἀπέχουσιν). Without this, every function word of a phrase
/// would be counted among the renderings of its lexeme: about 38,000 words of the New Testament.
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

    /// <summary>What an English function word is, by the Greek words it may take a number from.</summary>
    [Flags]
    private enum Kind
    {
        None = 0,
        Article = 1,
        Indefinite = 2,
        Preposition = 4,
        Pronoun = 8,
        Particle = 16,
        Negative = 32,
        Auxiliary = 64,
        Adverb = 128,
    }

    private static readonly Dictionary<string, Kind> Function = Kinds(
        (Kind.Article, "the"),
        (Kind.Indefinite, "a an"),
        (Kind.Preposition, "of to unto in into by with from at on upon through throughout among amongst toward towards against over "
                           + "under out about above below beneath beside besides between betwixt beyond within without concerning near "
                           + "round behind off up down across along underneath"),
        (Kind.Preposition | Kind.Particle, "for before after until till since"),
        (Kind.Pronoun, "i me my mine myself thou thee thy thine thyself he him his himself she her hers herself it its itself we us our "
                       + "ours ourselves ye you your yours yourselves they them their theirs themselves this these those who whom whose "
                       + "which what such"),
        (Kind.Pronoun | Kind.Particle, "that why wherefore"),
        (Kind.Particle, "and but or either if when whenever as so then therefore because though although while whilst whether lest "
                        + "yet also even how where wherein whereby whither whence thus now"),
        (Kind.Particle | Kind.Negative, "neither nor"),
        (Kind.Negative, "not no never none nothing nay"),
        (Kind.Auxiliary, "shall shalt should shouldest will wilt would wouldest have hast hath has had hadst having be been being am art "
                         + "is are was wast were wert do dost doth did didst let may mayest might mightest can canst could couldest must"),
        (Kind.Adverb, "there"));

    /// <summary>Robinson's codes for the Greek pronouns, of every kind, and the article Greek uses as one.</summary>
    private static readonly HashSet<string> Pronouns = ["P", "R", "C", "D", "K", "I", "X", "Q", "F", "S", "T"];

    private static readonly HashSet<string> Particles = ["CONJ", "COND", "PRT", "ADV", "INJ"];

    /// <summary>The classes a word of content is not the rendering of while its element names a word of another.</summary>
    private static readonly HashSet<string> Grammatical = ["T", "CONJ", "COND", "PRT", "PREP", "INJ"];

    private static Dictionary<string, Kind> Kinds(params (Kind Kind, string Words)[] kinds)
    {
        var words = new Dictionary<string, Kind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, list) in kinds)
        {
            foreach (var word in list.Split(' '))
            {
                words[word] = words.GetValueOrDefault(word) | kind;
            }
        }

        return words;
    }

    /// <summary>
    /// Whether a word is an English function word: an article, a preposition, a pronoun, a conjunction or particle, a
    /// negative or an auxiliary.
    /// </summary>
    public static bool Closed(string word) => Function.ContainsKey(word);

    /// <summary>
    /// The number a word of an element carries as its own.
    /// <list type="bullet">
    /// <item>A function word takes the number of a Greek word of its own class the element names, and none where it names
    /// none: the article for <em>the</em>; a preposition for <em>of</em>, <em>unto</em>, <em>to</em>; a pronoun or the article
    /// for <em>he</em>, <em>his</em>, <em>which</em>; a conjunction, particle or adverb for <em>and</em>, <em>when</em>; a
    /// negative for <em>not</em>; an adverb for <em>there</em>; never anything for <em>a</em> or <em>an</em>.</item>
    /// <item>An auxiliary takes the element's verb only where it is the verb: no word of the element is a word of content
    /// and no auxiliary follows it (<em>it was</em>, the <em>be</em> of <em>shall be</em>). A preposition whose element holds
    /// no word of content takes an adverb too, which is how Robinson codes the prepositions that are adverbs
    /// (<em>before</em> over ἔμπροσθεν).</item>
    /// <item>Any other word takes <see cref="Primary"/>, and so does a negative over an element that names no negative
    /// (<em>not</em> over ἀπίστων).</item>
    /// </list>
    /// </summary>
    /// <param name="numbers">The element's numbers, in the module's order.</param>
    /// <param name="morphology">The element's Robinson code for each number; null where it writes none.</param>
    /// <param name="word">The word, as the corpus prints it.</param>
    /// <param name="element">Every word of the element as the module prints it, in order, the word itself among them.</param>
    public static string? Own(IReadOnlyList<string> numbers, IReadOnlyList<string?>? morphology, string word, IReadOnlyList<string> element)
    {
        if (numbers.Count == 0)
        {
            return null;
        }

        if (!Function.TryGetValue(word, out var kind))
        {
            return Primary(numbers, morphology);
        }

        string? First(Func<string, bool> matches)
        {
            for (var at = 0; at < numbers.Count; at++)
            {
                if (morphology is not null && at < morphology.Count && morphology[at] is { } code && matches(code))
                {
                    return numbers[at];
                }
            }

            return null;
        }

        var headed = element.All(Function.ContainsKey);
        if (kind.HasFlag(Kind.Auxiliary))
        {
            var last = element.LastOrDefault(other => Function.GetValueOrDefault(other).HasFlag(Kind.Auxiliary));
            return headed && string.Equals(last, word, StringComparison.OrdinalIgnoreCase) ? First(code => Part(code) == "V") : null;
        }

        if (kind.HasFlag(Kind.Negative) && First(Negative) is { } negative)
        {
            return negative;
        }

        return First(code =>
                   (kind.HasFlag(Kind.Article) && Part(code) == "T")
                   || (kind.HasFlag(Kind.Preposition) && (Part(code) == "PREP" || (headed && Part(code) == "ADV")))
                   || (kind.HasFlag(Kind.Pronoun) && Pronouns.Contains(Part(code)))
                   || (kind.HasFlag(Kind.Particle) && Particles.Contains(Part(code)))
                   || (kind.HasFlag(Kind.Adverb) && Part(code) == "ADV"))
               ?? (kind == Kind.Negative ? Primary(numbers, morphology) : null);
    }

    /// <summary>The part of speech a Robinson code begins with: <c>T</c> of <c>T-NSM</c>, <c>PREP</c>, <c>V</c>.</summary>
    private static string Part(string code) => code.IndexOf('-') is var dash and >= 0 ? code[..dash] : code;

    /// <summary>A Robinson code of a negative: οὐ is <c>PRT-N</c>, οὐδέ <c>CONJ-N</c>, οὐδείς <c>A-NSM-N</c>.</summary>
    private static bool Negative(string code) => code.EndsWith("-N", StringComparison.Ordinal);

    /// <summary>
    /// The element's first number of a word of content (<em>cannot</em> over οὐ δύνασθε is δύναμαι), otherwise its first
    /// that is not the article, and the article where it names nothing else.
    /// </summary>
    public static string? Primary(IReadOnlyList<string> numbers, IReadOnlyList<string?>? morphology = null)
    {
        if (morphology is not null)
        {
            for (var at = 0; at < numbers.Count && at < morphology.Count; at++)
            {
                if (numbers[at] != Article && morphology[at] is { } code && !Grammatical.Contains(Part(code)))
                {
                    return numbers[at];
                }
            }
        }

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
            var units = printed.GroupBy(word => word.Unit).ToDictionary(unit => unit.Key, unit => unit.Select(word => word.Surface).ToList());
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
                    var text = verse.Words[i].Text;
                    tags[verse.Words[i].Id] = new KingJamesTag(numbers, unit, supplied && numbered is null,
                        Own(numbers, numbered?.Morphology, text, units[unit]), Closed(text));
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
