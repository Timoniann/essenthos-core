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
/// the links were made over. A language with no stemmer is matched on the spelling alone, and says
/// so by answering one form.
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

    private readonly Func<string, string> _stem;
    private readonly ILookup<string, WordForm> _byStem;

    public FormIndex(string language, IEnumerable<WordForm> spellings)
    {
        Language = language;
        var stemmer = Stemmer(language);
        Stems = stemmer is not null;
        Slavic = language is "rus" or "ru" or "ukr" or "uk";
        _stem = stemmer ?? (word => word);
        _byStem = spellings.ToLookup(form => Key(form.Text), StringComparer.Ordinal);
    }

    public string Language { get; }

    /// <summary>Whether this text's forms are gathered by a stem at all, or matched as spelled.</summary>
    public bool Stems { get; }

    private bool Slavic { get; }

    /// <summary>
    /// The forms of the word a reader typed, commonest first: every spelling sharing its stem, or
    /// the spelling alone where <paramref name="exact"/> asks for it. Folded as the stored words
    /// are, so a capital or an accent typed is a capital or an accent ignored.
    ///
    /// <para>
    /// The Slavic stemmer strips endings a nominative can itself end in, so a short noun loses
    /// letters of its root and meets words it is no form of: <em>любов</em> and <em>любі</em>
    /// (dear) both come to <em>люб</em>, while <em>любові</em> comes to <em>любов</em>. For those
    /// languages the forms are gathered from both stems and kept only where they agree with the
    /// word as typed at least one letter past the stem — <em>любов</em>, <em>любові</em>,
    /// <em>любов'ю</em>, and not <em>любить</em>.
    /// </para>
    /// </summary>
    public IReadOnlyList<WordForm> Matching(string typed, bool exact)
    {
        var folded = Bare(WordFolding.Fold(typed, Language));
        var stem = _stem(folded);

        IEnumerable<WordForm> forms = _byStem[stem];
        if (Slavic)
        {
            var agreeing = Math.Min(folded.Length, stem.Length + 1);
            forms = forms
                .Concat(stem == folded ? [] : _byStem[folded])
                .Where(form => SharedPrefix(Bare(form.Text), folded) >= agreeing);
        }

        if (exact)
        {
            forms = forms.Where(form => Bare(form.Text) == folded);
        }

        return [.. forms.OrderByDescending(form => form.Count).ThenBy(form => form.Text, StringComparer.Ordinal)];
    }

    private string Key(string spelling) => _stem(Bare(spelling));

    private static string Bare(string spelling) =>
        spelling.IndexOfAny(Apostrophes) < 0 ? spelling : string.Concat(spelling.Where(c => !Apostrophes.Contains(c)));

    private static int SharedPrefix(string first, string second)
    {
        var length = 0;
        while (length < first.Length && length < second.Length && first[length] == second[length])
        {
            length++;
        }

        return length;
    }

    /// <summary>
    /// The stemmer the alignment uses for the language, without the switches it keeps for names and
    /// for the closed classes: a reader typing a common word wants its endings stripped and nothing
    /// cleverer.
    /// </summary>
    private static Func<string, string>? Stemmer(string language) => language switch
    {
        "eng" or "en" => EnglishStemmer.Stem,
        "rus" or "ru" or "ukr" or "uk" => word => SlavicStemmer.Stem(word),
        "deu" or "de" => GermanStemmer.Stem,
        "spa" or "es" => SpanishStemmer.Stem,
        _ => null,
    };
}
