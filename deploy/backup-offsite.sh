#!/bin/sh
# Every hour, the encrypted dumps the backup service writes copied to BACKUP_OFFSITE — an rclone remote
# and a path in it, such as offsite:essenthos-backups/app — and the copies there older than
# BACKUP_KEEP_DAYS removed, so a dump is kept off the droplet exactly as long as it is kept on it.
#
# Only *.dump.gpg files are ever sent: a dump in the clear, from before the key was there, stays on the
# droplet and is never copied anywhere. The remote is defined in /config/rclone/rclone.conf, which the
# owner writes on the server; deploy/README.md says how, for a bucket or for an ssh host.
set -eu

: "${BACKUP_OFFSITE:?name the rclone remote and path to copy to, e.g. offsite:essenthos-backups/app}"

run() {
	if rclone copy /backups "$BACKUP_OFFSITE" --include '*.dump.gpg' --no-traverse; then
		echo "backup-offsite: copied to $BACKUP_OFFSITE"
	else
		echo "backup-offsite: copying to $BACKUP_OFFSITE FAILED" >&2
	fi

	# rclone keeps each file's own time on the remote, so its age there is its age here.
	if ! rclone delete "$BACKUP_OFFSITE" --include '*.dump.gpg' --min-age "$((BACKUP_KEEP_DAYS * 1440 - 30))m"; then
		echo "backup-offsite: removing expired copies from $BACKUP_OFFSITE FAILED" >&2
	fi
}

while :; do
	run
	sleep 3600
done
