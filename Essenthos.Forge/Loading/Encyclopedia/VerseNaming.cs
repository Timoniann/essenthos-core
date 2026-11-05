namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// Which listed verses name their entity (a word in them is annotated to it, in any text) and which only
/// concern it. A word for God is a word rather than a person, and no word is annotated to it: a verse names
/// it where a word of the verse carries its number.
/// </summary>
internal static class VerseNaming
{
    internal const string Sql =
        """
        WITH named AS (
            SELECT DISTINCT a.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse
            FROM word_entity a
            JOIN word w ON w.id = a.word_id
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            UNION
            SELECT DISTINCT n.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse
            FROM entity_name n
            JOIN entity e ON e.id = n.entity_id AND e.kind = 'term'
            JOIN word w ON w.strong_number IN (n.hebrew_strong_number, n.greek_strong_number)
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary)
        UPDATE entity_verse v
        SET names = n.entity_id IS NOT NULL
        FROM entity_verse o
        LEFT JOIN named n ON n.entity_id = o.entity_id
             AND (n.canonical_book, n.canonical_chapter, n.canonical_verse)
                 = (o.canonical_book, o.canonical_chapter, o.canonical_verse)
        WHERE o.id = v.id AND v.names IS DISTINCT FROM (n.entity_id IS NOT NULL)
        """;
}
