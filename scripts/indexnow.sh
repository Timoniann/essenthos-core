#!/bin/bash
# Tells IndexNow (Bing, Yandex and the others that share it) which public pages changed.
# Runs ON THE SERVER: ssh deploy@<server> 'bash -s' -- <args> < scripts/indexnow.sh
#
# Owner, 2026-11-17: at most once a day, after a release, and only the pages the release changed.
# The lead decides what changed and names the sitemap lists (or single addresses) to send:
#   --maps 'events'                        events.xml only
#   --maps 'records-(people|places)'       two record lists
#   --maps 'verses-.*'                     every verse list
#   --maps '.*'                            everything (a footer or a change every page shows)
#   --url https://essenthos.org/           one address; repeatable (a landing-page change)
#   --match '/genesis/'                    keep only addresses matching this, after --maps
#   --dry-run                              count and list samples, send nothing
#   --force                                send although a batch went out in the last 24 h
# List names are those in https://essenthos.org/sitemap.xml without .xml (pages, chapters,
# verses-<book>, records-<kind>, events, periods, occupations, lexicon-<language>).
#
# Reads INDEXNOW_KEY without printing it; prints only counts, sample addresses and HTTP codes.
set -u
MAPS=""; MATCH=""; DRY=0; FORCE=0; URLS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --maps) MAPS="$2"; shift 2 ;;
    --match) MATCH="$2"; shift 2 ;;
    --url) URLS+=("$2"); shift 2 ;;
    --dry-run) DRY=1; shift ;;
    --force) FORCE=1; shift ;;
    *) echo "unknown argument: $1"; exit 2 ;;
  esac
done
[ -n "$MAPS" ] || [ ${#URLS[@]} -gt 0 ] || { echo "name --maps <regex> and/or --url <address>; nothing is sent by default"; exit 2; }

HOST=essenthos.org
STAMP=$HOME/.indexnow-last
if [ $DRY -eq 0 ] && [ $FORCE -eq 0 ] && [ -f "$STAMP" ] && [ $(( $(date +%s) - $(stat -c %Y "$STAMP") )) -lt 86400 ]; then
  echo "a batch went out at $(date -u -r "$STAMP" '+%Y-%m-%d %H:%M UTC'), less than 24 h ago; wait, or --force"
  exit 3
fi

KEY=$(sed -n 's/^INDEXNOW_KEY=//p' /srv/essenthos/deploy/.env | tr -d '"\r')
[ -n "$KEY" ] || { echo "no key"; exit 1; }
SERVED=$(curl -s --resolve $HOST:443:127.0.0.1 "https://$HOST/$KEY.txt" | tr -d '\r\n')
[ "$SERVED" = "$KEY" ] || { echo "key file does not match"; exit 1; }

W=$(mktemp -d); trap 'rm -rf "$W"' EXIT
: > "$W/urls"
if [ -n "$MAPS" ]; then
  curl -s --resolve $HOST:443:127.0.0.1 "https://$HOST/sitemap.xml" | grep -o '<loc>[^<]*</loc>' | sed 's/<[^>]*>//g' \
    | grep -E "/sitemaps/($MAPS)\.xml$" > "$W/maps"
  echo "lists: $(wc -l < "$W/maps") ($(sed 's#.*/sitemaps/##; s#\.xml$##' "$W/maps" | head -8 | tr '\n' ' ')...)"
  while read -r m; do
    curl -s --resolve $HOST:443:127.0.0.1 "$m" | grep -o '<loc>[^<]*</loc>' | sed 's/<[^>]*>//g'
  done < "$W/maps" >> "$W/urls"
fi
for u in "${URLS[@]}"; do echo "$u" >> "$W/urls"; done
if [ -n "$MATCH" ]; then grep -E "$MATCH" "$W/urls" > "$W/kept" || true; mv "$W/kept" "$W/urls"; fi
sort -u "$W/urls" -o "$W/urls"
N=$(wc -l < "$W/urls")
echo "addresses: $N"; head -3 "$W/urls" | sed 's/^/  /'
[ "$N" -gt 0 ] || { echo "nothing to send"; exit 0; }
grep -vq "^https://$HOST/" "$W/urls" && { echo "an address is not on https://$HOST/"; exit 1; }
[ $DRY -eq 1 ] && { echo "dry run: nothing sent"; exit 0; }

cd "$W" && split -l 10000 urls batch-
FAILED=0
for b in batch-*; do
  python3 - "$b" "$KEY" "$HOST" > body.json <<'PY'
import json,sys
urls=[l.strip() for l in open(sys.argv[1]) if l.strip()]
print(json.dumps({"host":sys.argv[3],"key":sys.argv[2],"keyLocation":f"https://{sys.argv[3]}/{sys.argv[2]}.txt","urlList":urls}))
PY
  code=$(curl -s -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json; charset=utf-8' --data @body.json https://api.indexnow.org/indexnow)
  echo "$b $(wc -l < "$b") -> $code"
  case "$code" in 200|202) ;; *) FAILED=1 ;; esac
done
[ $FAILED -eq 0 ] && touch "$STAMP"
exit $FAILED
