#!/bin/sh
# Once a day, a custom-format dump of each database in BACKUP_DATABASES into /backups, keeping
# BACKUP_KEEP_DAYS of them. A dump rather than a copy of the data directory because it restores into
# any later Postgres and can be read table by table; custom format because pg_restore can then restore
# one table out of it.
#
# An untested backup is a belief. The restore is written down in deploy/README.md and has to be run
# once before this is relied on.
set -eu

run() {
	stamp=$(date -u +%Y%m%dT%H%MZ)
	for database in $BACKUP_DATABASES; do
		file="/backups/$database-$stamp.dump"
		if pg_dump -Fc -Z6 -d "$database" -f "$file.partial"; then
			mv "$file.partial" "$file"
			echo "backup: $file $(wc -c < "$file") bytes"
		else
			rm -f "$file.partial"
			echo "backup: $database FAILED" >&2
		fi
	done
	find /backups -name '*.dump' -mtime "+$BACKUP_KEEP_DAYS" -delete
}

# One on start, so a fresh deployment has a backup the same day and a broken one says so at once.
run
while :; do
	now=$(date -u +%s)
	next=$(date -u -d "$(date -u +%Y-%m-%d) $BACKUP_HOUR_UTC:00" +%s)
	[ "$next" -le "$now" ] && next=$((next + 86400))
	sleep $((next - now))
	run
done
