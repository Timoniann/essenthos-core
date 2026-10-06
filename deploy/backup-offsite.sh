#!/bin/sh
# Every hour, the encrypted dumps the backup service writes copied to BACKUP_OFFSITE — an rclone remote
# and a path in it, such as offsite:essenthos-backups/app — and the copies there older than
# BACKUP_KEEP_DAYS removed once the backup service has removed them here, so a dump is kept off the
# droplet exactly as long as it is kept on it: past the window only while that database's backups are
# failing and the backup service is holding its last good dumps.
#
# Only *.dump.gpg files are ever sent: a dump in the clear, written only where plain dumps were asked
# for, stays on the droplet and is never copied anywhere. The remote is defined in
# /config/rclone/rclone.conf, which the owner writes on the server; deploy/README.md says how, for a
# bucket or for an ssh host.
set -eu

: "${BACKUP_OFFSITE:?name the rclone remote and path to copy to, e.g. offsite:essenthos-backups/app}"

run() {
	run_status=0
	if rclone copy /backups "$BACKUP_OFFSITE" --include '*.dump.gpg' --no-traverse; then
		echo "backup-offsite: copied to $BACKUP_OFFSITE"
	else
		echo "backup-offsite: copying to $BACKUP_OFFSITE FAILED" >&2
		run_status=1
	fi

	# rclone keeps each file's own time on the remote, so its age there is its age here. A copy whose
	# dump is still here is one the backup service is holding, and stays.
	rules=$(mktemp)
	for kept in /backups/*.dump.gpg; do
		if [ -f "$kept" ]; then
			echo "- /$(basename "$kept")" >> "$rules"
		fi
	done
	printf '%s\n' '+ *.dump.gpg' '- **' >> "$rules"
	if ! rclone delete "$BACKUP_OFFSITE" --filter-from "$rules" --min-age "$((BACKUP_KEEP_DAYS * 1440 - 30))m"; then
		echo "backup-offsite: removing expired copies from $BACKUP_OFFSITE FAILED" >&2
		run_status=1
	fi
	rm -f "$rules"
	return "$run_status"
}

# Called as a plain command, never in a condition, so set -e holds inside it and a failure ends the
# script with its status.
if [ "${1:-}" = --once ]; then
	run
	exit
fi

# Each copy is its own process, for the same reason.
while :; do
	sh "$0" --once || echo "backup-offsite: this copy failed; the next is in an hour" >&2
	sleep 3600
done
