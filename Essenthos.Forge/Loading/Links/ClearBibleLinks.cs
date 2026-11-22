using System.Text.RegularExpressions;
using Essenthos.Core.ClearBible;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Corpus;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Links;

/// <param name="SourceWords">Their source words this corpus could name a word of its own for.</param>
/// <param name="SourceTotal">Their source words altogether.</param>
/// <param name="Verses">Verses the two sides could be lined up in at all.</param>
/// <param name="Refused">Verses where they could not, so every record in them is unresolved.</param>
/// <param name="Realigned">
/// Source verses where some word of theirs is not our word of the same number: a word one edition
/// has and the other has not, and every word after it. Each is a verse counting positions would have
/// joined wrongly.
/// </param>
internal readonly record struct ClearBiblePlacement(
    int SourceWords,
    int SourceTotal,
    int TargetWords,
    int TargetTotal,
    int Verses,
    int Refused,
    int Realigned)
{
    public override string ToString() =>
        $"{SourceWords} of {SourceTotal} source words and {TargetWords} of {TargetTotal} target words " +
        $"were placed, over {Verses} verses, {Refused} of which would not line up and {Realigned} of " +
        "whose source words do not all stand at the number the file gives them";
}

/// <summary>One side of a Clear Bible set as its join reads it.</summary>
/// <param name="Words">
/// Its words by verse: the verse it prints them in, or, for a source joined edition against edition, the frame address
/// its verse stands at first. Null for a translation the join numbers itself (<paramref name="InOrder"/>).
/// </param>
/// <param name="InOrder">Every word with its printed verse, for a translation whose records number its own words.</param>
/// <param name="Frame">
/// Every frame address each printed verse stands at, both written as <see cref="ClearBibleAlignment.Verse(string)"/> writes
/// them; null for a source joined edition against edition.
/// </param>
internal sealed record ClearBibleSide(
    string Language,
    IReadOnlyDictionary<(int, int, int), List<ClearBibleLinks.Word>>? Words,
    IReadOnlyList<ClearBibleLinks.PrintedWord>? InOrder,
    IReadOnlyDictionary<int, HashSet<int>>? Frame);

/// <summary>A set's drafts, and its records left out, counted by why.</summary>
internal sealed record ClearBibleJoined(
    List<ClearBibleLinks.Draft> Drafts,
    int Records,
    int Unresolved,
    int WithoutCounterpart,
    int Astray,
    int Shifted,
    int Moved,
    int Misnumbered,
    int Overruled,
    int Trimmed,
    ClearBiblePlacement Placed,
    int Edited = 0,
    int Renumbered = 0,
    int Unplaced = 0,
    int Crossed = 0,
    int MovedOntoWord = 0,
    int Slipped = 0);

/// <summary>
/// Clear Bible's hand-made alignments: a second opinion on the Berean, and the only opinion anybody
/// has published on the Reina-Valera.
///
/// <para>
/// **The Berean.** Its own publisher states which English word renders which Greek word, and that is
/// loaded. Clear Bible's team answered the same question about the same translation, without
/// consulting them. Measured before this was written, over the 7,925 verses where the two
/// tokenise the text identically: of 115,016 Greek words both name, **96.6% get exactly the same
/// English words**, 1.1% overlap, 2.3% share none. So this mostly writes nothing: where the two
/// agree it adds a claim to the link that is already there, and the link's own method and source do
/// not change — the Berean stated it first and still states it. What changes is that the link can
/// now say two people arrived at it, which is the cheapest evidence this corpus has and the thing
/// it threw away for as long as a method that spoke second was simply not recorded.
/// </para>
///
/// <para>
/// **The Reina-Valera.** Nothing else states a single Spanish correspondence. `process = "manual"`,
/// CC BY 4.0, whole Bible, and keyed by eBible identifier to the exact file this corpus loads. It
/// is <c>stated-by-source</c> and carries no confidence, which is the strongest thing this corpus
/// can say about a pair of words and is said here because a person made the claim, not because the
/// claim is beyond question — <see cref="ClearBibleSet"/> records how the identity was checked, and
/// the same repository's Russian set, believed on its label, turned out to name a punctuation mark
/// as the Russian word in 12,550 of its 89,248 records and to drift further with every one after.
/// </para>
///
/// <para>
/// Where two sources disagree, both answers are kept. A second link naming different words is not a
/// duplicate and does not trip the check that catches those — that check is about two links naming
/// *the same* words, which is agreement stored as rivalry. Two people disagreeing about which word
/// renders which is a fact about translation, and the corpus should hold it rather than pick.
/// </para>
///
/// <para>
/// Their Russian set is in the same download and is not loaded by anything: its records do not
/// correspond to the token file shipped beside them.
/// </para>
/// </summary>
internal static partial class ClearBibleLinks
{
    /// <summary>
    /// How much of a verse the two editions have to write the same way before their words are put in
    /// step at all. Below it the verse is refused whole and every record in it is unresolved: an
    /// alignment laid on a verse the two sides do not share is a claim about the wrong words, and it
    /// would look exactly like the correct ones.
    /// </summary>
    private const double SameVerse = 0.5;

    /// <summary>
    /// What a set says about the two texts' words, as drafts of links, with every record it could not resolve counted by
    /// why. It reads only the set's files and the words it is handed, so the database's load and a producer of method files
    /// make the same join.
    /// </summary>
    internal static ClearBibleJoined Join(
        string directory,
        ClearBibleSet set,
        ClearBibleSide translation,
        ClearBibleSide witness,
        IReadOnlySet<long> overruled,
        StrongNumberLists lists,
        IReadOnlyList<ClearBibleSourceEdits.Edition>? editions = null)
    {
        var alignment = Path.Combine(directory, set.Alignment);
        var target = Path.Combine(directory, set.Target);
        var source = Path.Combine(directory, set.Source);

        var placement = new Placement();
        List<ClearBibleToken> tokens;
        Dictionary<string, List<long>> theirTarget;
        if (translation.InOrder is { } inOrder)
        {
            (tokens, theirTarget) = Retokenised(inOrder, placement.Target);
        }
        else
        {
            tokens = [.. ClearBibleAlignment.Tokens(target)];
            theirTarget = Placed(lists, 
                ClearBibleJoin.Letters, translation, tokens, ClearBibleAlignment.Word, placement.Target);
        }

        var sourceTokens = ClearBibleAlignment.Tokens(source).ToList();
        var theirSource = Placed(lists, set.Join, witness, sourceTokens, ClearBibleAlignment.Unit, placement.Source);

        var renders = Renders(tokens);
        var all = ClearBibleAlignment.Records(alignment).ToList();
        var edits = set.Edited ? ClearBibleSourceEdits.Of(all, sourceTokens, tokens, editions ?? [], witness.Language) : null;
        var shift = set.Shift is { } and
            ? new Shift(Ands(tokens, and.And), Joining(source, and.Also), Named(all))
            : null;
        var early = set.Early is { } before ? EarlyNames.Of(tokens, before, source, all) : null;
        var misnumbered = set.Numbering switch
        {
            ClearBibleNumbering.Retokenised or ClearBibleNumbering.Redivided => Misnumbered(all, tokens),
            ClearBibleNumbering.Revised => [.. Misnumbered(all, tokens), .. Unglossed(all, tokens, sourceTokens)],
            _ => [],
        };

        var drafts = new List<Draft>();
        int records = 0, unresolved = 0, withoutCounterpart = 0;
        int astray = 0, shifted = 0, moved = 0, refused = 0, overruledRecords = 0, trimmed = 0, renumbered = 0, unplaced = 0;
        int movedOntoWord = 0, slipped = 0;

        foreach (var stated in all)
        {
            var record = stated;
            records++;
            if (edits is not null)
            {
                var sources = Edited(record.Source, edits.Placed);
                if (sources is null)
                {
                    unplaced++;
                    continue;
                }

                if (!ReferenceEquals(sources, record.Source))
                {
                    record = record with { Source = sources };
                    renumbered++;
                }
            }

            if (record.Target.Any(id => misnumbered.Contains(ClearBibleAlignment.Verse(id) ?? 0)))
            {
                refused++;
                continue;
            }

            if (Astray(record, renders, translation.Frame!, witness.Frame))
            {
                astray++;
                continue;
            }

            if (shift is not null && Shifted(record, shift.Ands, shift.Joining, shift.Named))
            {
                shifted++;
                continue;
            }

            var named = early is not null ? Early(record, early) : null;
            var move = Move.None;
            if (named is not null)
            {
                move = Move.Name;
                moved++;
            }
            else if (early is not null)
            {
                var (slip, word) = Slipped(record, early);
                if (slip == Slip.Refused)
                {
                    slipped++;
                    continue;
                }

                if (slip == Slip.Moved)
                {
                    named = word;
                    move = Move.Word;
                    movedOntoWord++;
                }
            }

            if (named is not null)
            {
                record = record with { Target = [named] };
            }

            var ours = Ours(record.Source, theirSource, ClearBibleAlignment.Unit);
            var theirs = Ours(record.Target, theirTarget, ClearBibleAlignment.Word);

            if (ours.Count == 0 || theirs.Count == 0)
            {
                unresolved++;
                if (record.Source.Any(id => placement.Source.WithoutCounterpart.Contains(ClearBibleAlignment.Unit(id))))
                {
                    withoutCounterpart++;
                }

                continue;
            }

            var (kept, lost) = Trim(ours, overruled);
            if (lost)
            {
                overruledRecords++;
                if (!kept)
                {
                    continue;
                }

                trimmed++;
            }

            drafts.Add(new Draft(theirs, ours, move));
        }

        return new ClearBibleJoined(
            drafts, records, unresolved, withoutCounterpart, astray, shifted, moved, refused, overruledRecords, trimmed,
            placement.Read(), edits?.Verses ?? 0, renumbered, unplaced, edits?.Crossed ?? 0, movedOntoWord, slipped);
    }

    /// <summary>
    /// A record's witness words without the ones a ruling sets aside (<c>mapping/&lt;pair&gt;/rulings</c>): whether it still
    /// names any, and whether it lost one.
    /// </summary>
    internal static (bool Kept, bool Trimmed) Trim(List<long> witness, IReadOnlySet<long> overruled)
    {
        if (overruled.Count == 0 || witness.RemoveAll(overruled.Contains) == 0)
        {
            return (true, false);
        }

        return (witness.Count > 0, true);
    }

    /// <summary>
    /// A record's source words as the edited numbering places them (<see cref="ClearBibleSourceEdits"/>): the same list
    /// where none moves, another where some do, or null where any is placed on nothing.
    /// </summary>
    internal static IReadOnlyList<string>? Edited(IReadOnlyList<string> source, IReadOnlyDictionary<string, string?> placed)
    {
        List<string>? edited = null;
        for (var index = 0; index < source.Count; index++)
        {
            if (!placed.TryGetValue(ClearBibleAlignment.Unit(source[index]), out var unit))
            {
                edited?.Add(source[index]);
                continue;
            }

            if (unit is null)
            {
                return null;
            }

            edited ??= [.. source.Take(index)];
            if (!edited.Contains(unit))
            {
                edited.Add(unit);
            }
        }

        return edited ?? source;
    }

    /// <summary>
    /// Whether a record pairs the translation's verse with the source verse of the same number where
    /// the two number it differently. That is the one way these hand-made alignments go wrong
    /// verse-wide: the Reina-Valera's Numbers 13:19, which the file itself and the frame both say
    /// renders the Hebrew 13:18, has its words linked to the Hebrew 13:19, and in the chapters where
    /// the Spanish runs a verse behind the English its words are linked to the Hebrew verse the
    /// English numbers alike.
    ///
    /// All three have to hold. The file's list of the verses a token renders is kept verse by verse,
    /// so a Spanish verse that opens with the last words of the Hebrew verse before it is listed
    /// against the next one and its correct links would look astray; and it is keyed to one edition
    /// where the alignment may be to another, as the Berean's Greek numbers Acts 19:41 where the list
    /// does not. The frame is this project's reading of the versification data. Where the file and
    /// the frame both put the verses apart and the record's source verse is merely the one carrying
    /// the translation verse's own number, printed or placed, the record is the odd one out. A record
    /// either of them says nothing about is kept.
    /// </summary>
    internal static bool Astray(
        ClearBibleRecord record,
        IReadOnlyDictionary<string, (int First, int Last)> renders,
        IReadOnlyDictionary<int, HashSet<int>> targetFrame,
        IReadOnlyDictionary<int, HashSet<int>>? sourceFrame)
    {
        var sources = record.Source.Select(ClearBibleAlignment.Verse).OfType<int>().ToHashSet();
        var own = record.Target.Select(ClearBibleAlignment.Verse).OfType<int>().ToHashSet();
        var said = new List<(int First, int Last)>(record.Target.Count);
        foreach (var id in record.Target)
        {
            if (!renders.TryGetValue(ClearBibleAlignment.Word(id), out var range))
            {
                return false;
            }

            said.Add(range);
        }

        if (sources.Count == 0 || said.Count == 0 ||
            sources.Any(verse => said.Any(range => range.First <= verse && verse <= range.Last)))
        {
            return false;
        }

        var here = Canonical(own, targetFrame);
        var there = sourceFrame is null ? sources : Canonical(sources, sourceFrame);
        return here.Count > 0 && there.Count > 0 && !here.Overlaps(there)
               && (sources.Overlaps(own) || there.Overlaps(own));
    }

    /// <summary>
    /// Whether a record puts a source word on the translation's <em>and</em> where the word after the
    /// <em>and</em> renders it. The Reina-Valera's set does this about five thousand times in the Old
    /// Testament: Genesis 1:4's וַיַּרְא, <em>and he saw</em>, is on the <em>Y</em> of <em>Y vió</em>,
    /// and <em>vió</em> is on nothing. The set leaves the Hebrew ו unaligned almost everywhere, so
    /// this is not a ו misread as the verb; it is the verb, the noun or the particle after it put one
    /// word too early.
    ///
    /// The <em>and</em> has to be the record's only target word, no source word may be a conjunction
    /// or a particle the translation renders with <em>and</em>, and the word after the <em>and</em>
    /// has to be named by no record. The record is refused rather than moved: which of the words
    /// after the <em>and</em> renders the source word is a question the record does not answer — for
    /// a verb it is mostly the next word, for a noun often the one after the article — and an answer
    /// this loader picked would be stored as the set's.
    /// </summary>
    internal static bool Shifted(
        ClearBibleRecord record,
        IReadOnlyDictionary<string, string?> ands,
        IReadOnlySet<string> joining,
        IReadOnlySet<string> named) =>
        record is { Target.Count: 1, Source.Count: > 0 }
        && ands.TryGetValue(ClearBibleAlignment.Word(record.Target[0]), out var next)
        && next is not null
        && !named.Contains(next)
        && !record.Source.Any(id => joining.Contains(ClearBibleAlignment.Unit(id)));

    /// <summary>
    /// The name a record puts on the preposition or article before it, where it does, or null. The Reina-Valera's
    /// set gives some four hundred names to the word before them and names the name with nothing: Genesis 14:3's
    /// שִׂדִּים is on the <em>de</em> of <em>valle de Siddim</em>, Jeremiah 24:1's Jeconiah on the <em>á</em> of
    /// <em>á Jechônías</em>. It is <see cref="Shifted"/>'s slip before a name instead of before a verb.
    ///
    /// The record's only target word has to be a preposition or an article, the word after it in its verse has to be
    /// written as a name (a capital, then a small letter) and named by no record, every source word has to be a noun,
    /// a verb or an adjective, never the preposition, particle or pronoun the Spanish word could render itself, and
    /// every source word has to be that name: its letters are the name's (<see cref="NameLists.LeastLikeness"/>), or
    /// the set pairs the same Spanish word with the same Strong number elsewhere (<em>Dios</em> and אֱלֹהִים,
    /// <em>Siros</em> and אֲרָמִים). Without that last mark a record on a <em>de</em> before a name can be a slip of
    /// another kind: 2 Kings 16:6's הֵשִׁיב, <em>restored</em>, is on the <em>de</em> of <em>rey de Siria</em>, and
    /// <em>restituyó</em> renders it.
    ///
    /// Here the record is moved onto the name rather than refused, as the <em>and</em> is: a preposition does not render
    /// a noun, and the word the record names is the name beside it by its letters or by the set's own pairing, so
    /// which word the record meant is not in doubt. The rows say they were moved.
    /// </summary>
    internal static string? Early(ClearBibleRecord record, EarlyNames early) =>
        record is { Target.Count: 1, Source.Count: > 0 }
        && early.Befores.TryGetValue(ClearBibleAlignment.Word(record.Target[0]), out var name)
        && !early.Named.Contains(name)
        && !early.Repeated.Contains(name)
        && record.Source.All(id =>
            !early.RepeatedSources.Contains(ClearBibleAlignment.Unit(id))
            && early.Content.TryGetValue(ClearBibleAlignment.Unit(id), out var word) && early.Names(word, name))
            ? name
            : null;

    /// <summary>
    /// What a record that puts a noun, a verb or an adjective on a preposition or an article says of the word after it, when
    /// that word is no name and no record names it: <see cref="Early"/>'s slip before a common word. The Reina-Valera's set
    /// does this some five thousand times in the Old Testament and some nine hundred in the New: Ezekiel 28:13's אֹדֶם is on
    /// the <em>el</em> of <em>el sardio</em> and <em>sardio</em> is on nothing; 2 Chronicles 1:11's נֶפֶשׁ on the <em>el</em>
    /// of <em>el alma</em>; and with an article between, Judges 3:13's תְּמָרִים on the <em>de</em> of <em>ciudad de las
    /// palmas</em>.
    ///
    /// The word the record means is the first after it in the verse that is neither (<see cref="Aheads"/>). A common word has
    /// no letters to confirm it as a name has, so the record is moved onto it only where the set itself renders the source
    /// word's Strong number with that word elsewhere, in a record naming that word alone, and in at least
    /// <see cref="LeastShare"/> of the records naming that word alone: <em>sardio</em> with אֹדֶם, <em>gentes</em> with
    /// גּוֹי. The share keeps out the determiners and pronouns the set gives a noun now and then (<em>su</em>, <em>un</em>),
    /// which would otherwise pass as its rendering. A word or a source word that stands twice in its verse is not moved onto,
    /// as for names. Any other record of this shape is refused: the preposition or article does not render the noun, verb or
    /// adjective, and which word does is not certain enough to be stored as the set's. Job 3:12's שָּׁדַיִם on the
    /// <em>las</em> of <em>las tetas</em> is one: no other record names <em>tetas</em> alone.
    /// </summary>
    internal static (Slip Slip, string? Word) Slipped(ClearBibleRecord record, EarlyNames early)
    {
        if (record is not { Target.Count: 1, Source.Count: > 0 }
            || !early.Aheads.TryGetValue(ClearBibleAlignment.Word(record.Target[0]), out var word)
            || early.Named.Contains(word)
            || !record.Source.All(id => early.Content.ContainsKey(ClearBibleAlignment.Unit(id))))
        {
            return (Slip.None, null);
        }

        var placed = !early.Repeated.Contains(word)
                     && record.Source.All(id =>
                         !early.RepeatedSources.Contains(ClearBibleAlignment.Unit(id))
                         && early.Renders(early.Content[ClearBibleAlignment.Unit(id)], word));
        return placed ? (Slip.Moved, word) : (Slip.Refused, null);
    }

    /// <summary>
    /// The least share of the records naming a word alone that must pair it with a source word's Strong number before
    /// <see cref="Slipped"/> moves a record onto it.
    /// </summary>
    internal const double LeastShare = 0.05;

    /// <summary>What <see cref="Slipped"/> does with a record.</summary>
    internal enum Slip
    {
        /// <summary>Not the slip: left as it is.</summary>
        None,

        /// <summary>Moved onto the word after the preposition or article.</summary>
        Moved,

        /// <summary>The slip, with no word it can be moved onto with confidence: refused.</summary>
        Refused,
    }

    /// <summary>Which word a record was moved onto from the preposition or article before it.</summary>
    internal enum Move
    {
        /// <summary>None: the record names the words it named.</summary>
        None,

        /// <summary>A name (<see cref="Early"/>).</summary>
        Name,

        /// <summary>A common word (<see cref="Slipped"/>).</summary>
        Word,
    }

    /// <summary>
    /// The translation's prepositions and articles in <paramref name="before"/>, each with the first word after it in its verse,
    /// punctuation left out, that is not one of them, skipping one at most (<em>de las palmas</em>), where that word is not
    /// written as a name (a name is <see cref="Befores"/>' to give).
    /// </summary>
    internal static Dictionary<string, string> Aheads(IEnumerable<ClearBibleToken> tokens, IReadOnlySet<string> before)
    {
        var words = tokens.Where(token => !token.Excluded).ToList();
        var aheads = new Dictionary<string, string>(StringComparer.Ordinal);
        bool Before(int i) => before.Contains(words[i].Text.ToLowerInvariant());
        int? Verse(int i) => ClearBibleAlignment.Verse(ClearBibleAlignment.Word(words[i].Id));
        for (var i = 0; i < words.Count; i++)
        {
            if (!Before(i))
            {
                continue;
            }

            var next = i + 1;
            if (next < words.Count && Verse(next) == Verse(i) && Before(next))
            {
                next++;
            }

            if (next < words.Count && Verse(next) == Verse(i) && !Before(next) && !WrittenAsName(words[next].Text))
            {
                aheads[ClearBibleAlignment.Word(words[i].Id)] = ClearBibleAlignment.Word(words[next].Id);
            }
        }

        return aheads;
    }

    /// <summary>
    /// The translation's prepositions and articles in <paramref name="before"/> whose next word in the verse,
    /// punctuation left out, is written as a name, each with that word.
    /// </summary>
    internal static Dictionary<string, string> Befores(IEnumerable<ClearBibleToken> tokens, IReadOnlySet<string> before)
    {
        var befores = new Dictionary<string, string>(StringComparer.Ordinal);
        string? pending = null;
        int? verse = null;
        foreach (var token in tokens)
        {
            if (token.Excluded)
            {
                continue;
            }

            var id = ClearBibleAlignment.Word(token.Id);
            var here = ClearBibleAlignment.Verse(id);
            if (pending is not null && here == verse && WrittenAsName(token.Text))
            {
                befores[pending] = id;
            }

            pending = before.Contains(token.Text.ToLowerInvariant()) ? id : null;
            verse = here;
        }

        return befores;
    }

    /// <summary>A capital, then a small letter: <em>Siddim</em>, <em>No</em>, not <em>LOS</em> of an inscription.</summary>
    private static bool WrittenAsName(string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && text.Skip(1).Any(char.IsLower);

    /// <summary>The source edition's nouns, verbs and adjectives, whichever way the file spells the part, by unit.</summary>
    internal static Dictionary<string, ClearBibleToken> Content(string tokens) =>
        ClearBibleAlignment.Tokens(tokens)
            .Where(token => token.Part is "noun" or "verb" or "adjective" or "adj")
            .ToDictionary(token => ClearBibleAlignment.Unit(token.Id), StringComparer.Ordinal);

    /// <summary>
    /// The translation's tokens that are its word for <em>and</em>, each with the token after it in
    /// its verse, punctuation left out, or null where it ends the verse.
    /// </summary>
    internal static Dictionary<string, string?> Ands(string tokens, IReadOnlySet<string> and) =>
        Ands(ClearBibleAlignment.Tokens(tokens), and);

    /// <inheritdoc cref="Ands(string, IReadOnlySet{string})"/>
    internal static Dictionary<string, string?> Ands(IEnumerable<ClearBibleToken> tokens, IReadOnlySet<string> and)
    {
        var ands = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? pending = null;
        int? verse = null;
        foreach (var token in tokens)
        {
            if (token.Excluded)
            {
                continue;
            }

            var id = ClearBibleAlignment.Word(token.Id);
            var here = ClearBibleAlignment.Verse(id);
            if (pending is not null && here == verse)
            {
                ands[pending] = id;
            }

            pending = and.Contains(token.Text.ToLowerInvariant()) ? id : null;
            verse = here;
            if (pending is not null)
            {
                ands[pending] = null;
            }
        }

        return ands;
    }

    /// <summary>
    /// The source edition's words a translation's <em>and</em> can render: its conjunctions, and the
    /// particles whose Strong numbers are in <paramref name="also"/>.
    /// </summary>
    internal static HashSet<string> Joining(string tokens, IReadOnlySet<int> also)
    {
        var joining = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in ClearBibleAlignment.Tokens(tokens))
        {
            if (token.Part is "conjunction" or "conj" || (Lemma(token.Strong) is { } lemma && also.Contains(lemma)))
            {
                joining.Add(ClearBibleAlignment.Unit(token.Id));
            }
        }

        return joining;
    }

    /// <summary>A Strong number's number however the file writes it: <c>1571</c>, <c>0637a</c>, <c>G2532</c>.</summary>
    private static int? Lemma(string? strong) =>
        int.TryParse(strong.AsSpan().TrimStart("GHgh").TrimEnd("abcdefghijklmnopqrstuvwxyz"), out var number)
            ? number
            : null;

    internal static HashSet<string> Named(IEnumerable<ClearBibleRecord> records) =>
        records.SelectMany(record => record.Target).Select(ClearBibleAlignment.Word).ToHashSet(StringComparer.Ordinal);

    /// <param name="Ands">See <see cref="ClearBibleLinks.Ands"/>.</param>
    /// <param name="Joining">See <see cref="ClearBibleLinks.Joining"/>.</param>
    /// <param name="Named">Every target word some record names.</param>
    private sealed record Shift(
        Dictionary<string, string?> Ands,
        HashSet<string> Joining,
        HashSet<string> Named);

    /// <summary>What <see cref="Early"/> reads of a set.</summary>
    /// <param name="Befores">See <see cref="ClearBibleLinks.Befores"/>.</param>
    /// <param name="Content">See <see cref="ClearBibleLinks.Content"/>.</param>
    /// <param name="Named">Every target word some record names, before any is moved.</param>
    /// <param name="Texts">Every target word's text, folded as <see cref="Comparable"/> folds it, by word.</param>
    /// <param name="Pairs">Every target word's folded text with the Strong number of every source word a record pairs it with.</param>
    /// <param name="Aheads">See <see cref="ClearBibleLinks.Aheads"/>.</param>
    /// <param name="Alone">
    /// How many times the records that name one target word pair its folded text with a source word's Strong number.
    /// </param>
    /// <param name="AloneTotals">How many records name a target word of each folded text alone.</param>
    /// <param name="Repeated">The target words whose folded text stands more than once in their verse.</param>
    /// <param name="RepeatedSources">
    /// The source units whose Strong number (or, without one, whose letters) stands more than once in their verse,
    /// kept apart from <paramref name="Repeated"/> because a New Testament unit and a target word share the same
    /// eleven digits. Where a verse names a person or a place twice the set's records
    /// on it are often crossed — Nehemiah 11:21's second נְתִינִים is on the <em>los</em> before the first
    /// <em>Nethineos</em>, Ezra 6:12's second אֱלָהָא on the <em>el</em> before the first <em>Dios</em> — and a record
    /// moved there would join the wrong mention, outranking the aligner's right one.
    /// </param>
    internal sealed record EarlyNames(
        IReadOnlyDictionary<string, string> Befores,
        IReadOnlyDictionary<string, ClearBibleToken> Content,
        IReadOnlySet<string> Named,
        IReadOnlyDictionary<string, string> Texts,
        IReadOnlySet<(string Text, int Lemma)> Pairs,
        IReadOnlySet<string> Repeated,
        IReadOnlySet<string> RepeatedSources,
        IReadOnlyDictionary<string, string> Aheads,
        IReadOnlyDictionary<(string Text, int Lemma), int> Alone,
        IReadOnlyDictionary<string, int> AloneTotals)
    {
        public static EarlyNames Of(
            IReadOnlyList<ClearBibleToken> tokens, IReadOnlySet<string> before, string source, IReadOnlyList<ClearBibleRecord> records)
        {
            var strongs = new Dictionary<string, int>(StringComparer.Ordinal);
            var sourceKeys = new Dictionary<string, (int?, string)>(StringComparer.Ordinal);
            foreach (var token in ClearBibleAlignment.Tokens(source))
            {
                var unit = ClearBibleAlignment.Unit(token.Id);
                var lemma = Lemma(token.Strong);
                if (lemma is { } number)
                {
                    strongs[unit] = number;
                }

                sourceKeys[unit] = (
                    ClearBibleAlignment.Verse(unit),
                    lemma?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ClearBibleLinks.Comparable(token.Text, null));
            }

            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var targetKeys = new Dictionary<string, (int?, string)>(StringComparer.Ordinal);
            foreach (var token in tokens.Where(token => !token.Excluded))
            {
                var word = ClearBibleAlignment.Word(token.Id);
                texts[word] = ClearBibleLinks.Comparable(token.Text, null);
                targetKeys[word] = (ClearBibleAlignment.Verse(word), texts[word]);
            }

            var pairs = new HashSet<(string, int)>();
            var alone = new Dictionary<(string, int), int>();
            var aloneTotals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                var one = record.Target.Count == 1;
                foreach (var target in record.Target)
                {
                    if (!texts.TryGetValue(ClearBibleAlignment.Word(target), out var text))
                    {
                        continue;
                    }

                    if (one)
                    {
                        aloneTotals[text] = aloneTotals.GetValueOrDefault(text) + 1;
                    }

                    foreach (var unit in record.Source)
                    {
                        if (strongs.TryGetValue(ClearBibleAlignment.Unit(unit), out var lemma))
                        {
                            pairs.Add((text, lemma));
                            if (one)
                            {
                                alone[(text, lemma)] = alone.GetValueOrDefault((text, lemma)) + 1;
                            }
                        }
                    }
                }
            }

            return new EarlyNames(
                ClearBibleLinks.Befores(tokens, before), ClearBibleLinks.Content(source), ClearBibleLinks.Named(records), texts, pairs,
                Twice(targetKeys), Twice(sourceKeys), ClearBibleLinks.Aheads(tokens, before), alone, aloneTotals);
        }

        private static HashSet<string> Twice(Dictionary<string, (int?, string)> keys) =>
        [
            .. keys.GroupBy(pair => pair.Value).Where(group => group.Count() > 1).SelectMany(group => group.Select(pair => pair.Key)),
        ];

        /// <summary>Whether a source word is the translation's word: by its letters, or by the set's pairing elsewhere.</summary>
        public bool Names(ClearBibleToken word, string name)
        {
            if (!Texts.TryGetValue(name, out var text))
            {
                return false;
            }

            if (Lemma(word.Strong) is { } lemma && Pairs.Contains((text, lemma)))
            {
                return true;
            }

            var letters = NameLists.Skeleton(word.Text);
            return letters.Length > 0
                   && NameLetters.Likeness(NameLetters.Of(text, "spa"), letters, text) >= NameLists.LeastLikeness;
        }

        /// <summary>
        /// Whether the set renders a source word's Strong number with the translation's word elsewhere, in records naming that
        /// word alone, often enough to say so (<see cref="ClearBibleLinks.Slipped"/>).
        /// </summary>
        public bool Renders(ClearBibleToken word, string target) =>
            Texts.TryGetValue(target, out var text)
            && Lemma(word.Strong) is { } lemma
            && Alone.GetValueOrDefault((text, lemma)) is var count and > 0
            && count >= LeastShare * AloneTotals.GetValueOrDefault(text);
    }

    private static HashSet<int> Canonical(IEnumerable<int> printed, IReadOnlyDictionary<int, HashSet<int>> frame)
    {
        var addresses = new HashSet<int>();
        foreach (var verse in printed)
        {
            if (frame.TryGetValue(verse, out var placed))
            {
                addresses.UnionWith(placed);
            }
        }

        return addresses;
    }

    /// <summary>The source verses the target file says each of its tokens renders, by word id.</summary>
    private static Dictionary<string, (int First, int Last)> Renders(IEnumerable<ClearBibleToken> tokens)
    {
        var renders = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (token.Renders is { } range)
            {
                renders[ClearBibleAlignment.Word(token.Id)] = range;
            }
        }

        return renders;
    }

    /// <summary>
    /// Their word ids as ours, dropping a record whose words this corpus does not hold. A record
    /// half-resolved would be a claim about fewer words than the person made, which is a different
    /// claim.
    /// </summary>
    private static List<long> Ours(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, List<long>> placed,
        Func<string, string> key)
    {
        var resolved = new List<long>(ids.Count);
        foreach (var id in ids)
        {
            if (!placed.TryGetValue(key(id), out var words))
            {
                return [];
            }

            foreach (var word in words)
            {
                if (!resolved.Contains(word))
                {
                    resolved.Add(word);
                }
            }
        }

        return resolved;
    }

    /// <summary>
    /// The translation's words, numbered the way a set whose token file does not match its records
    /// numbers them, each number with the word of ours it falls in.
    ///
    /// The numbering is the text's, not a join: each verse is counted from its first word, a word is
    /// divided after an elided article or pronoun — <em>l’</em> and <em>un</em> are two — a
    /// hyphenated word stays one, and every mark of punctuation counts as one. A number that falls on
    /// a mark belongs to no word, and <see cref="Misnumbered"/> refuses the verse it stands in.
    /// </summary>
    internal static (List<ClearBibleToken> Tokens, Dictionary<string, List<long>> Placed) Retokenised(
        IReadOnlyList<PrintedWord> words,
        Counter counter)
    {
        var tokens = new List<ClearBibleToken>(words.Count * 2);
        var placed = new Dictionary<string, List<long>>(words.Count * 2, StringComparer.Ordinal);

        foreach (var verse in words
                     .GroupBy(word => (word.Book, word.Chapter, word.Verse))
                     .OrderBy(verse => verse.Key))
        {
            counter.Verses++;
            var at = 0;
            foreach (var word in verse.OrderBy(word => word.Position))
            {
                foreach (var (text, punctuation) in Pieces(word.Surface + word.Trailer))
                {
                    var id = $"{verse.Key.Book:00}{verse.Key.Chapter:000}{verse.Key.Verse:000}{++at:000}";
                    tokens.Add(new ClearBibleToken(id, text, punctuation, null));
                    counter.Total++;
                    if (!punctuation)
                    {
                        placed[id] = [word.Id];
                        counter.Placed++;
                    }
                }
            }
        }

        return (tokens, placed);
    }

    /// <summary>The pieces a word and the punctuation after it are counted as, in order.</summary>
    internal static IEnumerable<(string Text, bool Punctuation)> Pieces(string written)
    {
        foreach (Match piece in Piece().Matches(written))
        {
            yield return (piece.Value, !char.IsLetterOrDigit(piece.Value[0]));
        }
    }

    /// <summary>
    /// The verses, as <see cref="ClearBibleAlignment.Verse(string)"/> writes them, in which some record
    /// names a mark of punctuation or a number past the verse's last piece. Numbering goes wrong from
    /// the first place the records' text and ours divide differently — a comma one of them has, an
    /// elision one of them writes together — and every number after it in the verse is out by as
    /// much, so the whole verse is refused rather than the record that happened to show it.
    /// </summary>
    internal static HashSet<int> Misnumbered(IEnumerable<ClearBibleRecord> records, IEnumerable<ClearBibleToken> tokens)
    {
        var punctuation = new HashSet<string>(StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            known.Add(token.Id);
            if (token.Excluded)
            {
                punctuation.Add(token.Id);
            }
        }

        var misnumbered = new HashSet<int>();
        foreach (var id in records.SelectMany(record => record.Target).Select(ClearBibleAlignment.Word))
        {
            if ((punctuation.Contains(id) || !known.Contains(id)) && ClearBibleAlignment.Verse(id) is { } verse)
            {
                misnumbered.Add(verse);
            }
        }

        return misnumbered;
    }

    /// <summary>
    /// The verses, as <see cref="ClearBibleAlignment.Verse(string)"/> writes them, in which two or more records name
    /// translation words that share no word with the glosses the source file gives their source words. Of the 7,122 verses
    /// of the Berean's set where no record names a mark, 28 have one such record and 25 have two or more; read by hand, 24
    /// of the 25 are numbered a word or more out over another revision of the translation, Mark 8:10's πλοῖον («boat»)
    /// naming «the» and μετὰ («with») «boat», and one is not, Matthew 23:34's ἀπὸ «from» and εἰς «to», glossed «in» and
    /// «after». That one loses a second opinion the Berean's tables state too.
    /// </summary>
    internal static HashSet<int> Unglossed(
        IEnumerable<ClearBibleRecord> records,
        IEnumerable<ClearBibleToken> tokens,
        IEnumerable<ClearBibleToken> sources)
    {
        var english = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            english[ClearBibleAlignment.Word(token.Id)] = token.Text;
        }

        var glosses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in sources)
        {
            if (token.Gloss is { Length: > 0 } gloss)
            {
                glosses[ClearBibleAlignment.Unit(token.Id)] = gloss;
            }
        }

        var misses = new Dictionary<int, int>();
        foreach (var record in records)
        {
            var words = new HashSet<string>(StringComparer.Ordinal);
            var resolved = true;
            foreach (var id in record.Target)
            {
                if (!english.TryGetValue(ClearBibleAlignment.Word(id), out var text))
                {
                    resolved = false;
                    break;
                }

                words.UnionWith(Stems(text));
            }

            var glossed = record.Source.Where(id => glosses.ContainsKey(ClearBibleAlignment.Unit(id)))
                .SelectMany(id => Stems(glosses[ClearBibleAlignment.Unit(id)])).ToHashSet(StringComparer.Ordinal);
            if (!resolved || words.Count == 0 || glossed.Count == 0 || words.Overlaps(glossed)
                || record.Target.Count == 0 || ClearBibleAlignment.Verse(record.Target[0]) is not { } verse)
            {
                continue;
            }

            misses[verse] = misses.GetValueOrDefault(verse) + 1;
        }

        return [.. misses.Where(verse => verse.Value >= Unrelated).Select(verse => verse.Key)];
    }

    /// <summary>How many records of a verse must miss their glosses before the verse is taken to be numbered over another text.</summary>
    private const int Unrelated = 2;

    /// <summary>The first four letters of each word of an English text, in lower case, which is as near as a gloss and a translation agree on a word's form.</summary>
    private static IEnumerable<string> Stems(string text) =>
        EnglishWord().Matches(text.ToLowerInvariant()).Select(word => word.Value.Length > StemLength ? word.Value[..StemLength] : word.Value);

    private const int StemLength = 4;

    [GeneratedRegex("[a-z]+")]
    private static partial Regex EnglishWord();

    /// <summary>
    /// One piece of written text as the records count it: a word up to and including an elision's
    /// apostrophe, a hyphenated word whole, <em>aujourd’hui</em> whole, or a single mark.
    /// </summary>
    [GeneratedRegex(@"(?i:aujourd[’']hui)|[\p{L}\p{M}\p{N}]+(?:-[\p{L}\p{M}\p{N}]+)*[’']?|\S")]
    private static partial Regex Piece();

    /// <summary>
    /// Their tokens as our word ids, joined on the letters inside each verse.
    ///
    /// The two sides are numbered by different tokenisers, so a token is placed by what it is
    /// written with and not by where it stands. A run the two divide differently is one span naming
    /// every word on both sides of it — their <em>bə</em> and <em>rēʾšîṯ</em> against our two words,
    /// or their two tokens against our one — which is the truth about a division and the only thing
    /// that can be said without inventing a split.
    ///
    /// A token is keyed by <paramref name="key"/>, which for a source edition is everything its
    /// identifier names: each morpheme of the Westminster morphology is placed on its own, so the ו
    /// and the verb of <em>וַיֹּאמֶר</em> land on BHSA's two words and a record naming one of them is
    /// about that one.
    /// </summary>
    private static Dictionary<string, List<long>> Placed(
        StrongNumberLists lists,
        ClearBibleJoin join,
        ClearBibleSide text,
        IReadOnlyList<ClearBibleToken> tokens,
        Func<string, string> key,
        Counter counter)
    {
        if (join == ClearBibleJoin.Edition)
        {
            return Edition(lists, text, tokens, key, counter);
        }

        var ours = text.Words!;
        var placed = new Dictionary<string, List<long>>(ours.Count * 20, StringComparer.Ordinal);

        foreach (var (address, theirs) in WithTheirTitles(Verses(tokens, text.Language, key), ours))
        {
            counter.Total += theirs.Sum(token => 1 + token.With.Count);
            if (!ours.TryGetValue(address, out var mine))
            {
                continue;
            }

            counter.Verses++;
            var spans = TaggedEdition.Align(
                [.. theirs.Select(token => token.Folded)],
                [.. mine.Select(word => word.Folded)]);

            if (TaggedEdition.Agreement(spans, mine.Count) < SameVerse)
            {
                counter.Refused++;
                continue;
            }

            foreach (var span in spans)
            {
                var words = new List<long>(span.CorpusTo - span.CorpusFrom);
                for (var at = span.CorpusFrom; at < span.CorpusTo; at++)
                {
                    words.Add(mine[at].Id);
                }

                for (var at = span.TaggedFrom; at < span.TaggedTo; at++)
                {
                    placed[theirs[at].Id] = words;
                    counter.Placed++;
                    foreach (var part in theirs[at].With)
                    {
                        placed[part] = words;
                        counter.Placed++;
                    }
                }
            }
        }

        return placed;
    }

    /// <summary>
    /// Their verses, with a psalm's title laid at the head of its first verse wherever they number the
    /// title as a verse of its own and ours prints it as the opening of verse 1. The Van Dyck's and
    /// the Indian Revised Version's token files number 721 and 973 words as a verse 0; left apart,
    /// their verse 1 would be laid against a verse of ours that opens with a title it does not have,
    /// and a long title is enough to refuse the verse.
    /// </summary>
    private static IEnumerable<((int, int, int) Address, List<Token> Tokens)> WithTheirTitles(
        IEnumerable<((int, int, int) Address, List<Token> Tokens)> verses,
        IReadOnlyDictionary<(int, int, int), List<Word>> ours)
    {
        List<Token>? title = null;
        var titled = (0, 0);
        foreach (var (address, tokens) in verses)
        {
            var (book, chapter, verse) = address;
            if (verse == 0 && !ours.ContainsKey(address) && ours.ContainsKey((book, chapter, 1)))
            {
                title = tokens;
                titled = (book, chapter);
                continue;
            }

            if (title is not null && verse == 1 && titled == (book, chapter))
            {
                yield return (address, [.. title, .. tokens]);
            }
            else
            {
                yield return (address, tokens);
            }

            title = null;
        }
    }

    /// <summary>
    /// Their words as ours where their source is another edition of a text the corpus holds, laid
    /// against it word for word inside each canonical verse. See <see cref="ClearBibleJoin.Edition"/>.
    /// </summary>
    private static Dictionary<string, List<long>> Edition(
        StrongNumberLists lists,
        ClearBibleSide text,
        IReadOnlyList<ClearBibleToken> tokens,
        Func<string, string> key,
        Counter counter)
    {
        var ours = text.Words!;
        var placed = new Dictionary<string, List<long>>(ours.Count * 20, StringComparer.Ordinal);

        foreach (var (address, theirs) in Verses(tokens, text.Language, key))
        {
            counter.Total += theirs.Count;
            if (!ours.TryGetValue(address, out var mine))
            {
                continue;
            }

            counter.Verses++;
            var counterparts = Counterparts(
                [.. theirs.Select(token => Form(token.Folded, token.Strong, lists))],
                [.. mine.Select(word => Form(word.Folded, word.Strong, lists))]);

            var realigned = theirs.Count != mine.Count;
            for (var at = 0; at < theirs.Count; at++)
            {
                realigned |= counterparts[at] != at;
                if (counterparts[at] < 0)
                {
                    counter.WithoutCounterpart.Add(theirs[at].Id);
                    continue;
                }

                placed[theirs[at].Id] = [mine[counterparts[at]].Id];
                counter.Placed++;
            }

            if (realigned)
            {
                counter.Realigned++;
            }
        }

        return placed;
    }

    /// <summary>
    /// Which of our words each of theirs is, or -1 where it is none of them.
    ///
    /// A word is the same word when the two editions write the same letters, when they state the same
    /// lemma number, or when the spelling is a letter or two apart and nothing says it is a different
    /// word. A word one edition has and the other has not has no counterpart, and neither has a word
    /// standing where the other edition prints a different one: the person who aligned theirs was
    /// looking at a word ours does not print, and what they said about it says nothing about the word
    /// ours prints instead.
    /// </summary>
    internal static int[] Counterparts(IReadOnlyList<WitnessForm> theirs, IReadOnlyList<WitnessForm> ours)
    {
        var counterparts = new int[theirs.Count];
        Array.Fill(counterparts, -1);

        foreach (var pairing in WitnessAlignment.Pair(theirs, ours))
        {
            if (pairing.From.Count != 1 || pairing.To.Count != 1)
            {
                continue;
            }

            var (mine, yours) = (theirs[pairing.From[0]], ours[pairing.To[0]]);
            var differentWord = pairing.Relation != LinkRelation.Equals
                                && mine.Lexeme.Length > 0
                                && yours.Lexeme.Length > 0
                                && mine.Lexeme != yours.Lexeme;

            if (!differentWord)
            {
                counterparts[pairing.From[0]] = pairing.To[0];
            }
        }

        return counterparts;
    }

    internal static WitnessForm Form(string folded, string? strong, StrongNumberLists lists) =>
        new(folded, lists.Lemma(StrongNumbers.Normalize(strong)) ?? string.Empty);

    /// <summary>
    /// Their token file as verses, each in the order its identifiers number it, with the punctuation
    /// the file marks out of the alignment left out and everything folded to the letters it is
    /// compared by.
    ///
    /// The order is the identifiers' and not the file's because the two disagree in 781 verses of the
    /// Westminster morphology, where a word's rows stand after the next word's — in 1 Samuel 22:15
    /// <em>דָבָר</em> is listed before <em>בְּעַבְדּוֹ</em> — and laid in file order against BHSA
    /// their letters would be placed on the wrong words or on none.
    ///
    /// A pronominal suffix is compared as part of the morpheme before it, and placed with it, because
    /// BHSA never writes one apart. Standing alone, the ו of <em>בָנָיו</em> is matched to the next
    /// word's conjunction as readily as to its own word, and the words between are left unplaced.
    /// </summary>
    private static IEnumerable<((int, int, int) Address, List<Token> Tokens)> Verses(
        IReadOnlyList<ClearBibleToken> tokens,
        string language,
        Func<string, string> key)
    {
        var address = (0, 0, 0);
        var verse = new List<ClearBibleToken>(64);

        foreach (var token in tokens)
        {
            if (!ClearBibleAlignment.Address(token.Id, out var book, out var chapter, out var number))
            {
                continue;
            }

            if ((book, chapter, number) != address)
            {
                if (verse.Count > 0)
                {
                    yield return (address, Joined(verse, language, key));
                }

                address = (book, chapter, number);
                verse = [];
            }

            if (!token.Excluded)
            {
                verse.Add(token);
            }
        }

        if (verse.Count > 0)
        {
            yield return (address, Joined(verse, language, key));
        }
    }

    private static List<Token> Joined(List<ClearBibleToken> verse, string language, Func<string, string> key)
    {
        verse.Sort((one, other) => string.CompareOrdinal(key(one.Id), key(other.Id)));
        var tokens = new List<Token>(verse.Count);
        foreach (var token in verse)
        {
            var folded = Comparable(token.Text, language);
            if (token.Part == ClearBibleAlignment.Suffix
                && tokens.Count > 0
                && ClearBibleAlignment.Word(tokens[^1].Id) == ClearBibleAlignment.Word(token.Id))
            {
                tokens[^1] = tokens[^1] with
                {
                    Folded = tokens[^1].Folded + folded,
                    With = [.. tokens[^1].With, key(token.Id)],
                };
                continue;
            }

            tokens.Add(new Token(key(token.Id), folded, token.Strong, []));
        }

        return tokens;
    }

    /// <summary>
    /// The form two editions are compared by: the word's own letters, folded the way the corpus
    /// folds them for search, with the punctuation each edition attaches differently removed.
    ///
    /// It is <see cref="WordFolding"/> and not a second fold written here, so that a Hebrew word
    /// loses its points and a Greek word its accents by the same rule the reader searches by. What
    /// is added is dropping everything that is not a letter or a digit, because the two sides of
    /// this join disagree about punctuation by construction: their tokeniser makes a token of it and
    /// this reader hangs it on the word before.
    /// </summary>
    internal static string Comparable(string text, string? language)
    {
        var folded = WordFolding.Fold(text, language);
        var letters = new System.Text.StringBuilder(folded.Length);
        foreach (var character in folded)
        {
            if (char.IsLetterOrDigit(character))
            {
                letters.Append(char.ToLowerInvariant(character));
            }
        }

        return letters.ToString();
    }

    /// <summary>A word with the verse its text prints it in.</summary>
    internal sealed record PrintedWord(int Book, int Chapter, int Verse, int Position, long Id, string Surface, string Trailer);

    internal sealed record Word(long Id, string Folded, string? Strong);

    /// <param name="With">The morphemes compared and placed as part of this one: its suffixes.</param>
    private sealed record Token(string Id, string Folded, string? Strong, IReadOnlyList<string> With);

    /// <param name="Moved">Which word, if any, the record was moved onto from the preposition or article before it.</param>
    internal sealed record Draft(List<long> From, List<long> To, Move Moved = Move.None);

    internal sealed class Counter
    {
        public int Total { get; set; }

        public int Placed { get; set; }

        public int Verses { get; set; }

        public int Refused { get; set; }

        public int Realigned { get; set; }

        public HashSet<string> WithoutCounterpart { get; } = new(StringComparer.Ordinal);
    }

    internal sealed class Placement
    {
        public Counter Source { get; } = new();

        public Counter Target { get; } = new();

        public ClearBiblePlacement Read() => new(
            Source.Placed,
            Source.Total,
            Target.Placed,
            Target.Total,
            Math.Max(Source.Verses, Target.Verses),
            Source.Refused + Target.Refused,
            Source.Realigned);
    }
}
