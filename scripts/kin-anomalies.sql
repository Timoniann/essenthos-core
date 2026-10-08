-- Family relationships that contradict themselves, read-only. Direction: from <type> to (isaac son-of abram).
\pset footer off
\pset format unaligned
\pset fieldsep ' | '
CREATE TEMP VIEW k AS
SELECT r.id, r.type, f.slug fs, f.sex fsex, f.kind fk, t.slug ts, t.sex tsex, t.kind tk, r.from_entity_id a, r.to_entity_id b,
       r.canonical_book||' '||r.canonical_chapter||':'||r.canonical_verse vref, left(r.source, 40) src
FROM entity_relationship r JOIN entity f ON f.id = r.from_entity_id JOIN entity t ON t.id = r.to_entity_id;

\echo '== 1. the from-side sex contradicts the relation (a woman as father/son/brother/husband, a man as mother/daughter/sister/wife)'
SELECT type, fs, fsex, ts, vref, src FROM k
WHERE (type IN ('father-of','son-of','brother-of','half-brother-of','husband-of','grandfather-of','father-in-law-of','son-in-law-of','uncle-of','nephew-of') AND fsex = 'female')
   OR (type IN ('mother-of','daughter-of','sister-of','half-sister-of','wife-of','grandmother-of','mother-in-law-of','daughter-in-law-of','aunt-of','niece-of','concubine-of') AND fsex = 'male')
ORDER BY type, fs;

\echo '== 2. the to-side sex contradicts the relation (husband of a man, wife of a woman)'
SELECT type, fs, ts, tsex, vref, src FROM k
WHERE (type IN ('husband-of') AND tsex = 'male') OR (type IN ('wife-of','concubine-of') AND tsex = 'female') ORDER BY type, fs;

\echo '== 3. self-relations'
SELECT type, fs, vref, src FROM k WHERE a = b;

\echo '== 4. two kinds of kin at once between the same two people (parent and sibling, parent and spouse, child and sibling ...)'
WITH n AS (
  SELECT a, b, fs, ts, CASE
    WHEN type IN ('father-of','mother-of') THEN 'parent' WHEN type IN ('son-of','daughter-of') THEN 'child'
    WHEN type IN ('brother-of','sister-of','half-brother-of','half-sister-of') THEN 'sibling'
    WHEN type IN ('husband-of','wife-of','concubine-of') THEN 'spouse'
    WHEN type IN ('grandfather-of','grandmother-of') THEN 'grandparent' WHEN type IN ('grandson-of','granddaughter-of') THEN 'grandchild'
  END rel, type, vref FROM k),
d AS (  -- bring every pair to one direction: a parent-of b == b child-of a
  SELECT LEAST(a,b) x, GREATEST(a,b) y, CASE
    WHEN rel IN ('parent','child') THEN CASE WHEN (rel = 'parent') = (a < b) THEN 'x parent of y' ELSE 'y parent of x' END
    WHEN rel IN ('grandparent','grandchild') THEN CASE WHEN (rel = 'grandparent') = (a < b) THEN 'x grandparent of y' ELSE 'y grandparent of x' END
    ELSE rel END kin, fs, ts, type, vref FROM n WHERE rel IS NOT NULL)
SELECT (SELECT slug FROM entity WHERE id = x) x, (SELECT slug FROM entity WHERE id = y) y,
       string_agg(DISTINCT kin, ' + ') kinds, string_agg(DISTINCT fs||' '||type||' '||ts||' @'||vref, '; ') rows
FROM d GROUP BY x, y HAVING count(DISTINCT kin) > 1 ORDER BY 1;

\echo '== 5. kin relations to something that is not a person'
SELECT type, fs, fk, ts, tk, vref, src FROM k
WHERE type IN ('father-of','mother-of','son-of','daughter-of','brother-of','sister-of','husband-of','wife-of','grandfather-of','half-brother-of')
  AND (fk <> 'person' OR tk <> 'person') ORDER BY type, fs;

\echo '== 6. someone with two different fathers or two different mothers (stated as son-of/daughter-of or father-of/mother-of)'
WITH p AS (
  SELECT b child, a parent, (SELECT sex FROM entity WHERE id = a) psex FROM k WHERE type IN ('father-of','mother-of')
  UNION SELECT a, b, (SELECT sex FROM entity WHERE id = b) FROM k WHERE type IN ('son-of','daughter-of'))
SELECT (SELECT slug FROM entity WHERE id = child) child, psex, string_agg((SELECT slug FROM entity WHERE id = parent), ', ') parents
FROM p WHERE psex IS NOT NULL GROUP BY child, psex HAVING count(DISTINCT parent) > 1 ORDER BY 1;

\echo '== 7. a parent cycle (a is the parent of b and b the parent of a, or through one more generation)'
WITH p AS (SELECT a parent, b child FROM k WHERE type IN ('father-of','mother-of') UNION SELECT b, a FROM k WHERE type IN ('son-of','daughter-of'))
SELECT (SELECT slug FROM entity WHERE id = p1.parent) a, (SELECT slug FROM entity WHERE id = p1.child) b, (SELECT slug FROM entity WHERE id = p2.child) c
FROM p p1 JOIN p p2 ON p2.parent = p1.child WHERE p2.child = p1.parent
UNION ALL
SELECT (SELECT slug FROM entity WHERE id = p1.parent), (SELECT slug FROM entity WHERE id = p1.child), (SELECT slug FROM entity WHERE id = p3.child)
FROM p p1 JOIN p p2 ON p2.parent = p1.child JOIN p p3 ON p3.parent = p2.child WHERE p3.child = p1.parent;

\echo '== 8. siblings who are also parent and child through the tree (a sibling of b, and a parent of b''s parent or of b)'
WITH p AS (SELECT a parent, b child FROM k WHERE type IN ('father-of','mother-of') UNION SELECT b, a FROM k WHERE type IN ('son-of','daughter-of')),
s AS (SELECT a, b FROM k WHERE type IN ('brother-of','sister-of','half-brother-of','half-sister-of'))
SELECT (SELECT slug FROM entity WHERE id = s.a) a, (SELECT slug FROM entity WHERE id = s.b) b, 'a is grandparent of b' why
FROM s JOIN p p1 ON p1.parent = s.a JOIN p p2 ON p2.parent = p1.child AND p2.child = s.b;
