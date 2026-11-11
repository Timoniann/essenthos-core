using Essenthos.Core.Loading;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Glaux;

/// <summary>
/// Lemmas for the Septuagint, which is the one text in the corpus that arrived without any.
///
/// Brenton is public domain and unannotated: 577,361 words with no lemma, no morphology and no
/// Strong number, so every method that needs a dictionary form stops at the Greek New Testament.
/// GLAUx annotates a *different* edition of the same book, and 99.4% of Brenton's tokens are
/// written the same way somewhere in it — so GLAUx is read as a **form-to-lemma dictionary** and
/// its own Greek is never loaded. That keeps a text whose transcription provenance Wikisource does
/// not document out of the corpus and confines what we take to lexical facts. GLAUx is CC BY-SA —
/// 3.0 on the source text, 4.0 on the annotation — which the owner accepted on 2026-09-03.
///
/// <para>
/// **A form is only lemmatised where the evidence agrees.** GLAUx lemmatises αὐτοῦ as a pronoun in
/// one place and as an adverb in another, and a table that kept only the winner would state a fact
/// where there was a vote. So the lexicon records the leading lemma and the share of occurrences
/// behind it, and the conversion (<see cref="Conversion.SeptuagintLemmas"/>) writes only what four fifths of the evidence agrees on. Measured over the
/// whole text, that threshold costs 2.1 points of coverage against taking every leader and buys
/// back the forms where GLAUx itself is unsure — and 90% would cost less than a point more, so it
/// is not a knife edge.
/// </para>
///
/// <para>
/// **The Strong number is deliberately not written.** It is derivable from the lemma, but it would
/// be *our* inference, and everywhere else in this corpus <c>word.strong_number</c> means a source
/// said so. Writing it in that column would make an inference indistinguishable from testimony,
/// which is the one thing this schema exists to prevent. Where it should live is a schema question
/// and it is filed as one.
/// </para>
/// </summary>
internal static class GlauxLemmas
{
    /// <summary>The text the lemmas are written onto. GLAUx annotates no other text we serve.</summary>
    public const string Septuagint = SeptuagintTextSource.Slug;

    /// <summary>
    /// GLAUx's leading lemma for a word as written, whatever share of its evidence agrees: a lemma for every word
    /// whose form GLAUx knows, for a measurement that wants one. The conversion keeps only the agreed ones.
    /// </summary>
    internal static Func<string, string?> Leading(string directory)
    {
        var (lexicon, spelling) = Read(directory);
        return surface => lexicon.TryGetValue(GreekLetters.Bare(surface), out var choice)
            ? spelling.GetValueOrDefault(choice.Lemma) ?? choice.Lemma
            : null;
    }

    /// <summary>
    /// The form-to-lemma table, and the spelling each folded lemma is most often written with.
    ///
    /// The lexicon folds both sides so that an accented text can be looked up by an unaccented key,
    /// which is the whole point of it — but a folded lemma is not a lemma anybody would print. So
    /// the written spellings are counted alongside, and what lands in the column is the dictionary
    /// form as GLAUx writes it.
    /// </summary>
    internal static (Dictionary<string, LemmaChoice> Lexicon, Dictionary<string, string> Spelling) Read(
        string directory)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var words = new List<GlauxWord>(700_000);

        foreach (var file in Directory.EnumerateFiles(directory, "*.xml").Order(StringComparer.Ordinal))
        {
            foreach (var word in GlauxReader.Read(file))
            {
                words.Add(word);
                var folded = GreekLetters.Bare(word.Lemma);
                if (folded.Length == 0)
                {
                    continue;
                }

                if (!counts.TryGetValue(folded, out var spellings))
                {
                    spellings = new Dictionary<string, int>(StringComparer.Ordinal);
                    counts[folded] = spellings;
                }

                spellings[word.Lemma] = spellings.GetValueOrDefault(word.Lemma) + 1;
            }
        }

        var spelling = new Dictionary<string, string>(counts.Count, StringComparer.Ordinal);
        foreach (var (folded, options) in counts)
        {
            var leader = string.Empty;
            var best = 0;
            foreach (var (written, count) in options)
            {
                if (count > best || (count == best && string.CompareOrdinal(written, leader) < 0))
                {
                    best = count;
                    leader = written;
                }
            }

            spelling[folded] = leader;
        }

        return (GlauxLexicon.Build(words), spelling);
    }
}
