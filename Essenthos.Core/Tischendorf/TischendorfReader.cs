using System.Text;

namespace Essenthos.Core.Tischendorf;

/// <param name="Kethiv">
/// The word as the printed Tischendorf has it. The edition was, in its editor's words,
/// typographically challenged, and its digital edition keeps even the clear errors as printed.
/// </param>
/// <param name="Qere">
/// What the digital edition's editor thinks it should have been. Equal to <paramref name="Kethiv"/>
/// for all but eleven words in the New Testament, and the parse follows this one either way.
/// </param>
/// <param name="Strong">The number as the file writes it, without the language letter.</param>
/// <param name="StrongLemma">The headword as Strong's dictionary spells it — Δαβίδ, not Δαυίδ.</param>
/// <param name="Lemma">
/// The headword as Friberg, Friberg and Miller's analytical lexicon spells it, which is the one
/// upstream's own OSIS export publishes and the one that stands closest to what Nestle carries.
/// </param>
/// <param name="Homonym">
/// The roman numeral the analytical lexicon uses to tell two identical headwords apart — δοῦλος
/// the noun from δοῦλος the adjective. Kept off <paramref name="Lemma"/>, because a lemma is an
/// identifier and 201 words spelt "δοῦλος (II)" are 201 words no other text can ever join.
/// </param>
/// <param name="Break">
/// <c>P</c> where the edition breaks a paragraph at this word, <c>C</c> where it breaks a chapter,
/// and null in between.
/// </param>
internal sealed record TischendorfWord(
    int Index,
    string Kethiv,
    string Qere,
    string Morphology,
    string Strong,
    string StrongLemma,
    string Lemma,
    string? Homonym,
    string? Break);

internal sealed record TischendorfVerse(int Chapter, int Number, IReadOnlyList<TischendorfWord> Words);

/// <param name="Doubled">
/// The addresses the file writes twice, most recently first. Tischendorf printed the woman taken in
/// adultery in two forms and this file carries both at the same verse numbers, so a reader that did
/// not notice would load John 7:53-8:11 with every word in it doubled.
/// </param>
internal sealed record TischendorfBook(
    IReadOnlyList<TischendorfVerse> Verses,
    IReadOnlyList<(int Chapter, int Verse)> Doubled);

/// <summary>
/// Tischendorf's eighth edition, one word to a line.
///
///     MT 1:1.1 C Βίβλος Βίβλος N-NSF 976 βίβλος ! βίβλος
///
/// Book, then the address with the word's place in the verse after a dot, then the break column,
/// then the word as printed and the word as corrected, then Robinson's parse code, the Strong
/// number, and two lemmas separated by an exclamation mark. The fields are space-separated and the
/// last one is not: a lemma can be several words — Ἄρειος Πάγος, μαρὰν ἀθά — so the tail of the
/// line is the lemma pair and cannot be split on spaces like the rest.
/// </summary>
internal static class TischendorfReader
{
    /// <summary>What separates the two lemmas, spaces included, because a lemma may contain one.</summary>
    private const string BetweenLemmas = " ! ";

    /// <summary>The fields before the lemma pair, which are one token each.</summary>
    private const int FixedFields = 7;

    /// <summary>Where the address stands, and where the lemma pair begins.</summary>
    private const int Address = 1;

    public static TischendorfBook Read(string content)
    {
        var runs = new List<TischendorfVerse>(1_200);
        var words = new List<TischendorfWord>(32);
        var chapter = 0;
        var number = 0;

        foreach (var line in content.Split('\n'))
        {
            if (Word(line) is not { } parsed)
            {
                continue;
            }

            // A word numbered one opens a verse. Everywhere else in the file the index runs on, so
            // this is what tells the two printings of the woman taken in adultery apart without
            // anyone naming them: the second one restarts at one under an address already written.
            if (parsed.Word.Index == 1 && words.Count > 0)
            {
                runs.Add(new TischendorfVerse(chapter, number, words));
                words = [];
            }

            (chapter, number) = (parsed.Chapter, parsed.Number);
            words.Add(parsed.Word);
        }

        if (words.Count > 0)
        {
            runs.Add(new TischendorfVerse(chapter, number, words));
        }

        return Once(runs);
    }

    /// <summary>
    /// One verse per address, and which addresses had to be reduced to get there.
    ///
    /// The later printing wins. Tischendorf gives the pericope adulterae twice — the ordinary form,
    /// and beneath it the divergent text of Codex Bezae — and this file carries the Bezan one first,
    /// unaccented and with its itacisms, then the ordinary one accented like the rest of the
    /// edition. The corpus holds one verse per address, so the ordinary form is the one loaded and
    /// the caller is told which addresses the choice was made at rather than the choice being
    /// silent.
    /// </summary>
    private static TischendorfBook Once(List<TischendorfVerse> runs)
    {
        var latest = new Dictionary<(int, int), TischendorfVerse>(runs.Count);
        var doubled = new List<(int Chapter, int Verse)>();

        foreach (var run in runs)
        {
            var address = (run.Chapter, run.Number);
            if (!latest.TryAdd(address, run))
            {
                latest[address] = run;
                doubled.Add(address);
            }
        }

        return new TischendorfBook([.. latest.Values], doubled);
    }

    private static (int Chapter, int Number, TischendorfWord Word)? Word(string line)
    {
        var trimmed = line.Trim('\r');
        if (trimmed.Length == 0)
        {
            return null;
        }

        var fields = trimmed.Split(' ');
        if (fields.Length < FixedFields + 1)
        {
            throw new InvalidOperationException(
                $"\"{trimmed}\" has {fields.Length} fields and a line of this edition has at least "
                + $"{FixedFields + 1}: the book, the address, the break, the printed word, the corrected "
                + "word, the parse, the Strong number and the two lemmas. A file with fewer is not the "
                + "word-per-line release this reader was written for.");
        }

        var (chapter, number, index) = Reference(fields[Address], trimmed);
        var (strongLemma, lemma) = Lemmas(string.Join(' ', fields.Skip(FixedFields)), trimmed);
        var (bare, homonym) = WithoutHomonym(lemma);

        return (chapter, number, new TischendorfWord(
            Index: index,
            Kethiv: Normalised(fields[3]),
            Qere: Normalised(fields[4]),
            Morphology: fields[5],
            Strong: fields[6],
            StrongLemma: Normalised(strongLemma),
            Lemma: Normalised(bare),
            Homonym: homonym,
            Break: fields[2] == "." ? null : fields[2]));
    }

    /// <summary>
    /// Composed, because a lemma is an identifier and a decomposed one joins to nothing. This
    /// release is composed already — every one of its 137,711 words and both of their lemmas — so
    /// this costs a comparison and buys the guarantee for whatever release comes next. PRB-0384 is
    /// the day the corpus spent finding that out from the other side.
    /// </summary>
    private static string Normalised(string value) => value.Normalize(NormalizationForm.FormC);

    private static (int Chapter, int Number, int Index) Reference(string reference, string line)
    {
        var dot = reference.IndexOf('.', StringComparison.Ordinal);
        var colon = reference.IndexOf(':', StringComparison.Ordinal);

        if (dot < 0 || colon < 0 || colon > dot)
        {
            throw new InvalidOperationException(
                $"\"{reference}\" in \"{line}\" is not an address of this edition, which writes them as "
                + "chapter:verse.word.");
        }

        return (
            int.Parse(reference[..colon]),
            int.Parse(reference[(colon + 1)..dot]),
            int.Parse(reference[(dot + 1)..]));
    }

    private static (string Strong, string Anlex) Lemmas(string tail, string line)
    {
        var at = tail.IndexOf(BetweenLemmas, StringComparison.Ordinal);
        if (at < 0)
        {
            throw new InvalidOperationException(
                $"\"{tail}\" in \"{line}\" holds one lemma and this edition writes two, the Strong "
                + "headword and the analytical lexicon's, separated by \" ! \".");
        }

        return (tail[..at], tail[(at + BetweenLemmas.Length)..]);
    }

    private static (string Lemma, string? Homonym) WithoutHomonym(string lemma)
    {
        var open = lemma.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && lemma.EndsWith(')')
            ? (lemma[..open], lemma[(open + 2)..^1])
            : (lemma, null);
    }
}
