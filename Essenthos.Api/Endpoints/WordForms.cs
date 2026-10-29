using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Loading.Links;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Every spelling a text prints, gathered by the stem the corpus's own stemmers give it — so that a
/// reader asking about <em>love</em> is answered about <em>loved</em>, <em>loveth</em> and
/// <em>lovest</em> too, and one asking about <em>любов</em> about <em>любові</em> and
/// <em>любов'ю</em>.
///
/// The stemmers are the ones the alignment reduces words with, so a form gathered here is a form
/// the links were made over; Russian and Ukrainian are read by their endings instead
/// (<see cref="SlavicParadigm"/>). A language with neither is matched on the spelling alone, and
/// says so by answering one form.
///
/// A text has a few tens of thousands of spellings and the corpus is read-only while the process
/// runs, so each text's are read once, on the index that leads with the text, and kept.
/// </summary>
internal sealed class WordForms(IServiceScopeFactory scopes)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, FormIndex> _texts = [];

    public async Task<FormIndex> Of(int textId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_texts.TryGetValue(textId, out var index))
            {
                index = await Read(textId, cancellationToken);
                _texts[textId] = index;
            }

            return index;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FormIndex> Read(int textId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var language = await db.Texts.Where(t => t.Id == textId).Select(t => t.Language)
            .FirstAsync(cancellationToken);
        var spellings = await db.Words
            .Where(w => w.TextId == textId && w.NormalisedText != null && !w.Elided)
            .GroupBy(w => w.NormalisedText!)
            .Select(g => new WordForm(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return new FormIndex(language, spellings);
    }
}

/// <summary>One spelling a text prints, and how many times it prints it.</summary>
internal sealed record WordForm(string Text, int Count);

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
