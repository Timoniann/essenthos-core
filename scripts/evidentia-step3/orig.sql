SET statement_timeout = '120s';
COPY (
  SELECT w.id, t.slug, b.canonical_ordinal, v.chapter_number, v.number, w.position, w.text,
         coalesce(w.lemma, ''), coalesce(w.strong_number, ''), coalesce(replace(w.gloss, E'\t', ' '), ''),
         coalesce(w.morphology::text, '{}')
  FROM word w
  JOIN verse v ON v.id = w.verse_id
  JOIN book b ON b.id = v.book_id
  JOIN text t ON t.id = w.text_id
  WHERE t.slug IN ('BHSA', 'NESTLE1904')
    AND (b.canonical_ordinal, v.chapter_number) IN (
      (1,1),(1,2),(1,3),(1,4),(1,5),(1,6),(1,7),(1,8),(1,9),(1,10),(1,11),
      (8,1),(8,2),(8,3),(8,4),(32,1),(32,2),(32,3),(32,4),
      (41,1),(41,2),(41,3),(41,4),(41,5),
      (9,16),(9,17),(9,18),
      (19,1),(19,2),(19,3),(19,4),(19,5),(19,6),(19,7),(19,8),(19,9),(19,10),(19,11),(19,12),(19,13),(19,14),
      (19,15),(19,16),(19,17),(19,18),(19,19),(19,20),(19,21),(19,22),(19,23),(19,24),
      (43,1),(43,2),(43,3),(43,4),
      (2,20),(2,21),(2,22),(2,23),(2,24),(23,39),(23,40),(23,41),(23,42),(23,43),
      (45,5),(45,6),(45,7),(45,8),(45,9),
      (4,32),(4,33),(4,34),(13,6),(13,7),(13,8),(15,9),(15,10),(23,33),(23,34),(23,35),
      (27,10),(27,11),(27,12),(40,25),(40,26),(58,10),(58,11),(66,17),(66,18))
  ORDER BY w.id
) TO STDOUT WITH (FORMAT text);
