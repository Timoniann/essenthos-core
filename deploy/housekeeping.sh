#!/bin/sh
# What must not be kept for longer than the privacy page says, once a day at HOUSEKEEPING_HOUR_UTC.
#
# The access log: Caddy rolls a file only by size, and a quiet site would keep its first lines for ever
# in the current file. So the day's lines are compressed out of it here and it is emptied in place, which
# Caddy, writing in append mode, does not notice. A line written between the two steps is lost; this is
# a log for abuse handling and not a record of anything. Every compressed file older than
# ACCESS_LOG_KEEP_DAYS goes, Caddy's own rolled ones too.
#
# Umami's rows: it has no retention of its own, so each one older than UMAMI_KEEP_DAYS is deleted here,
# with the sessions that have no event left. Skipped where the server has no Umami.
#
#   housekeeping.sh           the daily loop
#   housekeeping.sh --once    one run, with its real status
#   housekeeping.sh --check   the healthcheck: fails once the last good run is over 26 hours old
set -eu

LOGS=/logs
STAMP=$LOGS/.last-housekeeping
HOUR=${HOUSEKEEPING_HOUR_UTC:-2}
LOG_DAYS=${ACCESS_LOG_KEEP_DAYS:-28}
ROW_DAYS=${UMAMI_KEEP_DAYS:-730}

rotate_access_log() {
	if [ -s "$LOGS/access.log" ]; then
		daily="$LOGS/daily-$(date -u +%Y-%m-%d).log.gz"
		# Appended, since gzip members concatenate and a second run in a day must not lose the first.
		gzip -c "$LOGS/access.log" >> "$daily"
		: > "$LOGS/access.log"
	fi
	find "$LOGS" -maxdepth 1 -name '*.log.gz' -mtime "+$((LOG_DAYS - 1))" -delete
	echo "housekeeping: access log rotated, $(find "$LOGS" -maxdepth 1 -name '*.log.gz' | wc -l) files kept"
}

expire_umami_rows() {
	if [ -z "${PGPASSWORD:-}" ]; then
		return 0
	fi
	if ! psql -At -c 'SELECT 1' >/dev/null 2>&1; then
		if psql -At -d postgres -c 'SELECT 1' >/dev/null 2>&1; then
			echo "housekeeping: no Umami database yet"
			return 0
		fi
		echo "housekeeping: cannot reach Umami's database" >&2
		return 1
	fi
	psql -v ON_ERROR_STOP=1 -v days="$ROW_DAYS" -q <<'SQL'
BEGIN;
SET LOCAL statement_timeout = '10min';
CREATE TEMP TABLE cutoff ON COMMIT DROP AS SELECT now() - make_interval(days => :'days'::int) AS at;
DELETE FROM event_data WHERE created_at < (SELECT at FROM cutoff);
DELETE FROM website_event WHERE created_at < (SELECT at FROM cutoff);
DELETE FROM revenue WHERE created_at < (SELECT at FROM cutoff);
DELETE FROM session_data WHERE created_at < (SELECT at FROM cutoff);
DELETE FROM session_replay WHERE created_at < (SELECT at FROM cutoff);
DELETE FROM heatmap_event WHERE created_at < (SELECT at FROM cutoff);
DELETE FROM session_link WHERE created_at < (SELECT at FROM cutoff);
DELETE FROM session s
WHERE s.created_at < (SELECT at FROM cutoff)
  AND NOT EXISTS (SELECT FROM website_event e WHERE e.session_id = s.session_id)
  AND NOT EXISTS (SELECT FROM session_data d WHERE d.session_id = s.session_id)
  AND NOT EXISTS (SELECT FROM revenue r WHERE r.session_id = s.session_id);
COMMIT;
SQL
	echo "housekeeping: Umami rows older than $ROW_DAYS days deleted"
}

run_once() {
	rotate_access_log && expire_umami_rows && touch "$STAMP"
}

case "${1:-}" in
--once)
	run_once
	exit
	;;
--check)
	test -n "$(find "$STAMP" -mmin -1560 2>/dev/null)"
	exit
	;;
esac

while :; do
	now=$(date -u +%s)
	next=$(date -u -d "$(date -u +%Y-%m-%d) $HOUR:00:00" +%s)
	if [ "$next" -le "$now" ]; then
		next=$((next + 86400))
	fi
	sleep $((next - now))
	run_once || echo "housekeeping: this run failed; the next is tomorrow" >&2
done
