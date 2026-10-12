SET statement_timeout='180s';
\pset format unaligned
\pset fieldsep '\t'
\pset footer off
WITH p AS (
  SELECT e.id, e.slug FROM entity e WHERE e.kind='person' AND (
    NOT EXISTS (SELECT 1 FROM entity_name_form f WHERE f.entity_id=e.id AND f.language='ukr' AND f.grammatical_case='nominative')
    OR NOT EXISTS (SELECT 1 FROM entity_name_form f WHERE f.entity_id=e.id AND f.language='rus' AND f.grammatical_case='nominative'))),
orig AS (
  SELECT p.slug, w.id word_id, t.slug txt FROM p JOIN word_entity we ON we.entity_id=p.id JOIN word w ON w.id=we.word_id JOIN text t ON t.id=w.text_id
  WHERE t.slug IN ('BHSA','NESTLE1904','TR1894')),
tr AS (
  SELECT o.slug, tt.slug txt, rw.text form
  FROM orig o
  JOIN link_word a ON a.word_id=o.word_id
  JOIN link l ON l.id=a.link_id
  JOIN link_word b ON b.link_id=l.id AND b.side<>a.side
  JOIN word rw ON rw.id=b.word_id
  JOIN text tt ON tt.id=rw.text_id AND tt.slug IN ('RUSV','UBIO','UKR1871')
  WHERE rw.text ~ '^[А-ЯЁЄІЇҐ]')
SELECT slug, txt, string_agg(form||'x'||n, ' ' ORDER BY n DESC) FROM (SELECT slug, txt, form, count(*) n FROM tr GROUP BY 1,2,3) s GROUP BY 1,2 ORDER BY 1,2;
