using System.Globalization;
using System.Text;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>One verse of a text as the consensus reads it: the canonical addresses it stands at, and its words.</summary>
internal sealed record ConsensusVerse(IReadOnlyList<int> Addresses, IReadOnlyList<(long Word, string Surface)> Words);

/// <summary>
/// One name of an entity in a text: the letters every form of it shares, the forms, and how well it
/// fits the entity's verses.
/// </summary>
/// <param name="Core">The letters every form contains, folded.</param>
/// <param name="Forms">The folded forms, commonest first.</param>
/// <param name="Covered">How many of the entity's verses in the text print one of the forms.</param>
/// <param name="Coverage"><paramref name="Covered"/> over the entity's verses in the text.</param>
/// <param name="Specificity">
/// Of the verses printing one of the forms, apart from those only a namesake stands in, the share that
/// are the entity's.
/// </param>
internal sealed record NameCluster(
    string Core,
    IReadOnlyList<string> Forms,
    int Covered,
    double Coverage,
    double Specificity)
{
    public double Score => Coverage * Specificity;
}

/// <summary>What the consensus found for one entity in one text.</summary>
/// <param name="Verses">The entity's verses the text has.</param>
/// <param name="Name">The best name, or null where no word of the verses is particular to them.</param>
/// <param name="Margin">
/// How far <paramref name="Name"/> scores above the best name sharing no form with it: nothing where
/// two different words fit the verses equally, as two brothers always listed together do.
/// </param>
/// <param name="Likeness">
/// How alike the name's consonants are to those of the name the original writes, by
/// <see cref="NameLists"/>; null in a script whose letters that cannot read — Chinese, Korean,
/// Devanagari, Arabic — or where the question gives no spelling.
/// </param>
internal sealed record NameFinding(
    int Entity,
    int Verses,
    NameCluster? Name,
    double Margin,
    double? Likeness = null,
    EntityKind Kind = EntityKind.Person);

/// <summary>A word the consensus reads as naming an entity.</summary>
/// <param name="Verse">Which of the verses read it stands in.</param>
internal readonly record struct ConsensusWord(long Word, int Entity, int Verse);

/// <summary>What a reading of the whole text needs to be told, apart from the text.</summary>
/// <param name="Entities">Each entity's verses, as canonical addresses.</param>
/// <param name="Namesakes">For an entity, the others the original calls by one of its names.</param>
/// <param name="Spellings">For an entity, the lemmas the original names it with.</param>
/// <param name="Kinds">What each entity is; a person where it is not said.</param>
internal sealed record ConsensusQuestion(
    IReadOnlyDictionary<int, IReadOnlySet<int>> Entities,
    IReadOnlyDictionary<int, IReadOnlySet<int>> Namesakes,
    IReadOnlyDictionary<int, IReadOnlySet<string>>? Spellings = null,
    IReadOnlyDictionary<int, EntityKind>? Kinds = null)
{
    public EntityKind KindOf(int entity) => Kinds?.GetValueOrDefault(entity, EntityKind.Person) ?? EntityKind.Person;
}

/// <summary>How the words of one kind of entity are read.</summary>
/// <param name="Capitalised">
/// Whether, in a text that marks names with capitals, a form printed mostly in lower case is refused.
/// True of a person's and a place's name; a people's is a gentilic Spanish and Russian write in lower
/// case, and a thing's, a feast's or a title's is a common noun.
/// </param>
/// <param name="FormSpecificity">
/// How particular to the entity's verses a form must be to join its name. A common noun recurs in
/// verses that do not name the thing — <em>ark</em> is Noah's as well — so the contrast is held stricter.
/// </param>
/// <param name="Spelt">
/// Whether a finding read off fewer verses than the bar stands where its consonants are the
/// original's: a name is transliterated, a thing's word is translated.
/// </param>
internal sealed record ConsensusRules(bool Capitalised, double FormSpecificity, bool Spelt)
{
    private static readonly ConsensusRules Names = new(true, NameConsensus.FormSpecificity, true);
    private static readonly ConsensusRules Peoples = new(false, NameConsensus.FormSpecificity, true);
    private static readonly ConsensusRules Titles = new(false, NameConsensus.CommonNounSpecificity, true);
    private static readonly ConsensusRules CommonNouns = new(false, NameConsensus.CommonNounSpecificity, false);

    public static ConsensusRules For(EntityKind kind) => kind switch
    {
        EntityKind.People => Peoples,
        EntityKind.Title => Titles,
        EntityKind.Object or EntityKind.Observance or EntityKind.Term => CommonNouns,
        _ => Names,
    };
}

/// <summary>When a finding is believed.</summary>
/// <param name="LeastVerses">
/// How many of the entity's verses the text must have, unless the name is spelt like the original's.
/// A name read off one or two verses is as likely the father's printed beside it.
/// </param>
/// <param name="LeastScore">How high the first name must score.</param>
/// <param name="LeastMargin">How far above the best name sharing no form with it.</param>
internal sealed record ConsensusBar(int LeastVerses, double LeastScore, double LeastMargin)
{
    public bool Accepts(NameFinding finding) =>
        finding.Name is { } name
        && (finding.Verses >= LeastVerses
            || (ConsensusRules.For(finding.Kind).Spelt && finding.Likeness >= NameLists.LeastLikeness))
        && name.Coverage >= NameConsensus.LeastCoverage
        && name.Score >= LeastScore
        && finding.Margin >= LeastMargin;
}

/// <summary>A bar for each kind of entity, and one for a kind given none.</summary>
internal sealed record ConsensusBars(IReadOnlyDictionary<EntityKind, ConsensusBar> ByKind, ConsensusBar Otherwise)
{
    public ConsensusBar For(EntityKind kind) => ByKind.GetValueOrDefault(kind, Otherwise);

    public bool Accepts(NameFinding finding) => For(finding.Kind).Accepts(finding);
}

/// <summary>
/// The name of an entity in a text, read off nothing but the text and the verses that name it.
///
/// <para>
/// Abraham is named in some three hundred verses. Whatever a translation calls him, that word recurs
/// in nearly all of them and in hardly any other verse, while <em>and</em> recurs in all of them and
/// in every other verse too. So a word is scored by two shares: of the entity's verses, how many print
/// it, and of the verses that print it, how many are the entity's. The product is high only for the
/// name. It needs no link, no Strong number and no dictionary, which is the point: it reaches a text
/// that has none of them.
/// </para>
///
/// <para>
/// A name is inflected, prefixed and spelt two ways, so it is read as a cluster of forms sharing a
/// core of letters rather than as one word: <em>Авраам</em>, <em>Авраама</em> and <em>Авраамові</em>
/// share <em>авраам</em>, <em>وإبراهيم</em> and <em>لإبراهيم</em> share <em>ابراهيم</em>,
/// <em>아브라함이</em> and <em>아브라함의</em> share <em>아브라함</em>, and <em>Abram</em> and
/// <em>Abraham</em> share <em>abra</em>. A form joins a core only when it adds a few letters to it and
/// is itself particular to the entity's verses, so <em>Dan</em> does not gather <em>Daniel</em> and
/// <em>dance</em>.
/// </para>
///
/// <para>
/// A namesake shares the name and not the verses. The verses only a namesake stands in are left out
/// of the second share, since the name is expected there too, and a word in a verse that two
/// entities of one name both stand in names neither: that one the consensus cannot tell apart, and it
/// says nothing rather than guess.
/// </para>
/// </summary>
internal static class NameConsensus
{
    /// <summary>
    /// How particular to an entity's verses a form must be to join a name at all. A declined form
    /// printed in one of the verses and nowhere else is 1; a common word is near the share the verses
    /// are of the whole text.
    /// </summary>
    internal const double FormSpecificity = 0.5;

    /// <summary>
    /// The share of an entity's verses its name must be printed in. A word printed in one of six, the
    /// month <em>Sebat</em> beside the prophet Zechariah, is particular to the verses and is not the
    /// name: on the texts whose annotations came across a stated link, a fifth of the words found
    /// under this share were another entity's, against one in three hundred above it.
    /// </summary>
    internal const double LeastCoverage = 0.3;

    /// <summary><see cref="FormSpecificity"/> for a word that is a common noun: a thing's, a feast's, a title's.</summary>
    internal const double CommonNounSpecificity = 0.7;

    /// <summary>A name of two letters is a name — Og, Ur, Er — and a name of one is a letter.</summary>
    private const int ShortestName = 2;

    /// <summary>
    /// In a script with capitals, the share of a form's occurrences that must begin with one for it
    /// to be a name. <em>иерей</em> stands beside Joshua the high priest in every verse that names
    /// him, and it is never a name; half is low enough that a name printed at the head of a sentence
    /// and a form a text happens to print lower-case now and then are both kept.
    /// </summary>
    internal const double Capitalised = 0.5;

    /// <summary>
    /// How many of a text's words in a script with capitals must begin with one for the text to be
    /// read as marking its names by them. A manuscript edition writes every word in lower case, and
    /// there the test above would refuse every name.
    /// </summary>
    internal const double MarksNames = 0.02;

    /// <summary>
    /// What a name shorter than a core may add and still be the same name: <em>Ога</em> and
    /// <em>Огом</em> are Og, and no more than a case ending is let in.
    /// </summary>
    private const int ShortNameAffixes = 2;

    /// <summary>
    /// How close to the best score a longer core must come to be preferred to it. A short core gathers
    /// every stray word of the verses that happens to contain it — <em>aar</em> takes in a
    /// <em>Gabaar</em> printed once beside Aaron — and that one verse is worth less than the name
    /// being the name.
    /// </summary>
    private const double AsGood = 0.01;

    /// <summary>How a script builds its words, for the letters a name's core must have and may be given.</summary>
    /// <param name="LeastCore">The shortest core read as a name.</param>
    /// <param name="Affixes">How many letters a form may add to its core, before or after.</param>
    internal readonly record struct Script(int LeastCore, int Affixes);

    /// <summary>
    /// Alphabets write a name in several letters and put a case ending, an article or a conjunction on
    /// it: <em>وَلِإِبْرَاهِيمَ</em> is three letters more than the name, <em>Авраамові</em> three.
    /// </summary>
    private static readonly Script Alphabet = new(3, 4);

    /// <summary>A Chinese name is two to four characters, and a segmenter leaves a particle on it.</summary>
    private static readonly Script Han = new(2, 2);

    /// <summary>A Korean name is a few syllables, and a particle of up to four follows it.</summary>
    private static readonly Script Hangul = new(2, 4);

    /// <summary>An Ethiopic letter is a syllable; a name may be two, and a clitic one or two.</summary>
    private static readonly Script Ethiopic = new(2, 3);

    public static Script Of(string folded)
    {
        foreach (var letter in folded)
        {
            if (letter is >= '\u4E00' and <= '\u9FFF' or >= '\u3400' and <= '\u4DBF')
            {
                return Han;
            }

            if (letter is >= '\uAC00' and <= '\uD7AF')
            {
                return Hangul;
            }

            if (letter is >= '\u1200' and <= '\u137F')
            {
                return Ethiopic;
            }
        }

        return Alphabet;
    }

    /// <summary>
    /// A word as the consensus compares it: lower case, without the marks a script writes or leaves
    /// off at will — vowel points, accents, harakat — with the Arabic alef's hamza forms one letter,
    /// the Greek final sigma the medial one, the Ethiopic homophones merged, and nothing that is not a
    /// letter. It is <see cref="WordFolding"/>, which a reader's search already goes through, told the
    /// language by the script. Devanagari's marks are letters of the word and stay.
    /// </summary>
    public static string Fold(string surface)
    {
        var bare = WordFolding.Fold(surface, ScriptLanguage(surface));
        var folded = new StringBuilder(bare.Length);
        foreach (var letter in bare)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(letter);
            var devanagari = letter is >= '\u0900' and <= '\u097F';
            if (!char.IsLetter(letter) && !(devanagari && category is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark))
            {
                continue;
            }

            folded.Append(letter switch
            {
                '\u0623' or '\u0625' or '\u0622' or '\u0671' => '\u0627', // the hamza forms and the alif of union: alif
                '\u0649' => '\u064A', // alif maqsura: ya
                '\u03C2' => '\u03C3', // final sigma: sigma
                _ => char.ToLowerInvariant(letter),
            });
        }

        return folded.ToString();
    }

    /// <summary>The language whose folding a word's script needs, or null for one that only lowers its case.</summary>
    private static string? ScriptLanguage(string surface)
    {
        foreach (var letter in surface)
        {
            switch (letter)
            {
                case >= '\u0370' and <= '\u03FF' or >= '\u1F00' and <= '\u1FFF':
                    return "grc";
                case >= '\u0590' and <= '\u05FF':
                    return "hbo";
                case >= '\u0600' and <= '\u06FF':
                    return "arb";
                case >= '\u1200' and <= '\u137F':
                    return "gez";
            }
        }

        return null;
    }
    /// <summary>
    /// Every core a form can be filed under: each run of its letters at least
    /// <see cref="Script.LeastCore"/> long to which it adds no more than <see cref="Script.Affixes"/>.
    /// A form shorter than that is its own core and nothing else's, so <em>Og</em> is a name and
    /// <em>og</em> is not a core of <em>Ogom</em>.
    /// </summary>
    public static IEnumerable<string> Cores(string folded)
    {
        var script = Of(folded);
        if (folded.Length < script.LeastCore)
        {
            if (folded.Length >= ShortestName)
            {
                yield return folded;
            }

            yield break;
        }

        var shortest = Math.Max(Math.Max(script.LeastCore, folded.Length - script.Affixes), (folded.Length + 1) / 2);
        for (var length = folded.Length; length >= shortest; length--)
        {
            for (var start = 0; start + length <= folded.Length; start++)
            {
                yield return folded.Substring(start, length);
            }
        }
    }

    /// <summary>
    /// Reads the name of every entity in <paramref name="question"/> off <paramref name="verses"/>,
    /// and every word of the entity's verses that prints it.
    ///
    /// <para>
    /// A word is not claimed in a verse a namesake also stands in when the form is particular to the
    /// namesake too. Mary Magdalene's best name is <em>Magdalene</em>, which the other Marys never
    /// carry, but <em>Mary</em> is hers as much as theirs, and in the verses both stand in it is
    /// either's.
    /// </para>
    /// </summary>
    public static (IReadOnlyList<NameFinding> Findings, IReadOnlyList<ConsensusWord> Claims) Read(
        IReadOnlyList<ConsensusVerse> verses,
        ConsensusQuestion question)
    {
        var text = new TextIndex(verses);
        var findings = new List<NameFinding>();
        var read = new Dictionary<int, (int[] Mine, HashSet<int> Forms, Dictionary<int, int> Particular)>();

        foreach (var (entity, addresses) in question.Entities.OrderBy(entry => entry.Key))
        {
            var mine = text.VersesAt(addresses);
            if (mine.Length == 0)
            {
                continue;
            }

            var namesakeOnly = question.Namesakes.TryGetValue(entity, out var namesakes)
                ? text.VersesAt(namesakes
                        .Where(question.Entities.ContainsKey)
                        .SelectMany(namesake => question.Entities[namesake])
                        .Where(address => !addresses.Contains(address))
                        .ToHashSet())
                    .Except(mine)
                    .ToArray()
                : [];

            var kind = question.KindOf(entity);
            var (finding, forms, particular) = text.Name(entity, mine, namesakeOnly, ConsensusRules.For(kind));
            findings.Add(finding with
            {
                Likeness = Likeness(finding.Name, question.Spellings?.GetValueOrDefault(entity)),
                Kind = kind,
            });
            read[entity] = (mine, forms, particular);
        }

        var standing = new Dictionary<int, List<int>>();
        foreach (var (entity, (mine, _, _)) in read)
        {
            foreach (var verse in mine)
            {
                if (!standing.TryGetValue(verse, out var entities))
                {
                    standing[verse] = entities = [];
                }

                entities.Add(entity);
            }
        }

        var claims = new List<ConsensusWord>();
        foreach (var (entity, (mine, forms, particular)) in read.OrderBy(entry => entry.Key))
        {
            if (forms.Count == 0)
            {
                continue;
            }

            var rules = ConsensusRules.For(question.KindOf(entity));
            var namesakes = question.Namesakes.GetValueOrDefault(entity);
            foreach (var verse in mine)
            {
                var others = standing[verse].Where(other => other != entity).ToList();
                var tokens = text.Tokens[verse];
                for (var at = 0; at < tokens.Length; at++)
                {
                    var type = tokens[at];
                    if (!forms.Contains(type)
                        || (rules.Capitalised && text.MarksNames && text.Lower[verse][at])
                        || others.Any(other => Rivals(entity, other, type, particular[type], mine.Length, namesakes?.Contains(other) == true)))
                    {
                        continue;
                    }

                    claims.Add(new ConsensusWord(verses[verse].Words[at].Word, entity, verse));
                }
            }
        }

        return (findings, claims);

        // Another entity the verse names has as good a claim on the form. A namesake does whenever its
        // own verses print the form too. Another entity of the same kind does when the form is its own
        // name as well, and then neither takes it: Dishan beside Ezer, John beside James where the
        // Arabic's "and John" follows James in nearly every verse. A form that merely stands beside
        // another's name, as Abihu's does beside Nadab's, is not theirs. An entity of another kind does
        // when the form is particular to its verses and they print it as large a share of the time:
        // the Jashubites beside Jashub.
        bool Rivals(int entity, int other, int type, int own, int mine, bool namesake)
        {
            var (theirVerses, theirForms, theirs) = read[other];
            if (namesake)
            {
                return theirs.ContainsKey(type) || text.Printing(theirVerses, type) * 2 >= theirVerses.Length;
            }

            if (question.KindOf(other) == question.KindOf(entity))
            {
                return theirForms.Contains(type);
            }

            return theirs.TryGetValue(type, out var printed) && (long)printed * mine >= (long)own * theirVerses.Length;
        }
    }

    /// <summary>How many of a name's commonest forms are spelt against the original.</summary>
    private const int FormsSpelt = 3;

    /// <summary>
    /// The best <see cref="NameLists.Alike"/> between the consonants of the name's commonest forms and
    /// those of the original's lemmas, or null where the script is one those consonants are not read in.
    /// </summary>
    private static double? Likeness(NameCluster? name, IReadOnlySet<string>? spellings)
    {
        if (name is null || spellings is null || spellings.Count == 0 || !Spelt(name.Core))
        {
            return null;
        }

        var lemmas = spellings.Select(spelling => NameLists.Skeleton(spelling)).ToList();
        return name.Forms
            .Take(FormsSpelt)
            .Select(form => NameLists.Skeleton(form, form.Any(IsEthiopic) ? EthiopicLanguage : null))
            .Max(form => lemmas.Max(lemma => NameLists.Alike(form, lemma)));
    }

    /// <summary>Whether <see cref="NameLists.Skeleton(string?)"/> reads a word's letters: Latin, Greek, Cyrillic, Hebrew or Ethiopic.</summary>
    private static bool Spelt(string folded) =>
        folded.All(letter => letter is >= 'a' and <= 'z' or >= '\u00C0' and <= '\u024F'
            or >= '\u0370' and <= '\u03FF' or >= '\u1F00' and <= '\u1FFF' or >= '\u0400' and <= '\u04FF'
            or >= '\u0590' and <= '\u05FF'
            || IsEthiopic(letter));

    private const string EthiopicLanguage = "gez";

    private static bool IsEthiopic(char letter) => letter is >= '\u1200' and <= '\u137F';

    /// <summary>
    /// The claims of the findings <paramref name="bar"/> accepts, less every word two of them claim:
    /// in a verse two entities of one name both stand in, the name is either's, and saying which is
    /// a guess.
    /// </summary>
    public static IReadOnlyList<ConsensusWord> Settle(
        IReadOnlyList<NameFinding> findings,
        IReadOnlyList<ConsensusWord> claims,
        ConsensusBar bar) =>
        Settle(findings, claims, bar.Accepts);

    /// <inheritdoc cref="Settle(IReadOnlyList{NameFinding}, IReadOnlyList{ConsensusWord}, ConsensusBar)"/>
    public static IReadOnlyList<ConsensusWord> Settle(
        IReadOnlyList<NameFinding> findings,
        IReadOnlyList<ConsensusWord> claims,
        Func<NameFinding, bool> accepts)
    {
        var accepted = findings.Where(accepts).Select(finding => finding.Entity).ToHashSet();
        return claims
            .Where(claim => accepted.Contains(claim.Entity))
            .GroupBy(claim => claim.Word)
            .Where(word => word.Select(claim => claim.Entity).Distinct().Count() == 1)
            .Select(word => word.First())
            .ToList();
    }

    /// <summary>A text read once: its forms, and which verses print each.</summary>
    private sealed class TextIndex
    {
        private readonly Dictionary<int, List<int>> _atAddress = [];
        private readonly List<string> _forms = [];
        private readonly List<int[]> _printedIn = [];
        private readonly List<bool> _lowerCase = [];
        private readonly int[] _stamp;
        private int _generation;

        public TextIndex(IReadOnlyList<ConsensusVerse> verses)
        {
            var typeOf = new Dictionary<string, int>(StringComparer.Ordinal);
            var printedIn = new List<List<int>>();
            var occurrences = new List<int>();
            var capitals = new List<int>();
            var lower = new List<int>();
            Tokens = new int[verses.Count][];
            Lower = new bool[verses.Count][];
            for (var verse = 0; verse < verses.Count; verse++)
            {
                foreach (var address in verses[verse].Addresses)
                {
                    if (!_atAddress.TryGetValue(address, out var at))
                    {
                        _atAddress[address] = at = [];
                    }

                    at.Add(verse);
                }

                var words = verses[verse].Words;
                var tokens = new int[words.Count];
                var lowered = new bool[words.Count];
                for (var at = 0; at < words.Count; at++)
                {
                    var folded = Fold(words[at].Surface);
                    if (folded.Length == 0)
                    {
                        tokens[at] = -1;
                        continue;
                    }

                    if (!typeOf.TryGetValue(folded, out var type))
                    {
                        typeOf[folded] = type = _forms.Count;
                        _forms.Add(folded);
                        printedIn.Add([]);
                        occurrences.Add(0);
                        capitals.Add(0);
                        lower.Add(0);
                    }

                    tokens[at] = type;
                    occurrences[type]++;
                    var first = words[at].Surface.FirstOrDefault(char.IsLetter);
                    if (char.IsUpper(first))
                    {
                        capitals[type]++;
                    }
                    else if (char.IsLower(first))
                    {
                        lower[type]++;
                        lowered[at] = true;
                    }

                    var list = printedIn[type];
                    if (list.Count == 0 || list[^1] != verse)
                    {
                        list.Add(verse);
                    }
                }

                Tokens[verse] = tokens;
                Lower[verse] = lowered;
            }

            _printedIn = printedIn.Select(list => list.ToArray()).ToList();
            _stamp = new int[verses.Count];
            _lowerCase = Enumerable.Range(0, occurrences.Count)
                .Select(type => lower[type] > 0 && capitals[type] < Capitalised * occurrences[type])
                .ToList();
            MarksNames = capitals.Sum() >= NameConsensus.MarksNames * (capitals.Sum() + lower.Sum());
        }

        public int[][] Tokens { get; }

        /// <summary>Whether each word is printed beginning with a lower-case letter.</summary>
        public bool[][] Lower { get; }

        /// <summary>Whether the text marks its names with capitals, which a manuscript edition does not.</summary>
        public bool MarksNames { get; }

        /// <summary>How many of <paramref name="verses"/> print the form.</summary>
        public int Printing(int[] verses, int type) => verses.Count(verse => Tokens[verse].Contains(type));

        public int[] VersesAt(IEnumerable<int> addresses) =>
            addresses
                .SelectMany(address => _atAddress.TryGetValue(address, out var at) ? at : [])
                .Distinct()
                .Order()
                .ToArray();

        /// <returns>
        /// The finding, the forms of its name, and every form particular to the entity's verses with how
        /// many of them print it.
        /// </returns>
        public (NameFinding Finding, HashSet<int> Forms, Dictionary<int, int> Particular) Name(
            int entity,
            int[] mine,
            int[] namesakeOnly,
            ConsensusRules rules)
        {
            var inMine = new Dictionary<int, int>();
            foreach (var verse in mine)
            {
                foreach (var type in Tokens[verse].Where(type => type >= 0).Distinct())
                {
                    inMine[type] = inMine.GetValueOrDefault(type) + 1;
                }
            }

            var inNamesakes = new Dictionary<int, int>();
            foreach (var verse in namesakeOnly)
            {
                foreach (var type in Tokens[verse].Where(type => type >= 0).Distinct())
                {
                    inNamesakes[type] = inNamesakes.GetValueOrDefault(type) + 1;
                }
            }

            var particular = inMine
                .Where(entry => (double)entry.Value
                    / (_printedIn[entry.Key].Length - inNamesakes.GetValueOrDefault(entry.Key)) >= rules.FormSpecificity
                    && !(rules.Capitalised && MarksNames && _lowerCase[entry.Key]))
                .ToDictionary(entry => entry.Key, entry => entry.Value);

            var byCore = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var type in particular.Keys)
            {
                foreach (var core in Cores(_forms[type]))
                {
                    if (!byCore.TryGetValue(core, out var types))
                    {
                        byCore[core] = types = [];
                    }

                    types.Add(type);
                }
            }

            foreach (var (core, types) in byCore.Where(entry => entry.Key.Length < Of(entry.Key).LeastCore))
            {
                types.UnionWith(particular.Keys.Where(type => _forms[type].Length - core.Length <= ShortNameAffixes
                    && _forms[type].StartsWith(core, StringComparison.Ordinal)));
            }

            foreach (var (core, types) in byCore)
            {
                var lead = _forms[types.MaxBy(type => particular[type])];
                types.RemoveWhere(type => !SameName(_forms[type], lead, core));
            }

            var excluded = namesakeOnly.ToHashSet();
            var scored = byCore
                .Select(entry => Score(entry.Key, entry.Value, mine, excluded))
                .OrderByDescending(scoredCore => scoredCore.Cluster.Score)
                .ThenByDescending(scoredCore => scoredCore.Cluster.Core.Length)
                .ThenBy(scoredCore => scoredCore.Cluster.Core, StringComparer.Ordinal)
                .ToList();
            if (scored.Count == 0)
            {
                return (new NameFinding(entity, mine.Length, null, 0), [], particular);
            }

            var best = scored
                .TakeWhile(scoredCore => scoredCore.Cluster.Score >= scored[0].Cluster.Score - AsGood)
                .MaxBy(scoredCore => scoredCore.Cluster.Core.Length);
            var rival = scored.FirstOrDefault(other => !other.Types.Overlaps(best.Types));
            var margin = best.Cluster.Score - (rival.Cluster?.Score ?? 0);

            return (new NameFinding(entity, mine.Length, best.Cluster, margin), best.Types, particular);
        }

        /// <summary>
        /// Whether a form filed under a core is the same name as the form the entity's verses print most:
        /// one holds the other, or the core opens both or closes both. <em>Schadrac</em> and
        /// <em>Méschac</em> share <em>scha</em>, at the head of one and inside the other, and are two men.
        /// </summary>
        private static bool SameName(string form, string lead, string core) =>
            form.Contains(lead, StringComparison.Ordinal)
            || lead.Contains(form, StringComparison.Ordinal)
            || (form.StartsWith(core, StringComparison.Ordinal) && lead.StartsWith(core, StringComparison.Ordinal))
            || (form.EndsWith(core, StringComparison.Ordinal) && lead.EndsWith(core, StringComparison.Ordinal));

        private (NameCluster Cluster, HashSet<int> Types) Score(
            string core,
            HashSet<int> types,
            int[] mine,
            HashSet<int> excluded)
        {
            var reached = mine.Count(verse => Tokens[verse].Any(types.Contains));

            _generation++;
            var printed = 0;
            foreach (var type in types)
            {
                foreach (var verse in _printedIn[type])
                {
                    if (_stamp[verse] == _generation || excluded.Contains(verse))
                    {
                        continue;
                    }

                    _stamp[verse] = _generation;
                    printed++;
                }
            }

            var forms = types
                .OrderByDescending(type => _printedIn[type].Length)
                .ThenBy(type => _forms[type], StringComparer.Ordinal)
                .Select(type => _forms[type])
                .ToList();
            var cluster = new NameCluster(
                core,
                forms,
                reached,
                (double)reached / mine.Length,
                printed == 0 ? 0 : (double)reached / printed);
            return (cluster, types);
        }
    }
}
