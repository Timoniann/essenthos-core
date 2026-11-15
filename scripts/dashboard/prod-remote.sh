# Runs on the production server as deploy (piped by prod.sh). Read-only.
set -euo pipefail
metric="$1"
since=$(( $(date +%s) - 86400 ))
# The proxy's access log for the last 24 hours: the newest rolled file and the current one.
last_day() {
  docker exec essenthos-proxy-1 sh -c 'f=$(ls -t /var/log/caddy/access-*.log.gz 2>/dev/null | head -1); [ -n "$f" ] && gzip -dc "$f"; cat /var/log/caddy/access.log' \
    | awk -v s="$since" 'match($0, /"ts":[0-9.]+/) { if (substr($0, RSTART + 5, RLENGTH - 5) + 0 >= s) print }'
}
umami() {
  { echo "set default_transaction_read_only = on;"; echo "$1"; } \
    | docker exec -i essenthos-db-1 sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d essenthos_umami -At' | tail -1
}
case "$metric" in
  api)       docker ps --format '{{.Image}}' | grep -m1 '^essenthos-api:' | cut -d: -f2 | cut -c1-8 ;;
  web)       docker ps --format '{{.Image}}' | grep -m1 '^essenthos-web:' | cut -d: -f2 | cut -c1-8 ;;
  googlebot) last_day | grep -cE '"remote_ip":"(66\.249\.|2001:4860:7:)' || true ;;
  requests)  last_day | wc -l ;;
  errors)    last_day | grep -cE '"status":5[0-9][0-9]' || true ;;
  notfound)  last_day | grep -c '"status":404' || true ;;
  visitors)  umami "select count(distinct visit_id) from website_event where created_at >= now() - interval '24 hours';" ;;
  google)    umami "select count(distinct visit_id) from website_event where created_at >= now() - interval '24 hours' and referrer_domain like '%google.%';" ;;
  backup)    newest=$(ls -t /srv/essenthos/backups/*.dump.gpg | head -1); echo $(( ( $(date +%s) - $(stat -c %Y "$newest") ) / 3600 )) ;;
  disk)      df --output=pcent /srv/essenthos | tail -1 | tr -d ' %' ;;
  *)         echo "unknown metric $metric" >&2; exit 2 ;;
esac
