SET statement_timeout = '180s';
\pset tuples_only on
\pset format unaligned
WITH spa_text AS (
    SELECT id, slug FROM text WHERE language = 'spa'
),
missing AS (
    SELECT e.id, e.slug, e.kind, e.name, e.distinguisher, e.origin_entity_id
    FROM entity e
    WHERE NOT EXISTS (
        SELECT 1 FROM entity_name_form f
        WHERE f.entity_id = e.id AND f.language = 'spa' AND f.grammatical_case = 'nominative')
)
SELECT json_build_object(
    'slug', m.slug,
    'kind', m.kind,
    'name', m.name,
    'dist', m.distinguisher,
    'origin', (SELECT o.slug FROM entity o WHERE o.id = m.origin_entity_id),
    'other', (SELECT json_object_agg(f.language, f.form) FROM entity_name_form f
              WHERE f.entity_id = m.id AND f.grammatical_case = 'nominative' AND f.language IN ('ukr','deu','rus')),
    'printed', (SELECT r.form FROM entity_rendering r JOIN spa_text t ON t.id = r.text_id
                WHERE r.entity_id = m.id AND r.heading
                ORDER BY r.occurrences DESC, r.text_id LIMIT 1),
    'renderings', (SELECT json_agg(json_build_array(t.slug, r.form, r.occurrences, r.heading) ORDER BY r.occurrences DESC)
                   FROM entity_rendering r JOIN spa_text t ON t.id = r.text_id WHERE r.entity_id = m.id),
    'verses', (SELECT json_agg(x) FROM (
                   SELECT ev.canonical_book || ':' || ev.canonical_chapter || ':' || ev.canonical_verse AS ref,
                          (SELECT string_agg(w.text || w.trailer, '' ORDER BY w.position)
                           FROM spa_text t
                           JOIN book b ON b.text_id = t.id AND b.canonical_ordinal = ev.canonical_book
                           JOIN verse v ON v.text_id = t.id AND v.book_id = b.id
                                       AND v.chapter_number = ev.canonical_chapter AND v.number = ev.canonical_verse
                           JOIN word w ON w.verse_id = v.id
                           WHERE NOT EXISTS (SELECT 1 FROM entity_rendering r2 JOIN spa_text t2 ON t2.id = r2.text_id
                                             WHERE r2.entity_id = m.id AND r2.heading)) AS spa
                   FROM entity_verse ev WHERE ev.entity_id = m.id
                   ORDER BY ev.canonical_book, ev.canonical_chapter, ev.canonical_verse LIMIT 3) x)
)
FROM missing m
ORDER BY m.slug;
