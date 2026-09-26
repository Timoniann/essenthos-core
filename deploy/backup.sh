#!/bin/sh
# Once a day, a custom-format dump of each database in BACKUP_DATABASES into /backups, keeping
# BACKUP_KEEP_DAYS of them. A dump rather than a copy of the data directory because it restores into
# any later Postgres and can be read table by table; custom format because pg_restore can then restore
# one table out of it.
#
# Each dump is encrypted as it is written, to the public key in /backup-key, so no readable copy of a
# reader's email or reading history ever lands on the disk — or in the droplet's own backups of it, or
# off the droplet. The private key is never on the server: a dump is only readable where the owner
# keeps it. Without a key the dump is written as before and every run says so, so deploying this before
# the key is made does not stop the backups.
#
# An untested backup is a belief. The restore is written down in deploy/README.md and has to be run
# once before this is relied on.
set -eu

KEYS=/backup-key

# The key, where one has been put there: the first ASCII-armoured public key in the folder.
key() {
	for candidate in "$KEYS"/*.asc; do
		[ -f "$candidate" ] && { echo "$candidate"; return 0; }
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

run() {
	stamp=$(date -u +%Y%m%dT%H%MZ)
	recipient=$(key)
	if [ -z "$recipient" ]; then
		echo "backup: no public key in $KEYS, so this run's dumps are NOT encrypted; see deploy/README.md" >&2
	fi

	for database in $BACKUP_DATABASES; do
		# A database this server does not have — the page counter's, where it is off — is not a failure.
		if ! present=$(psql -d postgres -Atc "SELECT 1 FROM pg_database WHERE datname = '$database'"); then
			echo "backup: $database FAILED, the database server did not answer" >&2
			continue
		fi
		if [ "$present" != 1 ]; then
			echo "backup: $database is not on this server, skipped"
			continue
		fi

		if [ -n "$recipient" ]; then
			file="/backups/$database-$stamp.dump.gpg"
			if encrypted "$database" "$file.partial"; then
				mv "$file.partial" "$file"
				echo "backup: $file $(wc -c < "$file") bytes, encrypted"
				# A dump written in the clear before the key was there goes as soon as an encrypted one
				# of the same database stands in its place.
				find /backups -name "$database-*.dump" -delete
			else
				rm -f "$file.partial"
				echo "backup: $database FAILED" >&2
			fi
			continue
		fi

		file="/backups/$database-$stamp.dump"
		if pg_dump -Fc -Z6 -d "$database" -f "$file.partial"; then
			mv "$file.partial" "$file"
			echo "backup: $file $(wc -c < "$file") bytes"
		else
			rm -f "$file.partial"
			echo "backup: $database FAILED" >&2
		fi
	done

	# Kept for BACKUP_KEEP_DAYS and not a day more. Runs are a day apart and a dump's time is when it
	# finished, so a dump is at the edge of the window only minutes after the run that would remove it
	# has started; half an hour's grace makes that run the one that does. find's -mtime would round the
	# age down to whole days and keep each dump a day and more past what the privacy page says.
	find /backups \( -name '*.dump' -o -name '*.dump.gpg' \) -mmin "+$((BACKUP_KEEP_DAYS * 1440 - 30))" -delete
	find /backups -name '*.partial' -mmin +1440 -delete
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
