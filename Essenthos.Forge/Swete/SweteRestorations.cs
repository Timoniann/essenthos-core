namespace Essenthos.Core.Swete;

/// <param name="Book">The file the words stand in, by the name the edition's folder gives it.</param>
/// <param name="Digitised">
/// The tokens as the transcription has them, punctuation attached and separated by single spaces.
/// They must stand in the verse exactly once, which is what keeps a restoration from landing
/// anywhere but the place it was read for.
/// </param>
/// <param name="Printed">What the edition prints in their place, in the same form.</param>
/// <param name="Why">What establishes the printed words, which is the whole of the claim.</param>
/// <param name="Label">The letter after the verse number, where the edition prints one.</param>
internal sealed record SweteRestoration(
    string Book,
    int Chapter,
    int Verse,
    string Digitised,
    string Printed,
    string Why,
    string Label = "");

/// <summary>
/// Words Swete printed and the transcription lost, put back where two witnesses independent of the
/// transcription agree on them.
///
/// <para>
/// The loss is the source's and not the reader's: these words are absent from the file's own lines,
/// which carry every token they hold. The transcription is First1KGreek's reading of the printed
/// page, and in Genesis it drops whole words — a number word at 9:28 and 12:4, Nahor's and Terah's
/// names in the genealogy of chapter 11, a verb, the last word of a verse — besides misreading
/// letters in words it keeps. Read as it stands, Noah's life does not add up and Abram leaves Haran
/// at five.
/// </para>
///
/// <para>
/// <strong>A word is restored only where it is established, and the test is strict.</strong>
/// Brenton's Greek and the GLAUx treebank, whose Greek is Wikisource's and owes nothing to this
/// transcription, must read the same word at the same place; the verse must show the hole — a
/// number that does not close, a verb with no subject, a formula every sibling verse completes; and
/// the spelling is the one Swete himself prints in the verses beside it, never Brenton's. Where the
/// two witnesses disagree, as they do at 11:3 over whether the neighbour is αὐτοῦ, nothing is
/// restored, because what Swete printed there is then not something the corpus can know without the
/// page.
/// </para>
///
/// <para>
/// <strong>Misread letters are left as they are.</strong> ἴτη, ὄη and ἣ for ἔτη, τριόκοντα and
/// τριοκόσια, Φόλεκ, Φάρα, Θορὰ, Σόλο, κοὶ, θηατέρnς: each is a word the transcription kept and
/// spelled wrong, and correcting spelling against another edition is exactly how a second witness
/// stops being one. Only a token that is two words run together is divided, because the words are
/// both there.
/// </para>
///
/// <para>
/// These are this project's readings, not the transcription's, and the text's rights note says so
/// wherever the edition is served: the licence is CC BY-SA 4.0, which asks that a change be
/// indicated.
/// </para>
/// </summary>
internal static class SweteRestorations
{
    private const string Genesis = "01.Genesis";

    private const string Witnesses = "Brenton's Greek and GLAUx's both read";

    /// <summary>What the text's row says about these words, on a cold load and on a warm one.</summary>
    public const string Note =
        "Modified: words the transcription lost in the genealogies and the tower of Babel of Genesis 5, "
        + "9, 11 and 12 — among them Noah's three hundred and fifty years and Abram's seventy-five — are "
        + "restored by Essenthos where Brenton's Greek and the GLAUx treebank read the same words, in the "
        + "spelling Swete prints beside them. Letters the transcription misread are left as it reads them.";

    /// <summary>
    /// These, the corrections a rule settles, and the words read back off the printed page, which are
    /// made the same way and in this order: a page restoration names its place in the verse as the
    /// corrections leave it.
    /// </summary>
    public static readonly IReadOnlyList<SweteRestoration> BeforeChapterMarkers = [.. Lost(), .. SweteCorrections.All, .. SwetePage.All];

    public static readonly IReadOnlyList<SweteRestoration> ChapterMarkers =
    [
        new("12.Regnorum_II", 19, 43, "Ἰσραήλ. XX", "Ἰσραήλ.",
            "The duplicated chapter XX marker also opens the next source block, 20:1 XXυἱὸς; it is not a word of 19:43."),
    ];

    public const string ChapterMarkersNote =
        "Modified: the duplicated Roman chapter XX marker at the end of Second Samuel 19:43 is omitted by Essenthos; the transcription also runs it into the first word of chapter 20.";

    public static readonly IReadOnlyList<SweteRestoration> All = [.. BeforeChapterMarkers, .. ChapterMarkers];

    /// <summary>
    /// The restorations as earlier passes made them, oldest first: the Genesis words alone, then with
    /// the letter corrections, then with the words read off the page, before the figures were taken
    /// out. A corpus restored by one of them reads as that set and not as the transcription, and the
    /// pass carries it on to <see cref="All"/> rather than refusing it.
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<SweteRestoration>> Earlier =
    [
        Lost(),
        [.. Lost(), .. SweteCorrections.First],
        [.. Lost(), .. SweteCorrections.First, .. SwetePage.All],
        BeforeChapterMarkers,
    ];

    private static IReadOnlyList<SweteRestoration> Lost() =>
    [
        new(Genesis, 5, 15, "ἔτη, ἐγέννησεν", "ἔτη, καὶ ἐγέννησεν",
            $"{Witnesses} καὶ ἐγέννησε(ν), as every other begetting of the chapter does."),
        new(Genesis, 9, 28, "τριακόσια ἔτη.", "τριακόσια πεντήκοντα ἔτη.",
            $"{Witnesses} τριακόσια πεντήκοντα, the Hebrew 350, and 9:29's nine hundred and fifty is the "
            + "six hundred of 7:6 and these; πεντήκοντα as 9:29 prints it."),
        new(Genesis, 11, 4, "Δεῦτε μὲν ἐαυτοῖς", "Δεῦτε οἰκοδομήσωμεν ἐαυτοῖς",
            $"{Witnesses} δεῦτε οἰκοδομήσωμεν ἑαυτοῖς; μὲν is the verb's last syllable, and the sentence "
            + "has no verb without it."),
        new(Genesis, 11, 5, "πύργον οἱ", "πύργον ὃν ᾠκοδόμησαν οἱ",
            $"{Witnesses} ὃν ᾠκοδόμησαν, without which the sons of men stand in the verse with nothing to do."),
        new(Genesis, 11, 7, "καταβάν τες ἐκεῖ", "καταβάντες συγχέωμεν ἐκεῖ",
            $"{Witnesses} καταβάντες συγχέωμεν, the verb 11:9 names the place after; καταβάντες is one "
            + "word split in two."),
        new(Genesis, 11, 9, "συνέχεεν κύριος πάσης", "συνέχεεν κύριος τὰ χείλη πάσης",
            $"{Witnesses} τὰ χείλη, the object of συνέχεεν."),
        new(Genesis, 11, 10, "ἑκατὸν τὸν Ἀρφαξάδ,", "ἑκατὸν ὅτε ἐγέννησεν τὸν Ἀρφαξάδ,",
            $"{Witnesses} ὅτε ἐγέννησε(ν) τὸν Ἀρφαξάδ, and the verse has no verb for Arphaxad without it; "
            + "ἐγέννησεν as the chapter prints it."),
        new(Genesis, 11, 12, "Καὶ ἔζησεν ἑκατὸν", "Καὶ ἔζησεν Ἀρφαξὰδ ἑκατὸν",
            $"{Witnesses} Ἀρφαξάδ, the subject every begetting of the genealogy names; spelled as 11:13 "
            + "prints it."),
        new(Genesis, 11, 15, "τὸηἜβερ", "τὸν Ἔβερ",
            "Two words run together, the article misread; every witness reads τὸν Ἕβερ."),
        new(Genesis, 11, 21, "θυγατέρας, καὶ", "θυγατέρας, καὶ ἀπέθανεν.",
            $"{Witnesses} καὶ ἀπέθανε(ν), which ends every such verse of the chapter; spelled as 11:23 prints it."),
        new(Genesis, 11, 22, "ἐγέντὸν Ναχώρ.", "ἐγέννησεν τὸν Ναχώρ.",
            $"{Witnesses} ἐγέννησε(ν) τὸν Ναχώρ; the token is the verb's first syllables run into the article."),
        new(Genesis, 11, 23, "αὐτὸν τὸν ἔτη", "αὐτὸν τὸν Ναχὼρ ἔτη",
            $"{Witnesses} τὸν Ναχώρ, the son 11:22 names; spelled as 11:24 prints it."),
        new(Genesis, 11, 25, "τὸν αὐτὸν ἴτη", "τὸν Θαρά ἴτη",
            $"{Witnesses} τὸν Θάρα, the son 11:24 names, where the transcription repeats αὐτὸν; spelled as "
            + "11:24 and 11:32 print it."),
        new(Genesis, 11, 29, "ἐαυτοῖς ὄνομα", "ἐαυτοῖς γυναῖκας· ὄνομα",
            $"{Witnesses} ἑαυτοῖς γυναῖκας, what the two brothers took."),
        new(Genesis, 11, 29, "Ναχώρ ἁ,", "Ναχώρ Μελχά,",
            $"{Witnesses} Μελχά, whom the same verse names again; ἁ is what is left of the name."),
        new(Genesis, 12, 4, "καθάπερ κύριος,", "καθάπερ ἐλάλησεν αὐτῷ κύριος,",
            $"{Witnesses} καθάπερ ἐλάλησεν αὐτῷ Κύριος."),
        new(Genesis, 12, 4, "ἐτῶν πέντε", "ἐτῶν ἑβδομήκοντα πέντε",
            $"{Witnesses} seventy-five and the Hebrew 75; ἑβδομήκοντα as 11:26 prints it."),
    ];

    /// <summary>The books any restoration is made in, which are the only files worth reading twice.</summary>
    public static IEnumerable<string> Books => All.Select(r => r.Book).Distinct();

    /// <summary>
    /// A file's lines with its restorations made: the same references, with the printed tokens in
    /// place of the digitised ones. Everything else passes through unchanged and in order.
    /// </summary>
    public static IEnumerable<string> Apply(string book, IEnumerable<string> lines) => Apply(book, lines, All);

    /// <summary><see cref="Apply(string, IEnumerable{string})"/> with one set of restorations rather than all of them.</summary>
    public static IEnumerable<string> Apply(string book, IEnumerable<string> lines, IReadOnlyList<SweteRestoration> set)
    {
        var restorations = set.Where(r => r.Book == book).ToList();
        if (restorations.Count == 0)
        {
            foreach (var line in lines)
            {
                yield return line;
            }

            yield break;
        }

        var applied = new HashSet<SweteRestoration>();
        string? reference = null;
        var tokens = new List<string>();

        foreach (var line in lines)
        {
            var space = line.IndexOf(' ');
            if (space < 0)
            {
                // Not a line of the edition; the reader says why, so it passes through to be refused there.
                foreach (var flushed in Flush(reference, tokens, restorations, applied))
                {
                    yield return flushed;
                }

                reference = null;
                tokens.Clear();
                yield return line;
                continue;
            }

            var at = line[..space];
            if (reference != at)
            {
                foreach (var flushed in Flush(reference, tokens, restorations, applied))
                {
                    yield return flushed;
                }

                reference = at;
                tokens.Clear();
            }

            tokens.Add(line[(space + 1)..]);
        }

        foreach (var flushed in Flush(reference, tokens, restorations, applied))
        {
            yield return flushed;
        }

        var missed = restorations.Where(r => !applied.Contains(r)).ToList();
        if (missed.Count > 0)
        {
            throw new InvalidOperationException(
                $"{book} no longer reads as the transcription these restorations were made against: " +
                string.Join("; ", missed.Select(r => $"{r.Chapter}:{r.Verse}{r.Label} \"{r.Digitised}\"")) +
                ". The files were fetched again at another commit, or the verse moved. Read the verse " +
                $"afresh and correct or remove the entry in {nameof(SweteRestorations)} — upstream may " +
                "have restored the words itself.");
        }
    }

    private static IEnumerable<string> Flush(
        string? reference,
        List<string> tokens,
        List<SweteRestoration> restorations,
        HashSet<SweteRestoration> applied)
    {
        if (reference is null)
        {
            yield break;
        }

        var parts = reference.Split('.');
        var here = parts.Length == 3
            ? restorations.Where(r => parts[1] == r.Chapter.ToString() && parts[2] == $"{r.Verse}{r.Label}").ToList()
            : [];

        var result = tokens;
        foreach (var restoration in here)
        {
            result = Restore(result, restoration);
            applied.Add(restoration);
        }

        foreach (var token in result)
        {
            yield return $"{reference} {token}";
        }
    }

    private static List<string> Restore(List<string> tokens, SweteRestoration restoration)
    {
        var digitised = restoration.Digitised.Split(' ');
        var found = new List<int>(1);
        for (var i = 0; i + digitised.Length <= tokens.Count; i++)
        {
            if (tokens.Skip(i).Take(digitised.Length).SequenceEqual(digitised, StringComparer.Ordinal))
            {
                found.Add(i);
            }
        }

        if (found.Count != 1)
        {
            throw new InvalidOperationException(
                $"{restoration.Book} {restoration.Chapter}:{restoration.Verse}{restoration.Label} holds \"{restoration.Digitised}\" " +
                $"{found.Count} times where a restoration needs it exactly once: \"{string.Join(' ', tokens)}\". " +
                $"Lengthen the entry in {nameof(SweteRestorations)} until it names one place.");
        }

        return
        [
            .. tokens.Take(found[0]),
            .. restoration.Printed.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            .. tokens.Skip(found[0] + digitised.Length),
        ];
    }
}
