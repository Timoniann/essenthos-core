namespace Essenthos.Core.Loading.Links;

/// <summary>What joined the two sides, which is also how strongly they are joined.</summary>
internal enum StrongMatchKind
{
    /// <summary>
    /// One word of the translation, and the witness writes each number its tag names exactly once.
    /// </summary>
    Unambiguous,

    /// <summary>
    /// The same number as many times on one side as the other, paired in the order both texts write
    /// them.
    /// </summary>
    Paired,

    /// <summary>More than one candidate on a side, so which pairs with which is a guess.</summary>
    Contended,
}

/// <param name="Unmatched">
/// Tagged words whose numbers no witness word in the verse carries and that the dictionary could
/// not send anywhere either. Counted and never written: the translation may be rendering a longer
/// text than this corpus holds, or the match may simply have failed, and nothing here can tell
/// those apart.
/// </param>
/// <param name="Resolved">
/// Words matched through the lemma the dictionary names for their form rather than through the
/// number both sides write.
/// </param>
/// <param name="Phrases">
/// Words whose tag names more than one number — one word of the translation standing over a phrase
/// of the witness.
/// </param>
internal readonly record struct StrongMatchTally(int Unmatched, int Resolved, int Phrases);

/// <param name="Confidence">
/// How sure the pairing is. Never null: a Strong number is a lemma and not a token, so every match
/// built here is an inference and has to carry one.
/// </param>
internal sealed record StrongMatchDraft(
    List<long> From,
    List<long> To,
    double Confidence,
    StrongMatchKind Kind);

/// <summary>
/// One translated word and one witness word carry the same Strong number inside one verse,
/// therefore they correspond — the inference two texts in this corpus reach the originals by, and
/// the only one that rests on a number somebody printed rather than on a model.
///
/// <para>
/// **A Strong number is a lemma, not a token.** A verse using one lexeme twice does not say which
/// occurrence a word renders, and the confidence ladder below is entirely about how much of that
/// ambiguity is left after the counting. Nothing here is <c>stated-by-source</c>: what the source
/// states is the number, and the correspondence is ours.
/// </para>
///
/// <para>
/// It is one file because it is one method used twice. The King James reaches the Greek through the
/// Zefania KJV+ tags, which arrive in a separate edition and have to be laid onto the loaded words
/// first (<see cref="NewTestamentLinkLoader"/>); Luther 1912 carries its tags in the file the text
/// itself came from, so its numbers are already on the corpus's own words
/// (<see cref="TaggedTextLinkLoader"/>). Everything between those two ends — grouping by the
/// effective number, pairing repeated numbers in order, and what each shape is worth — is the same
/// argument, and two copies of it would be two ladders to keep in step.
/// </para>
/// </summary>
internal static class StrongNumberMatch
{
    /// <summary>
    /// One word on each side and the verse writes the number once. The correspondence is still
    /// inferred — the number is right and the occurrence could still be another — so it is high
    /// rather than certain.
    /// </summary>
    public const double Unambiguous = 0.9;

    /// <summary>
    /// The same number as many times on one side as the other, paired in the order both texts write
    /// them. It is an assumption on top of an inference, so it sits below an unambiguous match —
    /// but well above a set naming every candidate, because for a word repeated identically any
    /// bijection reads the same to a reader and order is the one both texts agree on.
    /// </summary>
    public const double PairedInOrder = 0.7;

    /// <summary>One side has more than one candidate, so which pairs with which is a guess.</summary>
    public const double OneSideContended = 0.5;

    public const double BothSidesContended = 0.3;

    /// <summary>
    /// Deducted wherever the two numbers were joined by the dictionary rather than written on both
    /// sides. Every tier loses the same amount, because what the redirect adds is the same
    /// everywhere: one more inference between the link and the two texts that state it.
    /// </summary>
    public const double ResolvedNumber = 0.1;

    /// <summary>
    /// One link per effective set of Strong numbers per verse, naming every translated word that
    /// carries it and every witness word that does. Where several translated words render one
    /// witness word, that is one claim about a set, not several claims each pretending to be about
    /// a pair.
    ///
    /// The effective number is the tag's own where the witness writes it, and the one the dictionary
    /// resolves it to otherwise. Grouping on it rather than on the tag is what lets a verse's ἐστί
    /// and its εἰμί arrive at the same link instead of at two claiming the same word. A tag naming
    /// several numbers is grouped under all of them together, which keeps a phrase apart from the
    /// words that carry one of its numbers alone.
    ///
    /// The Hebrew object marker is the exception on both sides: a bare one is reached by nothing, and
    /// a tag naming it beside other numbers is matched on the others (<see cref="ObjectMarker"/>).
    /// </summary>
    /// <param name="tagged">
    /// The translation's words in verse order, each with the numbers its tag names. A word with none
    /// is passed in with an empty list rather than left out, so a caller can go on reading the nth
    /// entry as the nth word.
    /// </param>
    /// <param name="witness">The witness's words in verse order, each with the number it carries.</param>
    /// <param name="resolution">
    /// The redirects the dictionary offers and the corpus bears out, or empty where none were
    /// admitted. Hebrew has none: the derivations this reads are the Greek concordance's.
    /// </param>
    public static List<StrongMatchDraft> Verse(
        IReadOnlyList<TaggedWord> tagged,
        IReadOnlyList<WitnessWord> witness,
        IReadOnlyDictionary<string, NumberRedirect> resolution,
        out StrongMatchTally tally)
    {
        var byNumber = new Dictionary<string, List<long>>(witness.Count, StringComparer.Ordinal);
        foreach (var word in witness)
        {
            if (word.Strong is null || !ObjectMarker.Reachable(word.Strong, word.Suffixed))
            {
                continue;
            }

            if (!byNumber.TryGetValue(word.Strong, out var carrying))
            {
                carrying = [];
                byNumber[word.Strong] = carrying;
            }

            carrying.Add(word.Id);
        }

        var order = new List<string>(16);
        var groups = new Dictionary<string, Group>(16, StringComparer.Ordinal);
        int unmatched = 0, resolved = 0, phrases = 0;

        foreach (var word in tagged)
        {
            if (word.Numbers.Count == 0)
            {
                continue;
            }

            if (Reach(byNumber, resolution, ObjectMarker.Rendered(word.Numbers)) is not { } reached)
            {
                unmatched++;
                continue;
            }

            Collect(order, groups, reached, word.Id);
            if (reached.Resolved)
            {
                resolved++;
            }

            if (reached.Numbers > 1)
            {
                phrases++;
            }
        }

        tally = new StrongMatchTally(unmatched, resolved, phrases);
        return Drafts(order, groups);
    }

    private static List<StrongMatchDraft> Drafts(List<string> order, Dictionary<string, Group> groups)
    {
        var drafts = new List<StrongMatchDraft>(order.Count);
        foreach (var key in order)
        {
            var group = groups[key];

            // A set naming every candidate on both sides is a true claim and a useless one. Matthew
            // 1:4 has three "and" against three δέ, and one link naming all six makes the reader
            // light the whole verse when a single word is touched — which says the corpus cannot
            // tell them apart, when in fact both texts write them in the same order.
            //
            // Where the counts agree the words are paired in that order, one link each. Where they
            // do not, nothing here can choose, and the set stands. A phrase is never paired this
            // way: two translated words tagged with the same two witness words each render both of
            // them, and pairing them off would split one stated claim into two invented ones.
            if (group.Numbers == 1 && group.From.Count == group.To.Count && group.From.Count > 1)
            {
                for (var at = 0; at < group.From.Count; at++)
                {
                    drafts.Add(new StrongMatchDraft(
                        [group.From[at]],
                        [group.To[at]],
                        Lower(PairedInOrder, group.Resolved),
                        StrongMatchKind.Paired));
                }

                continue;
            }

            var settled = group.From.Count == 1 && group.To.Count == group.Numbers;
            drafts.Add(new StrongMatchDraft(
                group.From,
                group.To,
                Confidence(settled, group.From.Count, group.To.Count, group.Resolved),
                settled ? StrongMatchKind.Unambiguous : StrongMatchKind.Contended));
        }

        return drafts;
    }

    /// <summary>
    /// The witness words one tag's numbers reach, and the key they group under.
    ///
    /// A tag naming several numbers is the source stating a phrase — <c>1223 5124</c> for διὰ
    /// τοῦτο, written <em>therefore</em> — so the words of all of them together are one claim. A
    /// number this witness does not write is left out rather than sinking the rest: the editions
    /// differ, and what the translation still reaches is what the link should say.
    /// </summary>
    private static Reached? Reach(
        Dictionary<string, List<long>> byNumber,
        IReadOnlyDictionary<string, NumberRedirect> resolution,
        IReadOnlyList<string> numbers)
    {
        var keys = new List<string>(numbers.Count);
        var reached = new List<long>(numbers.Count);
        var taken = new HashSet<long>(numbers.Count);
        var resolved = false;

        foreach (var number in numbers)
        {
            string key;
            List<long> carrying;
            if (byNumber.TryGetValue(number, out var direct))
            {
                key = number;
                carrying = direct;
            }
            else if (resolution.TryGetValue(number, out var redirect)
                     && Together(byNumber, redirect.Numbers) is { } through)
            {
                key = string.Join('+', redirect.Numbers);
                carrying = through;
                resolved = true;
            }
            else
            {
                continue;
            }

            if (keys.Contains(key))
            {
                continue;
            }

            keys.Add(key);
            reached.AddRange(carrying.Where(taken.Add));
        }

        return keys.Count == 0 ? null : new Reached(string.Join('+', keys), reached, keys.Count, resolved);
    }

    /// <summary>
    /// The witness words carrying every one of these numbers, or null where the verse is missing any
    /// of them. A phrase entry — G3364 for οὐ μή — names two words and is a claim about both.
    /// </summary>
    private static List<long>? Together(
        Dictionary<string, List<long>> byNumber,
        IReadOnlyList<string> numbers)
    {
        var words = new List<long>(numbers.Count);
        foreach (var number in numbers)
        {
            if (!byNumber.TryGetValue(number, out var carrying))
            {
                return null;
            }

            words.AddRange(carrying);
        }

        return words;
    }

    private static void Collect(
        List<string> order,
        Dictionary<string, Group> groups,
        Reached reached,
        long word)
    {
        if (!groups.TryGetValue(reached.Key, out var group))
        {
            group = new Group(reached.To, reached.Numbers);
            groups[reached.Key] = group;
            order.Add(reached.Key);
        }

        group.From.Add(word);
        group.Resolved |= reached.Resolved;
    }

    /// <param name="settled">
    /// One translated word, and the verse writes each number its tag names exactly once. Which
    /// witness word answers which is then not a choice — the ordinary single number matched alone is
    /// the commonest case of it, and a two-number phrase whose words the verse writes once each is
    /// as settled as that.
    /// </param>
    private static double Confidence(bool settled, int from, int to, bool resolved) => Lower(
        (settled, from, to) switch
        {
            (true, _, _) => Unambiguous,
            (_, 1, _) or (_, _, 1) => OneSideContended,
            _ => BothSidesContended,
        },
        resolved);

    // Rounded because the column is read by people and 0.3 less 0.1 is 0.19999999999999998.
    private static double Lower(double confidence, bool resolved) =>
        resolved ? Math.Round(confidence - ResolvedNumber, 2) : confidence;

    /// <param name="Id">The translated word, as the corpus holds it.</param>
    /// <param name="Numbers">
    /// The Strong numbers its tag names, which is usually one, sometimes none, and sometimes several
    /// — one word standing over a phrase of the original.
    /// </param>
    internal readonly record struct TaggedWord(long Id, IReadOnlyList<string> Numbers);

    /// <param name="Suffixed">
    /// Whether the word carries a pronominal suffix, which is what makes an object marker a word a
    /// translation renders (<see cref="ObjectMarker"/>).
    /// </param>
    internal readonly record struct WitnessWord(long Id, string? Strong, bool Suffixed = false);

    /// <param name="To">Every witness word the tag's numbers name, in the order the numbers stand.</param>
    /// <param name="Numbers">
    /// How many of the tag's numbers this witness writes at all. Compared against the words found,
    /// it is what says whether each of them was written once or some of them several times.
    /// </param>
    private sealed record Reached(string Key, List<long> To, int Numbers, bool Resolved);

    /// <param name="Numbers">
    /// How many distinct Strong numbers this group stands on. One is the ordinary case; more is a
    /// phrase tag, and it is what says whether the witness words are occurrences of one number to
    /// choose between or the several words the source names together.
    /// </param>
    private sealed record Group(List<long> To, int Numbers)
    {
        public List<long> From { get; } = [];

        public bool Resolved { get; set; }
    }
}
