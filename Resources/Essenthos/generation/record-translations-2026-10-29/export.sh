#!/usr/bin/env bash
# Reads, once and read-only, what the record-translation run needs from essenthos_core into $1 (default ./export):
# the verse texts of UBIO, LUTH1912 and RV1909 by canonical address, the events with their dates' arithmetic, the
# records' notes no translation renders yet, the places' modern sites and the gazetteer's candidate
# sites, the notes on titles borne, and the nominative name forms in Ukrainian, German and Spanish. Every statement is bounded by a server-side timeout.
set -euo pipefail
out="${1:-export}"
mkdir -p "$out"
q() { docker exec -i essenthos-core-db-1 sh -c 'psql -q -U "$POSTGRES_USER" -d essenthos_core'; }

for text in UBIO LUTH1912 RV1909; do
  echo "SET statement_timeout='180s';
\copy (SELECT vr.canonical_book, vr.canonical_chapter, vr.canonical_verse, string_agg(w.text || w.trailer, '' ORDER BY w.position) FROM verse v JOIN text t ON t.id = v.text_id AND t.slug = '$text' JOIN verse_reference vr ON vr.verse_id = v.id AND vr.is_primary JOIN word w ON w.verse_id = v.id GROUP BY v.id, 1,2,3 ORDER BY 1,2,3) TO STDOUT WITH (FORMAT csv)" | q > "$out/verses_$text.csv"
done

echo "SET statement_timeout='60s';
\copy (SELECT ev.id, ev.slug, ev.realm, ev.kind, ev.name, ev.description, ev.notes, ev.location, ev.region, ev.region_at_the_time, e.slug, e.source_id, ev.canonical_book, ev.canonical_chapter, ev.canonical_verse FROM event ev LEFT JOIN entity e ON e.id = ev.entity_id ORDER BY ev.id) TO STDOUT WITH (FORMAT csv)" | q > "$out/events.csv"
echo "SET statement_timeout='60s';
\copy (SELECT d.event_id, c.slug, d.calculation, d.notes FROM event_date d JOIN chronology c ON c.id = d.chronology_id WHERE d.calculation IS NOT NULL OR d.notes IS NOT NULL ORDER BY 1, 2) TO STDOUT WITH (FORMAT csv)" | q > "$out/event_dates.csv"
echo "SET statement_timeout='60s';
\copy (SELECT e.id, e.slug, e.kind, e.name, e.notes, e.modern_equivalent, e.source_id, (SELECT count(*) FROM entity_note_translation t WHERE t.entity_id = e.id) FROM entity e WHERE e.notes IS NOT NULL OR e.modern_equivalent IS NOT NULL ORDER BY e.id) TO STDOUT WITH (FORMAT csv)" | q > "$out/records.csv"
echo "SET statement_timeout='60s';
\copy (SELECT DISTINCT e.slug, c.site FROM place_candidate c JOIN entity e ON e.id = c.entity_id WHERE c.site IS NOT NULL ORDER BY 1, 2) TO STDOUT WITH (FORMAT csv)" | q > "$out/candidate_sites.csv"
echo "SET statement_timeout='60s';
\copy (SELECT t.slug, b.slug, tb.note FROM title_bearer tb JOIN entity t ON t.id = tb.title_entity_id JOIN entity b ON b.id = tb.bearer_entity_id WHERE tb.note IS NOT NULL ORDER BY 1, 2) TO STDOUT WITH (FORMAT csv)" | q > "$out/title_notes.csv"
echo "SET statement_timeout='60s';
\copy (SELECT e.slug, e.kind, e.name, e.source_id, f.language, f.form FROM entity_name_form f JOIN entity e ON e.id = f.entity_id WHERE f.grammatical_case = 'nominative' AND f.language IN ('ukr','deu','spa') ORDER BY 1, 5) TO STDOUT WITH (FORMAT csv)" | q > "$out/name_forms.csv"

(cd "$out" && sha256sum *.csv > SHA256SUMS)
