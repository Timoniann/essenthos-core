using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading.Links;

/// <param name="Book">The canonical ordinal of the verse's book.</param>
/// <param name="Chapter">The chapter the corpus stores the verse under, in the King James's numbering.</param>
/// <param name="Verse">The verse number the corpus stores it under.</param>
/// <param name="Stated">
/// The addresses the loaded file prints for this verse in the Synodal's own numbering, in the order
/// printed, and empty where it printed none.
/// </param>
/// <param name="Words">The verse's words as the corpus holds them, in order.</param>
internal sealed record CorpusVerse(
    int Book,
    int Chapter,
    int Verse,
    IReadOnlyList<VerseAddress> Stated,
    IReadOnlyList<CorpusWord> Words);

internal readonly record struct CorpusWord(long Id, string Text);

/// <param name="Numbers">The Strong numbers the edition puts on the word.</param>
/// <param name="Unit">
/// The tagged element it came from, so that the two words of a phrase the edition tags once are
/// counted as one rendering and not two.
/// </param>
internal readonly record struct WordTag(IReadOnlyList<string> Numbers, int Unit);

/// <param name="Tags">The numbers each corpus word was given, by word id. A word not here is untagged.</param>
/// <param name="Verses">Corpus verses the edition was laid onto.</param>
/// <param name="Renumbered">
/// Of those, the verses whose Synodal address is not their own — the numbering the loaded file
/// states, and the only way the two editions can be put side by side in the Psalms at all.
/// </param>
/// <param name="Joined">
/// Corpus verses laid together with a neighbour, where the two editions divide a run of verses at
/// different places and neither verse agrees on its own.
/// </param>
/// <param name="Refused">Corpus verses the edition could not be laid onto, and that carry no number.</param>
/// <param name="Unused">Edition verses no corpus verse took.</param>
/// <param name="TaggedWords">Corpus words given a number.</param>
internal sealed record LaidEdition(
    IReadOnlyDictionary<long, WordTag> Tags,
    int Verses,
    int Renumbered,
    int Joined,
    int Refused,
    int Unused,
    int TaggedWords)
{
    public override string ToString() =>
        $"the tagged Synodal laid onto {Verses} verses, {Renumbered} of them through the address the loaded " +
        $"file prints, {Joined} together with a neighbour the editions divide differently; {Refused} " +
        $"verses refused and {Unused} edition verses no verse took; {TaggedWords} words given a number";
}

/// <summary>
/// Lays the tagged Synodal onto the Synodal the corpus already holds, which is the same translation
/// in a different digitisation and a different numbering.
///
/// <para>
/// **The numbering is settled by what the loaded file says about itself.** bible4u renumbered its
/// Synodal to the King James's verses and printed the edition's own address wherever the two
/// differ — <c>(22-1)</c> at the head of Psalm 23:1 — and <c>stated_verse_number</c> keeps
/// those. So a tagged verse goes to the corpus verse that prints its address, and where nothing
/// prints one, to the verse at the same address. A superscription the Synodal numbers as a verse of
/// its own sits in front of a marker naming verse two, and goes to the verse that marker opens.
/// Nothing here consults a versification table: the loaded file is the witness to its own
/// renumbering, and measured over the whole Bible it places every tagged verse.
/// </para>
///
/// <para>
/// **Then the words have to agree**, the way the two printings of the King James have to
/// (<see cref="TaggedEdition"/>): four fifths of the verse written the same, the rest aligned on the
/// letters. Where a verse does not — Isaiah 3:19–26, where one edition moves a line across a verse
/// boundary and every verse after it is one off, or Acts 19:40, which the tagged edition does not
/// divide — the run is laid as one stretch with its neighbours, because a word's number does not
/// depend on which verse the word is printed in. What still does not agree is refused.
/// </para>
/// </summary>
internal static class SynodalStrongLayer
{
    /// <summary>
    /// How much of a corpus verse the tagged edition has to write the same way before the two are
    /// the same verse. The King James's two printings are held to the same share.
    /// </summary>
    public const double SameVerse = 0.8;

    /// <param name="corpus">The corpus's verses, in canonical order.</param>
    public static LaidEdition Lay(
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), List<EditionWord>> edition,
        IReadOnlyList<CorpusVerse> corpus)
    {
        var claims = Claims(corpus);
        var assigned = new List<(int Book, int Chapter, int Verse)>[corpus.Count];
        for (var i = 0; i < corpus.Count; i++)
        {
            assigned[i] = [];
        }

        var unused = 0;
        foreach (var address in edition.Keys)
        {
            if (Place(claims, corpus, address) is { } at)
            {
                assigned[at].Add(address);
            }
            else
            {
                unused++;
            }
        }

        var agrees = new bool[corpus.Count];
        var laid = new List<EditionSpan>?[corpus.Count];
        for (var i = 0; i < corpus.Count; i++)
        {
            var words = Words(edition, assigned[i]);
            var spans = TaggedEdition.Align([.. words.Select(w => w.Text)], [.. corpus[i].Words.Select(w => w.Text)]);
            agrees[i] = corpus[i].Words.Count > 0
                        && TaggedEdition.Agreement(spans, corpus[i].Words.Count) >= SameVerse;
            laid[i] = agrees[i] ? spans : null;
        }

        var tags = new Dictionary<long, WordTag>(600_000);
        var inStretch = new bool[corpus.Count];
        int verses = 0, renumbered = 0, joined = 0, refused = 0;

        for (var i = 0; i < corpus.Count; i++)
        {
            if (agrees[i] || inStretch[i])
            {
                continue;
            }

            var (from, to) = Stretch(corpus, agrees, inStretch, i);
            var stretch = Enumerable.Range(from, to - from + 1).ToList();
            var editionWords = Words(edition, stretch.SelectMany(at => assigned[at]).Order());
            var corpusWords = stretch.SelectMany(at => corpus[at].Words).ToList();
            var spans = TaggedEdition.Align(
                [.. editionWords.Select(w => w.Text)], [.. corpusWords.Select(w => w.Text)]);

            if (to > from && TaggedEdition.Agreement(spans, corpusWords.Count) >= SameVerse)
            {
                Project(tags, editionWords, corpusWords, spans);
                foreach (var at in stretch)
                {
                    inStretch[at] = true;
                }

                verses += stretch.Count;
                joined += stretch.Count;
                renumbered += stretch.Count(at => Renumbered(corpus[at], assigned[at]));
            }

            i = to;
        }

        for (var i = 0; i < corpus.Count; i++)
        {
            if (inStretch[i])
            {
                continue;
            }

            if (laid[i] is { } own)
            {
                Project(tags, Words(edition, assigned[i]), corpus[i].Words, own);
                verses++;
                if (Renumbered(corpus[i], assigned[i]))
                {
                    renumbered++;
                }
            }
            else if (corpus[i].Words.Count > 0)
            {
                refused++;
            }
        }

        return new LaidEdition(tags, verses, renumbered, joined, refused, unused, tags.Count);
    }

    /// <summary>
    /// Which corpus verse takes each Synodal address: first every address a verse prints, then every
    /// verse that prints none at its own. The printed ones go first because a verse that prints no
    /// address is silent rather than agreeing, and its own address may be one another verse prints.
    /// </summary>
    private static Dictionary<(int, int, int), int> Claims(IReadOnlyList<CorpusVerse> corpus)
    {
        var claims = new Dictionary<(int, int, int), int>(corpus.Count + 3_000);
        for (var i = 0; i < corpus.Count; i++)
        {
            foreach (var stated in corpus[i].Stated)
            {
                claims.TryAdd((corpus[i].Book, stated.Chapter, stated.Number), i);
            }
        }

        for (var i = 0; i < corpus.Count; i++)
        {
            if (corpus[i].Stated.Count == 0)
            {
                claims.TryAdd((corpus[i].Book, corpus[i].Chapter, corpus[i].Verse), i);
            }
        }

        return claims;
    }

    /// <summary>
    /// The corpus verse a tagged verse belongs to. A superscription the Synodal numbers as verse one
    /// is claimed by nothing, because the verse holding it prints only the marker for verse two that
    /// follows it — so an unclaimed address goes to the verse whose first printed address is the next
    /// one.
    /// </summary>
    private static int? Place(
        Dictionary<(int, int, int), int> claims,
        IReadOnlyList<CorpusVerse> corpus,
        (int Book, int Chapter, int Verse) address)
    {
        if (claims.TryGetValue(address, out var at))
        {
            return at;
        }

        var next = (address.Book, address.Chapter, address.Verse + 1);
        return claims.TryGetValue(next, out var following)
               && corpus[following].Stated is [var first, ..]
               && first.Chapter == address.Chapter
               && first.Number == address.Verse + 1
            ? following
            : null;
    }

    /// <summary>
    /// The run of verses around one that does not agree: every disagreeing verse next to it in the
    /// same book, and one agreeing neighbour on each side, because the words a disagreeing verse is
    /// missing are in the verse before it or the verse after.
    /// </summary>
    private static (int From, int To) Stretch(
        IReadOnlyList<CorpusVerse> corpus,
        bool[] agrees,
        bool[] taken,
        int at)
    {
        var from = at;
        var to = at;
        while (to + 1 < corpus.Count && corpus[to + 1].Book == corpus[at].Book && !agrees[to + 1])
        {
            to++;
        }

        if (from > 0 && corpus[from - 1].Book == corpus[at].Book && !taken[from - 1])
        {
            from--;
        }

        if (to + 1 < corpus.Count && corpus[to + 1].Book == corpus[at].Book)
        {
            to++;
        }

        return (from, to);
    }

    private static bool Renumbered(CorpusVerse verse, List<(int Book, int Chapter, int Verse)> assigned) =>
        assigned.Any(address => address.Chapter != verse.Chapter || address.Verse != verse.Verse);

    private static List<EditionWord> Words(
        IReadOnlyDictionary<(int, int, int), List<EditionWord>> edition,
        IEnumerable<(int, int, int)> addresses) =>
        [.. addresses.SelectMany(address => edition[address])];

    /// <summary>
    /// The numbers onto the corpus words, span by span. Where a span joins several tagged words to
    /// one corpus word, the word takes the first number the span writes, as the King James's
    /// printings do; where it joins one tagged word to several corpus words, each of them takes it,
    /// and they stay one unit.
    /// </summary>
    private static void Project(
        Dictionary<long, WordTag> tags,
        IReadOnlyList<EditionWord> edition,
        IReadOnlyList<CorpusWord> corpus,
        List<EditionSpan> spans)
    {
        foreach (var span in spans)
        {
            EditionWord? numbered = null;
            for (var i = span.TaggedFrom; i < span.TaggedTo && numbered is null; i++)
            {
                if (edition[i].Numbers.Count > 0)
                {
                    numbered = edition[i];
                }
            }

            if (numbered is null)
            {
                continue;
            }

            for (var i = span.CorpusFrom; i < span.CorpusTo; i++)
            {
                tags[corpus[i].Id] = new WordTag(numbered.Numbers, numbered.Unit);
            }
        }
    }
}
