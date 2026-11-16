#!/usr/bin/env bash
# Values for the avioniq dashboard tiles about production. Read-only: public
# pages, the proxy's access log, Umami counts and file dates on the server.
# Never reads .env, secrets or a container's configuration.
# Usage: prod.sh <site|page-ms|api|web|googlebot|requests|errors|notfound|visitors|google|backup|disk>
set -euo pipefail
metric="${1:?name a metric}"
case "$metric" in
  site)    curl -s -o /dev/null -m 15 -w '%{http_code}' https://essenthos.org/ ;;
  # The median of five: one request from the workstation swings by a second with the line.
  page-ms) for i in 1 2 3 4 5; do curl -s -o /dev/null -m 30 -w '%{time_total}
' https://essenthos.org/read/genesis/1; done | sort -n | sed -n 3p | awk '{printf "%d", $1 * 1000}' ;;
  *)       ssh -o BatchMode=yes -o ConnectTimeout=15 deploy@2.29.125.205 'bash -s' -- "$metric" < "$(dirname "$0")/prod-remote.sh" ;;
esac
