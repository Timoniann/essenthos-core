SET statement_timeout='120s';
\pset format unaligned
\pset fieldsep '\t'
\pset footer off
WITH p AS (
  SELECT e.id, e.slug, e.name,
    EXISTS (SELECT 1 FROM entity_name_form f WHERE f.entity_id=e.id AND f.language='ukr' AND f.grammatical_case='nominative') ukr,
    EXISTS (SELECT 1 FROM entity_name_form f WHERE f.entity_id=e.id AND f.language='rus' AND f.grammatical_case='nominative') rus
  FROM entity e WHERE e.kind='person')
SELECT p.slug, p.name, p.ukr, p.rus,
  (SELECT string_agg(f.language||'.'||f.grammatical_case||'='||f.form, ' ' ORDER BY f.language, f.grammatical_case) FROM entity_name_form f WHERE f.entity_id=p.id) own_forms,
  (SELECT string_agg(t.slug||':'||r.form||'x'||r.occurrences, ' ' ORDER BY t.slug, r.occurrences DESC) FROM entity_rendering r JOIN text t ON t.id=r.text_id WHERE r.entity_id=p.id AND t.slug IN ('RUSV','UBIO','UKR1871')) printed,
  (SELECT string_agg(DISTINCT f.language||'.'||f.grammatical_case||'='||f.form, ' ') FROM entity o JOIN entity_name_form f ON f.entity_id=o.id
     WHERE o.name=p.name AND o.id<>p.id AND o.kind='person' AND f.language IN ('ukr','rus') AND f.grammatical_case IN ('nominative','genitive')) namesakes
FROM p WHERE NOT p.ukr OR NOT p.rus ORDER BY p.slug;
