#!/usr/bin/env bash
# Reads, once and read-only, what the known-for run needs from essenthos_core into $1 (default ./export):
# the verse texts of KJV, UBIO, LUTH1912 and RV1909 by canonical address, and the persons, places and objects
# with their verses, summary clauses, events, titles, key passages, name forms and the lines already held.
# Every statement is bounded by a server-side timeout; nothing is written.
set -euo pipefail
out="${1:-export}"
mkdir -p "$out"
q() { docker exec -i essenthos-core-db-1 sh -c 'psql -q -U "$POSTGRES_USER" -d essenthos_core'; }

for text in KJV UBIO LUTH1912 RV1909; do
  echo "SET statement_timeout='180s';
\copy (SELECT vr.canonical_book, vr.canonical_chapter, vr.canonical_verse, v.id, string_agg(w.text || w.trailer, '' ORDER BY w.position) FROM verse v JOIN text t ON t.id = v.text_id AND t.slug = '$text' JOIN verse_reference vr ON vr.verse_id = v.id AND vr.is_primary JOIN word w ON w.verse_id = v.id GROUP BY 1,2,3,4 ORDER BY 1,2,3,4) TO STDOUT WITH (FORMAT csv)" | q > "$out/verses_$text.csv"
done

echo "SET statement_timeout='60s';
\copy (SELECT e.id, e.slug, e.kind, e.name, e.distinguisher, e.sex, e.subtype, e.place_kind, e.another_name_for_entity_id, e.notes FROM entity e WHERE e.kind IN ('person','place','object') ORDER BY e.id) TO STDOUT WITH (FORMAT csv)" | q > "$out/entities.csv"
echo "SET statement_timeout='60s';
\copy (SELECT DISTINCT v.entity_id, v.canonical_book, v.canonical_chapter, v.canonical_verse, v.names FROM entity_verse v JOIN entity e ON e.id=v.entity_id AND e.kind IN ('person','place','object') ORDER BY 1,2,3,4) TO STDOUT WITH (FORMAT csv)" | q > "$out/entity_verses.csv"
echo "SET statement_timeout='60s';
\copy (SELECT d.entity_id, d.ordinal, d.relation, t.slug, t.name, t.kind, d.canonical_book, d.canonical_chapter, d.canonical_verse FROM entity_descriptor d JOIN entity t ON t.id=d.target_entity_id JOIN entity e ON e.id=d.entity_id AND e.kind IN ('person','place','object') ORDER BY 1,2) TO STDOUT WITH (FORMAT csv)" | q > "$out/descriptors.csv"
echo "SET statement_timeout='60s';
\copy (SELECT ev.entity_id, ev.name, ev.kind, ev.description, ev.canonical_book, ev.canonical_chapter, ev.canonical_verse FROM event ev WHERE ev.entity_id IS NOT NULL ORDER BY 1, ev.year_from_creation NULLS LAST, ev.id) TO STDOUT WITH (FORMAT csv)" | q > "$out/events.csv"
echo "SET statement_timeout='60s';
\copy (SELECT tb.bearer_entity_id, t.slug, t.name, tb.canonical_book, tb.canonical_chapter, tb.canonical_verse FROM title_bearer tb JOIN entity t ON t.id = tb.title_entity_id ORDER BY 1) TO STDOUT WITH (FORMAT csv)" | q > "$out/titles.csv"
echo "SET statement_timeout='60s';
\copy (SELECT p.entity_id, p.role, p.canonical_book, p.canonical_chapter, p.canonical_verse, p.end_chapter, p.end_verse, p.note FROM entity_passage p ORDER BY 1, p.ordinal) TO STDOUT WITH (FORMAT csv)" | q > "$out/passages.csv"
echo "SET statement_timeout='60s';
\copy (SELECT f.entity_id, f.language, f.form FROM entity_name_form f WHERE f.grammatical_case='nominative' AND f.language IN ('ukr','deu','spa') ORDER BY 1,2) TO STDOUT WITH (FORMAT csv)" | q > "$out/name_forms.csv"
echo "SET statement_timeout='60s';
\copy (SELECT k.entity_id, count(*) FROM entity_known_for k GROUP BY 1) TO STDOUT WITH (FORMAT csv)" | q > "$out/known_for_held.csv"

(cd "$out" && sha256sum *.csv > SHA256SUMS)
