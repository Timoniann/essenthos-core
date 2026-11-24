using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
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
/// runs, so each text's are read once and kept: from what the load counted, a few milliseconds off one
/// index, or from the words themselves on a corpus the load has not counted.
/// </summary>
internal sealed class WordForms(IServiceScopeFactory scopes)
{
    private readonly PerRelease<int, FormIndex> _texts = new((textId, token) => Read(scopes, textId, token));

    public Task<FormIndex> Of(int textId, CancellationToken cancellationToken) => _texts.Get(textId, cancellationToken);

    private static async Task<FormIndex> Read(IServiceScopeFactory scopes, int textId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var language = await db.Texts.Where(t => t.Id == textId).Select(t => t.Language)
            .FirstAsync(cancellationToken);

        return new FormIndex(language, await Spellings(db, textId, cancellationToken));
    }

    internal static async Task<List<WordForm>> Spellings(AppDbContext db, int textId, CancellationToken cancellationToken)
    {
        var counted = await db.TextForms
            .Where(f => f.TextId == textId)
            .Select(f => new WordForm(f.Form, f.Occurrences))
            .ToListAsync(cancellationToken);
        if (counted.Count > 0)
        {
            return counted;
        }

        return await db.Words
            .Where(w => w.TextId == textId && w.NormalisedText != null && !w.Elided)
            .GroupBy(w => w.NormalisedText!)
            .Select(g => new WordForm(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }
}
