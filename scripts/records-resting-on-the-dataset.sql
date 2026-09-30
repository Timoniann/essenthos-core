-- The records BibleData supplied that still rest on it alone: neither a verse of ours (a row this
-- project wrote, from a word it annotated, a reading of the verse, or the verse a relationship of ours
-- was read from) nor a relationship of ours stands on them. A record withdrawn or folded into another
-- is no longer in the table, so it is not listed. After the records of 2026-09-30 are loaded this
-- returns no row; any row it returns is a record that needs a decision.
--
-- Read-only and bounded. Run it against the corpus database, for instance:
--   docker exec -i essenthos-core-db-1 psql -U essenthos -d essenthos_core -f - < scripts/records-resting-on-the-dataset.sql

SET statement_timeout = '60s';

SELECT e.slug, e.name, e.kind, e.distinguisher AS the_datasets_line
FROM entity e
WHERE e.source LIKE 'BibleData by%'
  AND NOT EXISTS (
      SELECT 1 FROM entity_verse v
      WHERE v.entity_id = e.id AND v.source LIKE 'Essenthos%')
  AND NOT EXISTS (
      SELECT 1 FROM entity_relationship r
      WHERE (r.from_entity_id = e.id OR r.to_entity_id = e.id) AND r.source NOT LIKE 'BibleData by%')
ORDER BY e.slug
LIMIT 1000;
