using System.Text.RegularExpressions;
using Essenthos.Core.Loading;

namespace Essenthos.Core.Conversion;

/// <summary>
/// The Synodal's dashes as the edition prints them (PRB-1994): bible4u's RUSV.xml types every dash as two hyphens, and the
/// trailers carried them so — «и семя; --вам [сие] будет в пищу» (Genesis 1:29), «человеческого--зло» (8:21) — where the
/// printed Synodal sets an em dash with a space on either side, as the deuterocanon (from Wikisource) already reads.
///
/// <para>
/// Measured over the words product (2026-11-23): 2,698 trailers hold «--» and 735 an em dash, all of the latter « — » or
/// «, — ». Of the 2,279 runs in the file, 1,670 stand between two letters with no space («тебя--прокляты»), 743 after a
/// space and before a word, 232 end a verse, and 25 stand directly after a word with a space after them. The 25 are not
/// dashes: they are hyphenated words the file broke («что-- нибудь», «по-- еврейски», «Авен-- Езера», «во-- первых»), and
/// a dash there would print «что — нибудь»; they are left as the file prints them.
/// </para>
///
/// <para>
/// Every other run becomes an em dash with one space before it and one after, the whitespace round it replaced; a dash
/// ending a verse's last word has no space after it, as no verse of the file ends in a space. Only the trailers change:
/// the words, their number and their letters are the file's, so every link that names one still names it.
/// </para>
/// </summary>
internal sealed partial class SynodalDashes : ITextStage
{
    private const string Dash = " — ";

    /// <summary>The run of hyphens a hyphenated word was broken at: the trailer opens with it and a space follows.</summary>
    private const string BrokenWord = "-- ";

    public bool AppliesTo(string slug) => slug == Bible4uTextSource.Synodal;

    public IReadOnlyList<string> Sources(string slug) => [];

    public void Apply(ConvertedText text, string resources)
    {
        var dashes = 0;
        foreach (var verse in text.Verses)
        {
            for (var at = 0; at < verse.Words.Count; at++)
            {
                var word = verse.Words[at];
                var trailer = Dashed(word.Trailer, at == verse.Words.Count - 1);
                if (!ReferenceEquals(trailer, word.Trailer))
                {
                    word.Trailer = trailer;
                    dashes++;
                }
            }
        }

        if (dashes > 0)
        {
            text.Provenance.Add(new("dashes", $"{dashes} trailers' «--» printed as the edition's em dash"));
        }
    }

    /// <summary>The trailer with its double hyphens as the edition's dash; the same instance where there is nothing to change.</summary>
    internal static string Dashed(string trailer, bool last)
    {
        if (!trailer.Contains("--", StringComparison.Ordinal) || trailer == BrokenWord)
        {
            return trailer;
        }

        var dashed = DoubleHyphen().Replace(trailer, Dash);
        return last && !char.IsWhiteSpace(trailer[^1]) ? dashed.TrimEnd() : dashed;
    }

    [GeneratedRegex(@"\s*-{2,}\s*")]
    private static partial Regex DoubleHyphen();
}
