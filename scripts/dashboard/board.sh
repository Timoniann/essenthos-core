#!/usr/bin/env bash
# Values for the avioniq dashboard tiles about the board. Run from the control folder.
# Usage: board.sh <review|questions|open-high>
set -euo pipefail
count() { grep -o '"id"' | wc -l | tr -d ' '; }
case "${1:?name a metric}" in
  review)    echo $(( $(avioniq tasks list --status review --json --fields id | count) + $(avioniq problems list --status review --json --fields id | count) )) ;;
  questions) avioniq questions | grep -cE '^\s*[A-Z]{3}-[0-9]+' || true ;;
  open-high) echo $(( $(avioniq problems list --open --priority high --priority critical --json --fields id | count) )) ;;
  *)         echo "unknown metric $1" >&2; exit 2 ;;
esac
