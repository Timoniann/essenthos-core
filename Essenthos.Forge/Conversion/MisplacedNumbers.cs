using System.Text.Json;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;

namespace Essenthos.Core.Conversion;

/// <summary>
/// Luther 1912's Strong numbers taken off the German pronouns and auxiliaries that cannot render the word they name.
///
/// <para>
/// The tagging descends from the Zefania module of 2005, whose number follows the phrase it renders
/// (<c>gekauft ist&lt;4735&gt;</c>); eBible's file puts it on the phrase's last word alone, and the module's own
/// slips put it on a neighbour. Either way a number lands on <em>ist</em>, <em>hatte</em>, <em>werden</em>, <em>er</em> or
/// <em>wir</em> while naming a noun, a preposition or a particle: Genesis 4:17's <em>er</em> carries H5892 'town', Micah 4:1's
/// <em>werden</em> H5921 'upon', Matthew 3:10's <em>ist</em> G2235 'already'. A Strong pairing then joins the pronoun to the
/// noun. A sample of 30 such tags read by hand (TSK-1471) found the number wrong on the word in 13, right in 6 (a pronoun
/// standing for the noun it names, <em>hatte</em> for יֵשׁ) and the end of a phrase in 9.
/// </para>
///
/// <para>
/// What a number names is read from the originals' own words: the part of speech BHSA or Nestle 1904 gives the words that
/// carry it most often. Then, for each tagged word:
/// <list type="bullet">
/// <item>an auxiliary (a form of <em>sein</em>, <em>haben</em>, <em>werden</em> or a modal) carrying a number of a word no
/// auxiliary renders by itself (not a verb, pronoun, name or interjection, nor one of the words an auxiliary does render:
/// יֵשׁ, אַיִן, עוֹד, the conditional אִם/לוּ/לוּלֵא/εἰ/ἐάν that German renders by putting the verb first, εἴτε, בַּעַל
/// 'owner' as <em>der … hat</em>): the number moves to the word before it where that word is a content word with no number
/// and the verse carries the number nowhere else (the phrase's head), and is cleared otherwise; a modal keeps a noun or an
/// adjective it may render (ἀνάγκη, δυνατός) where nothing takes the number;</item>
/// <item>a pronoun after an untagged German preposition, carrying a Greek preposition's number: the number moves to the
/// preposition (<em>zu ihnen</em> for πρὸς αὐτούς);</item>
/// <item>a subject pronoun (<em>er</em>, <em>ich</em>, <em>du</em>, <em>wir</em>) carrying the number of a preposition,
/// conjunction, adverb, particle, negation or article: cleared;</item>
/// <item>any other pronoun carrying such a number of the Greek, or of a Hebrew conjunction: cleared. A Hebrew preposition,
/// noun, adverb or negation keeps it: the German pronoun renders its suffix (<em>vor dir</em> for לְפָנֶיךָ).</item>
/// <item>an auxiliary or a pronoun carrying a verb's number, right after an untagged content word in the same clause: the
/// number moves to that word, which renders the verb (<em>geboten hat</em>, <em>vergossen wird</em>, <em>sprach er</em>,
/// <em>fürchtete sich</em>), unless the verb is one an auxiliary or a modal renders itself (הָיָה, εἰμί, γίνομαι, ἔχω,
/// יָכֹל, δύναμαι, θέλω, δεῖ, ὀφείλω, אָבָה). Read on samples of 30: right or defensible in 30, 30, 30 and 27 of 27;
/// the Zefania module of the same lineage puts 93% of these numbers on the same word.</item>
/// </list>
/// A capitalised form inside a sentence is a noun (<em>Habe</em>, <em>Waren</em>), and <em>sein</em> is the verb only where
/// nothing but a function word follows it. A number the rules miss is put right at the word in the text's corrections
/// file.
/// </para>
/// </summary>
internal sealed class MisplacedNumbers : ITextStage
{
    /// <summary>The originals whose words say what each number names: the Hebrew Bible's, then the Greek New Testament's.</summary>
    private static readonly string[] Originals = [BhsaTextSource.Slug, NestleTextSource.Slug];

    private static readonly HashSet<string> Subjects = new(["er", "ich", "du", "wir"], StringComparer.Ordinal);

    private static readonly HashSet<string> Pronouns = new(
        ["sie", "es", "ihr", "man", "ihn", "ihm", "ihnen", "mich", "mir", "dich", "dir", "uns", "euch", "sich"], StringComparer.Ordinal);

    private static readonly HashSet<string> Modals = new(
        ["soll", "sollst", "sollen", "sollt", "sollte", "sollten", "will", "willst", "wollen", "wollt", "wollte", "wollten", "mag", "mögen",
         "möge", "kann", "kannst", "können", "könnt", "konnte", "konnten", "muß", "mußt", "müssen", "müßt", "mußte", "mußten", "darf", "dürfen"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> Auxiliaries = new(
        ["ist", "war", "sind", "waren", "bin", "bist", "seid", "sei", "seien", "wäre", "wären", "gewesen", "sein", "hat", "hatte", "hatten",
         "haben", "habe", "hast", "habt", "hätte", "hätten", "hättest", "gehabt", "wird", "werden", "wurde", "wurden", "werde", "wirst",
         "werdet", "würde", "würden", "worden", "ward", .. Modals],
        StringComparer.Ordinal);

    private static readonly HashSet<string> Prepositions = new(
        ["zu", "von", "in", "an", "auf", "mit", "bei", "nach", "aus", "für", "über", "unter", "vor", "durch", "um", "gegen", "ohne", "zum",
         "zur", "im", "am", "vom", "ins", "wider", "samt", "gen"],
        StringComparer.Ordinal);

    /// <summary>Words that are never the head of a phrase whose number its auxiliary carries.</summary>
    private static readonly HashSet<string> Function = new(
        [.. Subjects, .. Pronouns, .. Auxiliaries, .. Prepositions,
         "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "einer", "eines", "und", "oder", "aber", "denn", "daß",
         "wenn", "als", "wie", "so", "da", "nicht", "ja", "auch", "noch", "schon", "nur", "sehr",
         "mein", "meine", "meinen", "meinem", "meiner", "meines", "dein", "deine", "deinen", "deinem", "deiner", "deines", "seine",
         "seinen", "seinem", "seiner", "seines", "ihre", "ihren", "ihrem", "ihrer", "ihres", "unser", "unsre", "unsere", "unsern", "unserm",
         "unsres", "euer", "eure", "euren", "eurem", "eurer", "eures"],
        StringComparer.Ordinal);

    /// <summary>Parts of speech (BHSA's and Nestle's) of a word an auxiliary renders by itself.</summary>
    private static readonly HashSet<string> RenderedByAuxiliaries = new(
        ["verb", "pron", "prps", "prde", "prin", "inrg", "nmpr", "intj"], StringComparer.Ordinal);

    /// <summary>Parts of speech of a word no pronoun stands for.</summary>
    private static readonly HashSet<string> NeverReferred = new(
        ["prep", "conj", "adv", "advb", "ptcl", "nega", "det", "art"], StringComparer.Ordinal);

    private static readonly HashSet<string> Nouns = new(["subs", "noun", "adj", "adjv"], StringComparer.Ordinal);

    /// <summary>Numbers a German auxiliary or pronoun does render, whatever their part of speech.</summary>
    private static readonly HashSet<string> Rendered = new(
        ["H3426", "H369", "H5750", "H518", "H3863", "H3884", "G1487", "G1437", "G1535", "H1167"], StringComparer.Ordinal);

    /// <summary>Verbs a German auxiliary or modal renders by itself, so their numbers stay on it.</summary>
    private static readonly HashSet<string> OwnVerbs = new(
        ["H1961", "H1933", "G1510", "G1096", "G2192", "H3426", "G1163", "G3784", "G1410", "H3201", "G2309", "H14"], StringComparer.Ordinal);

    /// <summary>Words that never render the verb whose number the auxiliary or pronoun after them carries.</summary>
    private static readonly HashSet<string> NotVerbs = new(
        [.. Function, "lassen", "läßt", "ließ", "ließen", "lasse", "lasset", "laß", "ließest",
         "also", "nun", "dann", "darum", "daher", "doch", "sondern", "sonst", "hier", "dort", "daselbst", "dahin", "dennoch", "jetzt",
         "wohl", "gar", "immer", "allein", "ebenso", "desgleichen", "abermals", "alsbald", "alsdann", "bald", "zuvor", "hernach",
         "wieder", "nimmer", "niemals", "nie", "oft", "welcher", "welche", "welches", "welchen", "welchem", "was", "wer", "wen", "wem",
         "wessen", "dieser", "diese", "dieses", "diesen", "diesem", "jener", "jene", "jenes", "jenen", "jenem", "neben", "zwischen",
         "hinter", "bis", "seit", "außer", "wegen", "statt", "trotz"],
        StringComparer.Ordinal);

    /// <summary>Whether a German word is a form of <em>sein</em>, <em>haben</em>, <em>werden</em> or a modal verb.</summary>
    public static bool IsAuxiliary(string surface) => Auxiliaries.Contains(surface.ToLowerInvariant());

    public bool AppliesTo(string slug) => slug == EbibleTextSource.Luther;

    public IReadOnlyList<string> Sources(string slug) => [];

    public IReadOnlyList<string> Products(string slug) => [.. Originals.Select(TextWord.ProductOf)];

    /// <summary>What the stage did to the text's numbers.</summary>
    internal sealed record Outcome(int Moved, int Cleared, int Verbs)
    {
        public override string ToString() =>
            $"{Moved} moved to the word before, {Cleared} cleared; {Verbs} verb numbers moved from an auxiliary or pronoun to the verb before it";
    }

    public void Apply(ConvertedText text, string resources)
    {
        var store = new ProductStore(resources);
        var outcome = Place(text.Verses, Classes(store));
        text.Provenance.Add(new("strong-placed",
            $"numbers on German pronouns and auxiliaries that name a word neither renders, by the part of speech {string.Join(" and ", Originals)} give it: {outcome}"));
    }

    /// <summary>Moves or clears the misplaced numbers of <paramref name="verses"/>; <paramref name="classes"/> gives each number its part of speech.</summary>
    internal static Outcome Place(IReadOnlyList<ConvertedVerse> verses, IReadOnlyDictionary<string, string> classes)
    {
        int moved = 0, cleared = 0, verbs = 0;
        foreach (var verse in verses)
        {
            var words = verse.Words;
            var carried = words.Where(w => w.Strong is not null).GroupBy(w => w.Strong!).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            for (var at = 0; at < words.Count; at++)
            {
                var word = words[at];
                if (word.Strong is not { } number || Rendered.Contains(number) || !classes.TryGetValue(number, out var part))
                {
                    continue;
                }

                var before = at > 0 ? words[at - 1] : null;
                var lower = word.Surface.ToLowerInvariant();
                var auxiliary = Auxiliaries.Contains(lower)
                                && !(char.IsUpper(word.Surface[0]) && before is not null && !EndsSentence(before))
                                && !(lower == "sein" && at + 1 < words.Count && !Function.Contains(words[at + 1].Surface));
                if (part == "verb" && !OwnVerbs.Contains(number) && (auxiliary || Subjects.Contains(lower) || Pronouns.Contains(lower))
                    && RendersTheVerb(before))
                {
                    before!.Strong = number;
                    word.Strong = null;
                    verbs++;
                    continue;
                }

                switch (Decide(lower, auxiliary, number, part, before, carried[number]))
                {
                    case Placement.Move:
                        before!.Strong = number;
                        word.Strong = null;
                        moved++;
                        break;
                    case Placement.Clear:
                        word.Strong = null;
                        cleared++;
                        break;
                }
            }
        }

        return new Outcome(moved, cleared, verbs);
    }

    /// <summary>
    /// The verb numbers this stage moves off an auxiliary or a pronoun in a text, each as the word it leaves and the verb
    /// before it that takes it (<em>hat</em> and <em>geboten</em>), by verse and place in the verse (from 1) as the words
    /// product numbers them: the text is read up to this stage and the stage run on it, so it decides as the conversion did.
    /// </summary>
    internal static List<(VerseKey Verse, int Word, int Verb)> VerbMoves(ProductStore store, CataloguedText declared)
    {
        var (text, _) = TextConverter.Staged(store, declared, stage => stage is MisplacedNumbers);
        var before = text.Verses.Select(verse => verse.Words.Select(word => word.Strong).ToArray()).ToList();
        var classes = Classes(store);
        Place(text.Verses, classes);
        var moves = new List<(VerseKey, int, int)>();
        for (var v = 0; v < text.Verses.Count; v++)
        {
            var words = text.Verses[v].Words;
            for (var at = 1; at < words.Count; at++)
            {
                if (before[v][at] is { } number && words[at].Strong is null && before[v][at - 1] is null && words[at - 1].Strong == number
                    && classes.GetValueOrDefault(number) == "verb")
                {
                    moves.Add((text.Verses[v].Key, at + 1, at));
                }
            }
        }

        return moves;
    }

    /// <summary>Whether the word before an auxiliary or pronoun can be the verb whose number it carries: untagged, a content word, in the same clause.</summary>
    private static bool RendersTheVerb(ConvertedWord? before) =>
        before is { Strong: null }
        && !NotVerbs.Contains(before.Surface.ToLowerInvariant())
        && before.Trailer.IndexOfAny([',', ';', ':', '.', '!', '?', '(', ')']) < 0
        && before.Surface.IndexOfAny(['\'', '’']) < 0;

    private enum Placement
    {
        Keep,
        Move,
        Clear,
    }

    private static Placement Decide(string lower, bool auxiliary, string number, string part, ConvertedWord? before, int carried)
    {
        var beforeFree = before is not null && before.Strong is null;
        if (auxiliary && !RenderedByAuxiliaries.Contains(part))
        {
            if (beforeFree && carried == 1 && !Function.Contains(before!.Surface.ToLowerInvariant()))
            {
                return Placement.Move;
            }

            return Modals.Contains(lower) && Nouns.Contains(part) ? Placement.Keep : Placement.Clear;
        }

        var greek = number[0] == 'G';
        if ((Subjects.Contains(lower) || Pronouns.Contains(lower)) && part == "prep" && greek && beforeFree
            && Prepositions.Contains(before!.Surface.ToLowerInvariant()))
        {
            return Placement.Move;
        }

        if (Subjects.Contains(lower) && NeverReferred.Contains(part))
        {
            return Placement.Clear;
        }

        return Pronouns.Contains(lower) && NeverReferred.Contains(part) && (greek || part == "conj") ? Placement.Clear : Placement.Keep;
    }

    private static bool EndsSentence(ConvertedWord word) => word.Trailer.IndexOfAny(['.', '!', '?', ':', '“', '"', '(']) >= 0;

    /// <summary>Each number's part of speech: the one the originals give the words carrying it most often.</summary>
    internal static Dictionary<string, string> Classes(ProductStore store)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var slug in Originals)
        {
            using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
            foreach (var word in reader.Rows())
            {
                if (word.Strong is not { } number || PartOfSpeech(word.Morphology) is not { } part)
                {
                    continue;
                }

                if (!counts.TryGetValue(number, out var parts))
                {
                    counts[number] = parts = new Dictionary<string, int>(StringComparer.Ordinal);
                }

                parts[part] = parts.GetValueOrDefault(part) + 1;
            }
        }

        return counts.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key,
            StringComparer.Ordinal);
    }

    private static string? PartOfSpeech(string? morphology)
    {
        if (morphology is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(morphology);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("pos", out var pos)
                                                                     && pos.ValueKind == JsonValueKind.String
            ? pos.GetString()
            : null;
    }
}
