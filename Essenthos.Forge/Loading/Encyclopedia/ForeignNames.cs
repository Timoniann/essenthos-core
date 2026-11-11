using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// A person's name carried along a link onto a word that is somebody else's name.
///
/// <para>
/// An aligner that pairs the wrong two names of a verse makes every name carried along the pair
/// wrong with it. Ohienko's Romans 16:20 reads <em>сатану</em> at its sixth word and
/// <em>Христа</em> at its fourteenth, the aligner put <em>Христа</em> against <em>Σατανᾶν</em>, and
/// the lexicon's Satan arrived on Christ — where, carried by a strong number, it outranked the
/// Jesus the title carried there. Tyndale's and Douay's <em>Ananias</em> of Acts 5:3 were given
/// Satan the same way. The link is a guess about which words correspond; the word's own spelling is
/// not a guess.
/// </para>
///
/// <para>
/// <strong>What makes a word another's name is the text itself.</strong> The test asks five
/// things, and refuses only where all five hold:
/// </para>
/// <list type="number">
/// <item>The seed word is the person's own name — its number is one the person's names bear, or it
/// is spelled like one of them. A reading that names a man on <em>the king</em> or on
/// <em>the LORD</em> carries a description, not a name, and a description may be rendered by any
/// word.</item>
/// <item>The word it lands on is written as a name: with a capital where the sentence does not
/// open. A word written in lower case, or in a script that has no capitals, is never taken for
/// another's name here, because nothing on the page says it is one.</item>
/// <item>The word is no spelling of the person: not alike any name or name form the encyclopedia
/// holds for them, nor the seed word itself, nor a part of one of their names — <em>Moab</em> in
/// <em>Pahath-moab</em> is half the man's name as well as the land's.</item>
/// <item>The text writes the word, wherever else it stands, as the name of another record the verse
/// names, in any text: words of the same spelling in the same text name that record at least
/// <see cref="Least"/> times and at least <see cref="Outnumbering"/> times as often as they name this
/// person or anyone who shares a name, a number or a title with them. Namesakes are therefore never
/// each other's foreigners — <em>Захарии</em> is thirty men's — and neither are a title and its
/// bearer.</item>
/// <item>The text does not write the word for this person in <see cref="Elsewhere"/> verses or more
/// where the other record goes unnamed. A translation that spells two men alike keeps its spelling: Almeida writes Jehoiachin
/// <em>Joaquim</em> as it writes his father, in verses that name the son alone, and so the son's
/// <em>Joaquim</em> of 2 Chronicles 36:8 stays his though the father is named three words before it.
/// What a link misplaces, it misplaces in the same company each time: every <em>João</em> given
/// James stands in a verse that names John.</item>
/// </list>
///
/// <para>
/// So it costs nothing where it cannot see: a language whose forms are not held, a script with no
/// capitals, a name written once. Measured on the corpus of 2026-10-06 it takes back some seven
/// hundred carried names in the cased texts, and the examples are the class: <em>João</em> given
/// James, <em>Jeroboam</em> given Nebat in <em>Jeroboam son of Nebat</em>, <em>Joshua</em> given
/// Nun, <em>Христа</em> given Satan.
/// </para>
///
/// <para>
/// A row a person or a source settled is left alone: it carries no confidence, and a spelling does
/// not overturn a ruling. Nor is a row taken back where something besides the links names the word
/// so — an answer read of the word itself stands on its own.
/// </para>
///
/// <para>
/// The names projection asks it of every name it carries (<see cref="Names.ForeignNameRule"/>), with
/// the bars held here.
/// </para>
/// </summary>
internal static class ForeignNames
{
    /// <summary>How many times the text must write the spelling as the other record's name.</summary>
    public const int Least = 3;

    /// <summary>How many times more often than as this person's, or a namesake's.</summary>
    public const int Outnumbering = 3;

    /// <summary>
    /// In how many verses that do not name the other record the text must write the word for this
    /// person before the word is taken for a second spelling of them rather than a misplaced one.
    /// One is not enough: a link that misplaces a name once misplaces it in a verse or two more.
    /// </summary>
    public const int Elsewhere = 2;
}
