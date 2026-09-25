using System.Diagnostics;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

internal sealed record BhsaLemmaOutcome(int Words, TimeSpan Elapsed)
{
    public override string ToString() => Words == 0
        ? "BHSA's lemmas are already its dictionary forms"
        : $"BHSA: {Words} lemmas written as the dictionary form, the occurrence's spelling kept beside it, in {Elapsed}";
}

/// <summary>
/// Puts BHSA's dictionary form in <c>word.lemma</c> on a corpus that loaded the lexeme as the
/// occurrence spells it.
///
/// <para>
/// ETCBC has two features for one lexeme. <c>g_lex_utf8</c> is the lexeme as this occurrence writes
/// it — prefixes gone, affixes cut, the occurrence's pointing kept — so Elohim's is <c>אֱלֹה</c> and
/// "be" is <c>הִי</c>, which are not Hebrew words. <c>voc_lex_utf8</c> is the headword, the form a
/// dictionary and every other witness's lemma mean. A cold load reads the second into the column and
/// keeps the first in the morphology as <c>realisedLexeme</c>; this does the same to the rows a warm
/// corpus already holds, in place, so no word id changes and no link moves.
/// </para>
///
/// <para>
/// One statement over the whole text, guarded on any word whose lemma is not its headword: after it
/// there is none, so the next run finds nothing. The headword is already in every word's morphology,
/// which is why nothing has to be read again from ETCBC's files.
/// </para>
/// </summary>
internal sealed class BhsaLemmaLoader(AppDbContext db, ILogger<BhsaLemmaLoader> logger)
{
    /// <summary>Rewriting four hundred thousand rows takes longer than the default thirty seconds.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

    public async Task<BhsaLemmaOutcome> Load(CancellationToken cancellationToken = default)
    {
        var text = await db.Texts
            .Where(t => t.Slug == BhsaTextSource.Slug)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (text is null)
        {
            return new BhsaLemmaOutcome(0, TimeSpan.Zero);
        }

        db.Database.SetCommandTimeout(Timeout);
        var pending = await db.Database
            .SqlQueryRaw<bool>(
                """
                SELECT EXISTS (
                    SELECT 1 FROM word
                    WHERE text_id = {0} AND morphology ? 'vocalizedLexeme'
                      AND lemma IS DISTINCT FROM morphology ->> 'vocalizedLexeme') AS "Value"
                """,
                text.Value)
            .SingleAsync(cancellationToken);
        if (!pending)
        {
            logger.LogInformation("BHSA's lemmas are already its dictionary forms; nothing to do");
            return new BhsaLemmaOutcome(0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var words = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE word
            SET morphology = CASE WHEN lemma IS NULL THEN morphology
                                  ELSE morphology || jsonb_build_object('realisedLexeme', lemma) END,
                lemma = morphology ->> 'vocalizedLexeme'
            WHERE text_id = {0} AND morphology ? 'vocalizedLexeme' AND NOT morphology ? 'realisedLexeme'
            """,
            [text.Value],
            cancellationToken);

        var outcome = new BhsaLemmaOutcome(words, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }
}
