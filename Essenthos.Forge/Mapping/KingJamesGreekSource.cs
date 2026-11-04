using System.Runtime.CompilerServices;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The King James New Testament against a Greek edition, as <see cref="NewTestamentLinkLoader"/> draws it (DOC-0224 §6.1):
/// the Zefania KJV+ tags laid onto the King James's words (<see cref="TaggedEdition.Align"/> under
/// <see cref="AmericanSpelling.Fold"/>, as the conversion lays them), their Strong numbers matched within the verse
/// (<c>strong</c>), and the untagged English function words given a Greek word by the morphology the edition states
/// (<c>lexical</c>). Reads no database: <c>Zefania/SF_2009-01-20_ENG_KJV_(KJV+).xml</c>, both texts' words and frames and
/// Strong's Greek dictionary.
///
/// <para>
/// **The tags number the verses as the King James prints them**, so a tagged verse is the King James's own verse, and the
/// Greek words are those standing at that verse's frame address (PRB-1458). The words product holds a tag's first number;
/// where it holds another (a correction), the words' number stands. The italics, the translators' own mark that they
/// supplied a word, keep the function words off it; their absences are the stated file's, not these.
/// </para>
/// </summary>
internal sealed class KingJamesGreekSource : IPairSource
{
    public const string Zefania = "Zefania/SF_2009-01-20_ENG_KJV_(KJV+).xml";

    private const string KingJames = "KJV";

    private static readonly ConditionalWeakTable<ProductTexts, Lazy<Dictionary<VerseKey, List<NewTestamentLinkLoader.TaggedWord>>>> Laid = new();

    private readonly bool _lexical;

    private KingJamesGreekSource(bool lexical) => _lexical = lexical;

    /// <summary>The Strong matches and the function words, one source each.</summary>
    public static readonly IReadOnlyList<KingJamesGreekSource> All = [new(lexical: false), new(lexical: true)];

    public string Method => _lexical ? MappingMethods.Lexical : MappingMethods.Strong;

    public string Name => _lexical ? "kjv-function-words" : "kjv-zefania";

    /// <summary>The source's words before the witness's name, and after it.</summary>
    private (string Before, string After) Frame =>
        (_lexical ? NewTestamentLinkLoader.RecoveredSource("\0") : NewTestamentLinkLoader.Source("\0")).Split('\0') is [var before, var after]
            ? (before, after)
            : throw new InvalidOperationException("A King James source names its witness once.");

    public bool States(string pair) =>
        MappingProducts.Split(pair) is var (from, to) && from == KingJames && DatasetLoader.GreekWitnesses.Contains(to);

    public bool Makes(string source) => Witness(source) is not null;

    /// <summary>The witness's name a source of this kind carries, or null for a source of another.</summary>
    private string? Witness(string source)
    {
        var (before, after) = Frame;
        return source.Length > before.Length + after.Length && source.StartsWith(before, StringComparison.Ordinal)
                                                             && source.EndsWith(after, StringComparison.Ordinal)
            ? source[before.Length..^after.Length]
            : null;
    }

    public IEnumerable<string> Inputs(ProductStore store, string pair) =>
        [Zefania, StrongNumberSource.GreekDictionary, .. store.Find(TextCorrections.ProductOf(KingJames)) is { } corrections ? [corrections] : Array.Empty<string>()];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var english = texts[fromText];
        var greek = texts[toText];
        var tagged = Laid.GetValue(texts, t => new Lazy<Dictionary<VerseKey, List<NewTestamentLinkLoader.TaggedWord>>>(() => Lay(t, log))).Value;
        var offset = (long)english.Words.Count;

        var pairs = new List<NewTestamentLinkLoader.VersePair>(8_000);
        int unframed = 0;
        foreach (var (own, tags) in tagged)
        {
            if (!greek.ByAddress.TryGetValue(english.Primary(own), out var at))
            {
                unframed++;
                continue;
            }

            var words = english.InVerse(own)
                .Select(id => new NewTestamentLinkLoader.Word(id, english.Words[id].Surface, english.Words[id].Strong, default)).ToList();
            var witness = at.Select(id => new NewTestamentLinkLoader.Word(offset + id, greek.Words[id].Surface, greek.Words[id].Strong,
                GreekMorphology.Parse(Morphology(greek.Words[id].Morphology)))).ToList();
            pairs.Add(new NewTestamentLinkLoader.VersePair(tags, words, witness));
        }

        var resolution = GreekNumberResolution.Admit(
            StrongNumberSource.GreekEntries(texts.Store),
            pairs.SelectMany(p => p.Greek).Where(w => w.Strong is not null).Select(w => w.Strong!).ToHashSet(StringComparer.Ordinal),
            NewTestamentLinkLoader.Occurrences(pairs));

        var source = Named(texts.Store, pair, toText);
        var rows = new List<MappingRow>(_lexical ? 20_000 : 130_000);
        int unmatched = 0, resolved = 0, phrases = 0;
        foreach (var verse in pairs)
        {
            foreach (var draft in NewTestamentLinkLoader.Build(verse, resolution, ref unmatched, ref resolved, ref phrases))
            {
                var lexical = draft.Kind == NewTestamentLinkLoader.GreekMatch.FunctionWord;
                if (draft.Kind == NewTestamentLinkLoader.GreekMatch.Supplied || lexical != _lexical)
                {
                    continue;
                }

                rows.Add(new MappingRow(MappingText.Keys(draft.English.Select(english.Key)),
                    MappingText.Keys(draft.Greek.Select(id => greek.Key(id - offset))),
                    "renders", Method, draft.Confidence, source, null, null, null));
            }
        }

        log($"{pairs.Count} verses, {rows.Count} rows; {unmatched} tagged words no Greek word in the verse carries, {resolved} matched through "
            + $"the dictionary over {resolution.Count} redirects, {phrases} phrases; {unframed} verses at an address the witness lacks");
        return rows;
    }

    /// <summary>The Robinson code the edition's morphology states (<see cref="GreekMorphology.Of(System.Text.Json.JsonDocument?)"/>).</summary>
    private static string? Morphology(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        return root.TryGetProperty("form", out var form) && form.ValueKind == System.Text.Json.JsonValueKind.String
            ? form.GetString()
            : root.TryGetProperty("robinson", out var robinson) && robinson.ValueKind == System.Text.Json.JsonValueKind.String
                ? robinson.GetString()
                : null;
    }

    /// <summary>
    /// The Zefania tags laid onto each King James verse they agree with, one tag per word (an untagged word has none), as
    /// <see cref="NewTestamentLinkLoader.Project"/> lays them; the first number from the words product.
    /// </summary>
    private static Dictionary<VerseKey, List<NewTestamentLinkLoader.TaggedWord>> Lay(ProductTexts texts, Action<string> log)
    {
        var english = texts[KingJames];
        var added = Added(texts.Store);
        var laid = new Dictionary<VerseKey, List<NewTestamentLinkLoader.TaggedWord>>(8_000);
        int refused = 0, set = 0, cleared = 0;
        foreach (var (address, tags) in NewTestamentLinkLoader.Tagged(texts.Store.FullPath(Zefania)))
        {
            var own = new VerseKey(address.Item1, address.Item2, string.Empty, address.Item3, string.Empty);
            if (!english.Has(own))
            {
                continue;
            }

            // The words the source printed: an epistle's subscription, appended by a correction, is not in the tagged edition.
            var all = english.InVerse(own).ToList();
            var (head, tail) = added.GetValueOrDefault(own);
            var ids = all.Skip(head).Take(all.Count - head - tail).ToList();
            var spans = TaggedEdition.Align([.. tags.Select(tag => tag.Text)], [.. ids.Select(id => english.Words[id].Surface)], AmericanSpelling.Fold);
            if (TaggedEdition.Agreement(spans, ids.Count) < NewTestamentLinkLoader.SameVerse)
            {
                refused++;
                continue;
            }

            var projected = NewTestamentLinkLoader.Project(tags, ids.Count, spans);
            for (var i = 0; i < projected.Count; i++)
            {
                var number = english.Words[ids[i]].Strong;
                var first = projected[i].Numbers.Count > 0 ? projected[i].Numbers[0] : null;
                if (number == first)
                {
                    continue;
                }

                if (number is null)
                {
                    projected[i] = projected[i] with { Numbers = [] };
                    cleared++;
                }
                else if (number[0] == 'G')
                {
                    projected[i] = projected[i] with { Numbers = [number, .. projected[i].Numbers.Skip(1)] };
                    set++;
                }
            }

            laid[own] = [.. Enumerable.Repeat(new NewTestamentLinkLoader.TaggedWord(string.Empty, [], false), head), .. projected,
                .. Enumerable.Repeat(new NewTestamentLinkLoader.TaggedWord(string.Empty, [], false), tail)];
        }

        log($"{Zefania} laid onto {laid.Count} verses, {refused} refused; {set} numbers the words product holds otherwise, {cleared} it clears");
        return laid;
    }

    /// <summary>How many words the King James's corrections put before and after each verse's printed words (<c>prepend</c>, <c>append</c>).</summary>
    private static Dictionary<VerseKey, (int Head, int Tail)> Added(ProductStore store)
    {
        var added = new Dictionary<VerseKey, (int Head, int Tail)>();
        foreach (var row in TextCorrections.Read(store, KingJames).Rows.Where(row => row.Kind is TextCorrection.Prepend or TextCorrection.Append))
        {
            var verse = VerseKey.Parse(row.Own);
            var (head, tail) = added.GetValueOrDefault(verse);
            var count = TextCorrections.Words(row).Count;
            added[verse] = row.Kind == TextCorrection.Prepend ? (head + count, tail) : (head, tail + count);
        }

        return added;
    }

    /// <summary>The source the pair's rows carry, under the witness's name the previous file gave it (scrivener1894), or its slug.</summary>
    private string Named(ProductStore store, string pair, string toText) =>
        StrongNumberSource.Previous(store, pair, Method,
            source => Witness(source) is { } name && TextAliases.Of(toText).Append(toText).Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)))
        ?? (_lexical ? NewTestamentLinkLoader.RecoveredSource(toText) : NewTestamentLinkLoader.Source(toText));
}
