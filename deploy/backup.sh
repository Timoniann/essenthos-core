#!/bin/sh
# Once a day, a custom-format dump of each database in BACKUP_DATABASES into /backups, keeping
# BACKUP_KEEP_DAYS of them. A dump rather than a copy of the data directory because it restores into
# any later Postgres and can be read table by table; custom format because pg_restore can then restore
# one table out of it.
#
# Each dump is encrypted as it is written, to the public key in /backup-key, so no readable copy of a
# reader's email or reading history ever lands on the disk — or in the droplet's own backups of it, or
# off the droplet. The private key is never on the server: a dump is only readable where the owner
# keeps it. Without a key nothing is written and the run fails, unless BACKUP_ALLOW_UNENCRYPTED=yes
# says plain dumps are wanted on purpose.
#
# Every database whose run succeeded gets /backups/.last-success-<database>; `backup.sh --check`, the
# container's healthcheck, fails once any of them is older than BACKUP_STALE_HOURS (26 by default). A
# database whose latest run failed keeps every dump it has, however old, until a run succeeds again,
# so an outage never ages out the last good one.
#
# An untested backup is a belief. The restore is written down in deploy/README.md and has to be run
# once before this is relied on.
set -eu

KEYS=/backup-key

# The key, where one has been put there: the first ASCII-armoured public key in the folder.
key() {
	for candidate in "$KEYS"/*.asc; do
		if [ -f "$candidate" ]; then
			echo "$candidate"
			return 0
		fi
	done
	return 0
}

# The dump of one database through gpg, never touching the disk in the clear. gpg reads the key from the
# file itself, so nothing is imported and the keyring it needs is thrown away with the run.
encrypted() {
	database=$1
	target=$2
	failed=$(mktemp)
	GNUPGHOME=$(mktemp -d)
	export GNUPGHOME
	{ pg_dump -Fc -Z6 -d "$database" || echo failed > "$failed"; } |
		gpg --batch --quiet --yes --trust-model always --compress-algo none \
			--recipient-file "$recipient" --output "$target" --encrypt
	status=$?
	rm -rf "$GNUPGHOME"
	[ "$status" -eq 0 ] && [ ! -s "$failed" ] && [ -s "$target" ]
	status=$?
	rm -f "$failed"
	return "$status"
}

# One database's dump, encrypted unless plain dumps were asked for. Fails without writing anything
# where there is no key and they were not.
dump() {
	database=$1
	if [ -n "$recipient" ]; then
		file="/backups/$database-$stamp.dump.gpg"
		if encrypted "$database" "$file.partial"; then
			mv "$file.partial" "$file"
			echo "backup: $file $(wc -c < "$file") bytes, encrypted"
			# A dump written in the clear before the key was there goes as soon as an encrypted one
			# of the same database stands in its place.
			find /backups -maxdepth 1 -name "$database-*.dump" -delete
			return 0
		fi
		rm -f "$file.partial"
		return 1
	fi

	if [ "${BACKUP_ALLOW_UNENCRYPTED:-}" != yes ]; then
		echo "backup: no public key in $KEYS and BACKUP_ALLOW_UNENCRYPTED is not yes, so $database is not dumped; see deploy/README.md" >&2
		return 1
	fi

	file="/backups/$database-$stamp.dump"
	if pg_dump -Fc -Z6 -d "$database" -f "$file.partial"; then
		mv "$file.partial" "$file"
		echo "backup: $file $(wc -c < "$file") bytes, NOT encrypted (BACKUP_ALLOW_UNENCRYPTED=yes)"
		return 0
	fi
	rm -f "$file.partial"
	return 1
}

run() {
	run_status=0
	held=""
	stamp=$(date -u +%Y%m%dT%H%MZ)
	recipient=$(key)

	for database in $BACKUP_DATABASES; do
		# A database this server does not have — the page counter's, where it is off — is not a failure,
		# and there is nothing of it to keep.
		if ! present=$(psql -d postgres -Atc "SELECT 1 FROM pg_database WHERE datname = '$database'"); then
			echo "backup: $database FAILED, the database server did not answer" >&2
			run_status=1
			held="$held $database"
			continue
		fi
		if [ "$present" != 1 ]; then
			echo "backup: $database is not on this server, skipped"
			touch "/backups/.last-success-$database"
			continue
		fi

		if dump "$database"; then
			touch "/backups/.last-success-$database"
		else
			echo "backup: $database FAILED" >&2
			run_status=1
			held="$held $database"
		fi
	done

	# Kept for BACKUP_KEEP_DAYS and not a day more — except a database whose run just failed, whose dumps
	# all stay until one succeeds. Runs are a day apart and a dump's time is when it finished, so a dump
	# is at the edge of the window only minutes after the run that would remove it has started; half an
	# hour's grace makes that run the one that does. find's -mtime would round the age down to whole days
	# and keep each dump a day and more past what the privacy page says.
	set -- \( -name '*.dump' -o -name '*.dump.gpg' \) -mmin "+$((BACKUP_KEEP_DAYS * 1440 - 30))"
	for database in $held; do
		echo "backup: $database keeps every dump it has until a run succeeds" >&2
		set -- "$@" ! -name "$database-*"
	done
	find /backups -maxdepth 1 "$@" -delete
	find /backups -maxdepth 1 -name '*.partial' -mmin +1440 -delete
	return "$run_status"
}

# Whether every database has succeeded recently enough: the healthcheck.
check() {
	limit=$((${BACKUP_STALE_HOURS:-26} * 60))
	check_status=0
	for database in $BACKUP_DATABASES; do
		last="/backups/.last-success-$database"
		if [ ! -f "$last" ]; then
			echo "backup: $database has never been backed up" >&2
			check_status=1
		elif [ -n "$(find "$last" -mmin "+$limit")" ]; then
			echo "backup: $database last succeeded more than ${BACKUP_STALE_HOURS:-26} hours ago" >&2
			check_status=1
		fi
	done
	return "$check_status"
}

# Called as plain commands, never in a condition, so set -e holds inside them and a failure ends the
# script with its status.
case "${1:-}" in
	--once)
		run
		exit
		;;
	--check)
		check
		exit
		;;
esac

# Each run is its own process, so set -e holds inside it: a command that fails in a run ends that run
# as failed, where `run || true` here would have switched set -e off for everything run calls. One on
# start, so a fresh deployment has a backup the same day and a broken one says so at once.
sh "$0" --once || echo "backup: this run failed; the next is at $BACKUP_HOUR_UTC:00 UTC" >&2
while :; do
	now=$(date -u +%s)
	next=$(date -u -d "$(date -u +%Y-%m-%d) $BACKUP_HOUR_UTC:00" +%s)
	[ "$next" -le "$now" ] && next=$((next + 86400))
	sleep $((next - now))
	sh "$0" --once || echo "backup: this run failed; the next is at $BACKUP_HOUR_UTC:00 UTC" >&2
done
