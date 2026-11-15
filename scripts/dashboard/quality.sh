#!/usr/bin/env bash
# Values for the avioniq dashboard tiles about the local corpus, read from the
# latest `forge verify` run in essenthos_core (verification_run). Read-only.
# Usage: quality.sh coverage <TEXT> "<old testament|new testament|deuterocanon>"
#        quality.sh <crowded|suspect|broken|age-h>
set -euo pipefail
metric="${1:?name a metric}"
latest="v.id = (select max(id) from verification_run)"
case "$metric" in
  coverage) sql="select round((c->>'share')::numeric * 100, 1) from verification_run v, jsonb_array_elements(v.measures->'coverage') c where $latest and c->>'text' = '${2:?text}' and c->>'section' = '${3:?section}';" ;;
  crowded)  sql="select sum((c->>'crowded')::int) from verification_run v, jsonb_array_elements(v.measures->'crowding') c where $latest;" ;;
  suspect)  sql="select sum((c->>'suspect')::int) from verification_run v, jsonb_array_elements(v.measures->'pairing') c where $latest;" ;;
  broken)   sql="select v.broken from verification_run v where $latest;" ;;
  age-h)    sql="select floor(extract(epoch from now() - v.ran_at) / 3600)::int from verification_run v where $latest;" ;;
  *)        echo "unknown metric $metric" >&2; exit 2 ;;
esac
echo "$sql" | docker exec -i essenthos-core-db-1 sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d essenthos_core -At'
