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
/// and a word's surface is its letters and nothing else. A run of dots or an asterisk standing
/// where a word would is a mark too, and goes to the trailer of the word before it.
/// </para>
///
/// <para>
/// Dillmann's edition, as HaCohen digitised it, marks two kinds of words with brackets:
/// <c>( )</c> the Ethiopic has and the Septuagint does not, and <c>[ ]</c> his base manuscript
/// lacks and he supplied from later ones. Those are statements about the words, not letters of
/// them, so where <paramref name="marksBrackets"/> says a source means them so they come off the
/// surface and stand on the word. A bracket can open inside a word — <c>ለ(ሱራፌል ፡ ወለ)ኪሩቤል</c> —
/// and a word is marked where most of its letters are inside: the first word here is an addition,
/// the second is the Greek's word with an added "and to" in front.
/// </para>
/// </summary>
internal static class GeezWords
{
    /// <summary>The wordspace, and the marks that end a clause, a sentence or a paragraph.</summary>
    private static readonly HashSet<char> Separators = ['፡', '።', '፣', '፤', '፥', '፦', '፧', '፨'];

    private const char OpensAddition = '(';

    private const char ClosesAddition = ')';

    private const char OpensRestoration = '[';

    private const char ClosesRestoration = ']';

    /// <param name="marksBrackets">
    /// The source uses round and square brackets as Dillmann does. Elsewhere a bracket is left as
    /// the typist wrote it, since nobody has said what it means there.
    /// </param>
    public static IReadOnlyList<WordDraft> Words(string verse, bool marksBrackets = false)
    {
        var words = new List<Token>();
        var pendingBreak = false;
        var addition = 0;
        var restoration = 0;
        var inAddition = false;
        var inRestoration = false;
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

            var token = new Token(pendingBreak ? TextBreak.Line : null);
            while (i < verse.Length
                   && !char.IsWhiteSpace(verse[i])
                   && verse[i] != GeezReader.LineBreak
                   && !Separators.Contains(verse[i]))
            {
                var letter = verse[i++];
                if (marksBrackets && letter is OpensAddition or ClosesAddition or OpensRestoration or ClosesRestoration)
                {
                    switch (letter)
                    {
                        case OpensAddition:
                            inAddition = true;
                            addition++;
                            break;
                        case ClosesAddition:
                            inAddition = false;
                            break;
                        case OpensRestoration:
                            inRestoration = true;
                            restoration++;
                            break;
                        default:
                            inRestoration = false;
                            break;
                    }

                    continue;
                }

                token.Surface.Append(letter);
                if (IsWritten(letter))
                {
                    token.Letters++;
                    token.Added += inAddition ? 1 : 0;
                    token.Restored += inRestoration ? 1 : 0;
                    token.Addition = inAddition ? addition : token.Addition;
                    token.Restoration = inRestoration ? restoration : token.Restoration;
                }
            }

            if (token.Letters == 0)
            {
                // A bracket alone, a run of dots, an asterisk: nothing a reader would call a word.
                if (words.Count > 0)
                {
                    words[^1].Trailer.Append(token.Surface);
                    continue;
                }

                if (token.Surface.Length == 0)
                {
                    continue;
                }
            }

            words.Add(token);
            pendingBreak = false;
        }

        return
        [
            .. words.Select((word, index) => new WordDraft(
                word.Surface.ToString(),
                index == words.Count - 1 ? word.Trailer.ToString().TrimEnd() : word.Trailer.ToString(),
                SuppliedSpan: Mostly(word.Added, word.Letters) ? word.Addition : null,
                Break: word.Break)
            {
                RestoredSpan = Mostly(word.Restored, word.Letters) ? word.Restoration : null,
            }),
        ];
    }

    private static bool Mostly(int inside, int letters) => inside * 2 > letters;

    /// <summary>A letter, or a digit of either script: the Ethiopic numerals are numbers, not letters.</summary>
    private static bool IsWritten(char character) =>
        char.IsLetterOrDigit(character)
        || char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.OtherNumber;

    private sealed class Token(TextBreak? @break)
    {
        public StringBuilder Surface { get; } = new();

        public StringBuilder Trailer { get; } = new();

        public TextBreak? Break { get; } = @break;

        public int Letters { get; set; }

        public int Added { get; set; }

        public int Restored { get; set; }

        public int? Addition { get; set; }

        public int? Restoration { get; set; }
    }
}
