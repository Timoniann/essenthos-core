namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// What an entity is. Deliberately short: an event is not one of these because an event carries
/// dates and an arithmetic that a person does not, and forcing the two into one table would mean
/// a dozen columns null for every row of one kind.
/// </summary>
public enum EntityKind
{
    Person,
    Place,

    /// <summary>
    /// A collective the text names and speaks of as one — a nation, a tribe, a clan.
    ///
    /// It is neither of the other two and cannot be squeezed into either. It is not a person: it
    /// has no birth and no death, it acts across centuries, and annotating <em>the tribe of Judah
    /// could not drive out the Jebusites</em> to Jacob's fourth son offers a reader a man four
    /// hundred years dead. It is not a place: a people moves, and the land it moves out of keeps
    /// the name. The corpus was already carrying the gap in two places — every gentilic Strong
    /// derives had a lexeme where its near end should be, and the largest class of dissent in an
    /// 8,092-occurrence audit was a tribal name forced onto its eponym — and both of those are one
    /// missing kind.
    /// </summary>
    People,

    /// <summary>
    /// A word the text uses of God and of gods — elohim, el, eloah — which is none of the other three.
    ///
    /// It is not a person. The divine name is its letters, יהוה, and belongs to YHVH; these are words
    /// the text says of the God of Israel, of the gods of the nations and of judges alike, so an entry
    /// for one of them is an entry for a word and where it stands, not for somebody. BHSA marks none of
    /// them as a name, which is why nothing resolves an occurrence to one of these the way a proper noun
    /// resolves.
    /// </summary>
    Term,

    /// <summary>
    /// A name the text gives to whoever holds an office, which may be more than one holder and is
    /// not certainly anybody's own name — Abimelech of the kings of Gerar, as Pharaoh is of Egypt's.
    ///
    /// It is not a person, because a person is one man, and the reason for this kind is that the
    /// text does not establish one: Abimelech king of Gerar deals with Abraham, again with Isaac
    /// after Abraham's death, and in the title of Psalm 34 with David, whose own history calls that
    /// king Achish. A person record has to answer whether those are one man, and either answer is a
    /// guess. It is not a term either: a term is a word said of God and of gods, and a reader told
    /// Abimelech is a word for God has been told something false. The occurrences are named and the
    /// record says whose office the name belongs to; which men bore it is left open, and the record
    /// says so.
    /// </summary>
    Title,
}
