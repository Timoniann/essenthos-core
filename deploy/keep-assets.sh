#!/bin/sh
# Keeps the reader's scripts and styles of the builds a deploy replaces, so a tab left open across a
# deploy, or a search engine rendering a page with the entry script it cached, still finds the chunks
# that script asks for. The web container serves this folder behind its own /assets (nginx.conf).
#
#   keep-assets.sh <folder> <image>...
#
# Copies each image's /assets into the folder, over any file of the same name, so every file of
# these builds is dated now; then deletes what no deploy has dated for KEEP_DAYS (21 by default).
# deploy.ps1 names the image it is replacing and the one it starts, so a build's files stay for
# KEEP_DAYS after it was last served. Vite names a file after its content, so a name never stands for
# two different files and nothing here can break the build being started.
set -eu

kept=$1
shift
days=${KEEP_DAYS:-21}
mkdir -p "$kept"

for image in "$@"; do
	[ -n "$image" ] || continue
	if ! docker image inspect "$image" >/dev/null 2>&1; then
		echo "keep-assets: there is no $image here, so its files are not kept" >&2
		continue
	fi
	docker run --rm --network none --user "$(id -u):$(id -g)" -v "$kept:/kept" --entrypoint sh "$image" \
		-c 'cd /usr/share/nginx/html/assets && cp -Rf . /kept/'
done

pruned=$(find "$kept" -type f -mtime +"$days" -print -delete | wc -l)
echo "keep-assets: $(find "$kept" -type f | wc -l) files kept in $kept, $pruned older than $days days removed"
