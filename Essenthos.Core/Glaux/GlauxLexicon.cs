using Essenthos.Core.TextusReceptus;

namespace Essenthos.Core.Glaux;

/// <summary>
/// A folded Greek word form, and the lemma GLAUx gives it most of the time.
/// </summary>
/// <param name="Lemma">The folded lemma, ready to join against a lemma list folded the same way.</param>
/// <param name="Share">
/// How much of the evidence that lemma holds, between 0 and 1. One means every occurrence in the
/// corpus agreed. Kept rather than thresholded away, because the caller decides what it is willing
/// to write and a link that records its confidence can be reconsidered later; a form that was
/// silently dropped cannot.
/// </param>
/// <param name="Occurrences">How often the form occurs in GLAUx at all — the weight behind the share.</param>
internal readonly record struct LemmaChoice(string Lemma, double Share, int Occurrences);

/// <summary>
/// The Septuagint's missing anchor, as a dictionary rather than as a text.
///
/// The corpus already serves Brenton, which is public domain and has no lemmas. GLAUx has lemmas
/// over a different edition of the same book, and 99.38% of Brenton's tokens are written the same
/// way somewhere in it. So GLAUx is read as a **form-to-lemma table** and its own Greek is never
/// loaded — which keeps a text whose transcription provenance Wikisource does not document out of
/// the corpus, and confines what we take from GLAUx to lexical facts. DOC-0161 has the licence.
///
/// A form is ambiguous when the corpus lemmatises it more than one way — <em>αὐτοῦ</em> the
/// pronoun against <em>αὐτοῦ</em> the adverb — so the table records the leading lemma and the share
/// of the evidence behind it instead of picking one and forgetting there was a choice.
/// </summary>
internal static class GlauxLexicon
{
    /// <summary>
    /// The folded form of every word GLAUx lemmatises, against its leading lemma.
    /// </summary>
    public static Dictionary<string, LemmaChoice> Build(IEnumerable<GlauxWord> words)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var word in words)
        {
            var form = GreekLetters.Bare(word.Form);
            var lemma = GreekLetters.Bare(word.Lemma);
            if (form.Length == 0 || lemma.Length == 0)
            {
                continue;
            }

            if (!counts.TryGetValue(form, out var lemmas))
            {
                lemmas = new Dictionary<string, int>(StringComparer.Ordinal);
                counts[form] = lemmas;
            }

            lemmas[lemma] = lemmas.GetValueOrDefault(lemma) + 1;
        }

        var lexicon = new Dictionary<string, LemmaChoice>(counts.Count, StringComparer.Ordinal);
        foreach (var (form, lemmas) in counts)
        {
            var total = 0;
            var leader = string.Empty;
            var best = 0;
            foreach (var (lemma, count) in lemmas)
            {
                total += count;
                if (count > best || (count == best && string.CompareOrdinal(lemma, leader) < 0))
                {
                    best = count;
                    leader = lemma;
                }
            }

            lexicon[form] = new LemmaChoice(leader, (double)best / total, total);
        }

        return lexicon;
    }
}
