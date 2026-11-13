using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.Corpus;

/// <summary>One spelling a text prints, and how many times it prints it.</summary>
internal sealed record WordForm(string Text, int Count);

/// <summary>
/// A text's spellings gathered as a reader's word asks for them: by the stem the corpus's stemmers give, by the endings
/// of a Russian or Ukrainian word (<see cref="SlavicParadigm"/>), or as spelled.
/// </summary>
internal sealed class FormIndex
{
    /// <summary>
    /// Written three ways across the Ukrainian texts, and part of no stem: <em>любов'ю</em> is a
    /// form of <em>любов</em> however its apostrophe is typed.
    /// </summary>
    private static readonly char[] Apostrophes = ['\'', '’', 'ʼ'];

    private readonly Func<string, IEnumerable<WordForm>> _formsOf;

    public FormIndex(string language, IEnumerable<WordForm> spellings)
    {
        Language = language;
        var forms = spellings.ToList();
        if (language is "rus" or "ru" or "ukr" or "uk")
        {
            Stems = true;
            var russian = language is "rus" or "ru";
            var bySpelling = forms.ToLookup(form => Bare(form.Text), StringComparer.Ordinal);
            var byParadigm = forms
                .SelectMany(form => SlavicParadigm.Stems(Bare(form.Text), russian).Select(stem => (stem, form)))
                .ToLookup(pair => pair.stem, pair => pair.form);
            _formsOf = folded => bySpelling[folded]
                .Concat(SlavicParadigm.Stems(folded, russian).SelectMany(stem => byParadigm[stem]))
                .Distinct();
        }
        else
        {
            var stemmer = Stemmer(language);
            Stems = stemmer is not null;
            var stem = stemmer ?? (word => word);
            var byStem = forms.ToLookup(form => stem(Bare(form.Text)), StringComparer.Ordinal);
            _formsOf = folded => byStem[stem(folded)];
        }
    }

    public string Language { get; }

    /// <summary>Whether this text's forms are gathered by a stem at all, or matched as spelled.</summary>
    public bool Stems { get; }

    /// <summary>
    /// The forms of the word a reader typed, commonest first: every spelling sharing its stem, or
    /// the spelling alone where <paramref name="exact"/> asks for it. Folded as the stored words
    /// are, so a capital or an accent typed is a capital or an accent ignored.
    ///
    /// <para>
    /// Russian and Ukrainian are not gathered by the aligner's stemmer, which lands a verb on the
    /// noun it is made from and loses a noun's own cases: they are read by their endings,
    /// <see cref="SlavicParadigm"/>, so <em>завіт</em> answers <em>завіту</em> and
    /// <em>завіти</em> and not <em>завітати</em>.
    /// </para>
    /// </summary>
    public IReadOnlyList<WordForm> Matching(string typed, bool exact)
    {
        var folded = Bare(WordFolding.Fold(typed, Language));

        var forms = _formsOf(folded);

        if (exact)
        {
            forms = forms.Where(form => Bare(form.Text) == folded);
        }

        return [.. forms.OrderByDescending(form => form.Count).ThenBy(form => form.Text, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Whether one request of <see cref="Matching"/> can gather two of <paramref name="spellings"/> together. Outside
    /// Russian and Ukrainian two spellings gather together exactly when they share a stem. There a typed word gathers a
    /// spelling by its letters or by a stem they share, and every stem of a word is a beginning of it, so two spellings
    /// are answered together only if they are spelled alike or a stem of one begins a stem of the other: true is said of
    /// every pair that can be, and of a few that cannot.
    /// </summary>
    public static bool Together(string language, IReadOnlyCollection<string> spellings)
    {
        if (spellings.Count < 2)
        {
            return false;
        }

        if (language is "rus" or "ru" or "ukr" or "uk")
        {
            var russian = language is "rus" or "ru";
            var read = spellings.Select(Bare).Distinct(StringComparer.Ordinal).ToList();
            if (read.Count < spellings.Count)
            {
                return true;
            }

            var stems = read.Select(word => SlavicParadigm.Stems(word, russian).Select(stem => stem.Stem).Distinct().ToList()).ToList();
            for (var one = 0; one < stems.Count; one++)
            {
                for (var other = one + 1; other < stems.Count; other++)
                {
                    if (stems[one].Any(a => stems[other].Any(b => a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal))))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        var stemmer = Stemmer(language) ?? (word => word);
        return spellings.Select(spelling => stemmer(Bare(spelling))).Distinct(StringComparer.Ordinal).Count() < spellings.Count;
    }

    private static string Bare(string spelling) =>
        (spelling.IndexOfAny(Apostrophes) < 0 ? spelling : string.Concat(spelling.Where(c => !Apostrophes.Contains(c))))
        .Replace('ё', 'е');

    /// <summary>
    /// The stemmer the alignment uses for the language, without the switches it keeps for names and
    /// for the closed classes: a reader typing a common word wants its endings stripped and nothing
    /// cleverer.
    /// </summary>
    private static Func<string, string>? Stemmer(string language) => language switch
    {
        "eng" or "en" => EnglishStemmer.Stem,
        "deu" or "de" => GermanStemmer.Stem,
        "spa" or "es" => SpanishStemmer.Stem,
        _ => null,
    };
}
