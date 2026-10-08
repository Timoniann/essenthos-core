WITH kjv AS (SELECT id FROM text WHERE slug='KJV'),
v AS (SELECT vr.canonical_book b, vr.canonical_chapter c, string_agg(w.text||coalesce(w.trailer,''), '' ORDER BY w.position) line
      FROM verse ve JOIN kjv ON kjv.id=ve.text_id JOIN verse_reference vr ON vr.verse_id=ve.id AND vr.is_primary JOIN word w ON w.verse_id=ve.id
      GROUP BY ve.id, 1, 2),
k AS (SELECT b, c, count(*) n FROM v WHERE line ~ '(son|sons|daughter|daughters|wife|wives|husband|brother|sister|father|mother|Son|Sons) of (the )?[A-Z][a-z]' AND line !~ 'of (Man|Zion|Jerusalem|Israel|Judah|Babylon|God|Belial|the LORD)\M' GROUP BY 1,2),
p AS (SELECT canonical_book b, canonical_chapter c, count(DISTINCT entity_id) n FROM entity_verse ev JOIN entity e ON e.id=ev.entity_id AND e.kind='person' GROUP BY 1,2),
r AS (SELECT DISTINCT canonical_book b, canonical_chapter c FROM entity_relationship WHERE canonical_book IS NOT NULL)
SELECT k.b||'|'||k.c||'|'||k.n||'|'||p.n FROM k JOIN p USING (b,c) LEFT JOIN r USING (b,c) WHERE r.b IS NULL AND p.n >= 2 ORDER BY k.n DESC;
