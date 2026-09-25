using System.Text;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;

namespace Essenthos.Core.BetaMasaheft;

/// <summary>
/// Divides a Ge'ez verse into words.
///
/// <para>
/// Ge'ez separates its words with a mark of its own, the wordspace <c>፡</c>, and ends its
/// sentences and clauses with <c>።</c>, <c>፣</c>, <c>፤</c> and their kin. The two sources write
/// them differently: HaCohen sets the wordspace apart, <c>በቀዳሚ ፡ ገብረ</c>, where the church's
/// printed Bible was typed with it against the word, <c>መጽሐፈ፡ ልደቱ</c>, and now and then with no
/// space after it at all. Either way the mark belongs to the gap between two words and not to
/// either word, so it is kept in the trailer — the verse reads back exactly as the file gives it,
/// and a word's surface is its letters and nothing else.
/// </para>
/// </summary>
internal static class GeezWords
{
    /// <summary>The wordspace, and the marks that end a clause, a sentence or a paragraph.</summary>
    private static readonly HashSet<char> Separators = ['፡', '።', '፣', '፤', '፥', '፦', '፧', '፨'];

    public static IReadOnlyList<WordDraft> Words(string verse)
    {
        var words = new List<(StringBuilder Surface, StringBuilder Trailer, TextBreak? Break)>();
        var pendingBreak = false;
        var i = 0;

        while (i < verse.Length)
        {
            var character = verse[i];
            if (character == GeezReader.LineBreak || char.IsWhiteSpace(character) || Separators.Contains(character))
            {
                if (character == GeezReader.LineBreak)
                {
                    pendingBreak = words.Count > 0;
                    if (words.Count > 0)
                    {
                        words[^1].Trailer.Append(' ');
                    }
                }
                else if (words.Count > 0)
                {
                    words[^1].Trailer.Append(character);
                }

                i++;
                continue;
            }

            var surface = new StringBuilder();
            while (i < verse.Length
                   && !char.IsWhiteSpace(verse[i])
                   && verse[i] != GeezReader.LineBreak
                   && !Separators.Contains(verse[i]))
            {
                surface.Append(verse[i]);
                i++;
            }

            words.Add((surface, new StringBuilder(), pendingBreak ? TextBreak.Line : null));
            pendingBreak = false;
        }

        return
        [
            .. words.Select((word, index) => new WordDraft(
                word.Surface.ToString(),
                index == words.Count - 1 ? word.Trailer.ToString().TrimEnd() : word.Trailer.ToString(),
                Break: word.Break)),
        ];
    }
}
