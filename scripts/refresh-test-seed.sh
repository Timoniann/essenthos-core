#!/bin/bash
# The agents' test seed: a copy of the seed on core-db restored on core-test-db, with its build/served
# state, so an agent proves a corpus change by cloning it and applying what its branch moved
# (build --into <proof> --seed test_seed) instead of building the whole corpus fresh.
#
# The lead runs it after a landing, once the seed is the database just landed (guide Database, "The
# daily path"): avioniq services run core-test-seed. It reads the seed on core-db and writes only
# core-test-db and build/served/<target> in the store.
#
#   $1  the database copied from core-db (default essenthos_core_seed)
#   $2  its name on core-test-db (default test_seed)
#   $3  the Forge that writes the served state (default .run/forge, main's build)
#   $4  the Resources folder whose store gets build/served/<target> (default Resources, main's)
#
# While pg_dump reads the seed (under a minute) nothing else may clone it: core-daily refuses a seed
# with a session on it. The copy is restored under <target>_new and renamed over the old one only when
# it is whole, so a failed refresh leaves the previous test seed and its served state as they were.
set -euo pipefail
export MSYS_NO_PATHCONV=1

source=${1:-essenthos_core_seed}
target=${2:-test_seed}
forge=${3:-.run/forge}
resources=${4:-Resources}
core=essenthos-core-db-1
test=essenthos-core-test-db-1
# On each server's volume beside its data directory (/var/lib/postgresql/18/docker), not in a
# container's /tmp: a container copies the dump from one volume to the other at disk speed, where a
# tar stream through docker exec moved 7.4 GB in six minutes.
dump=/var/lib/postgresql/${target}_dump
image=$(grep -m1 -oE 'postgres:18[^[:space:]]*' compose.yaml)
served="$resources/Essenthos/build/served/${source}.tsv"
start=$SECONDS

say() { echo "$(date +%H:%M:%S) [$((SECONDS - start)) s] $*"; }
on_test() { docker exec "$test" psql -v ON_ERROR_STOP=1 -U essenthos -d postgres -Atqc "$1"; }

[[ $target =~ ^[a-z0-9_]+$ && $source =~ ^[a-z0-9_]+$ ]] || { echo "names are lower-case letters, digits and _" >&2; exit 2; }
[[ $target != essenthos_core* ]] || { echo "the test seed is never named like a database of core-db ($target)" >&2; exit 2; }
[[ -f $served ]] || { echo "no $served: run this from essenthos-core after a landing" >&2; exit 2; }
[[ -n $image ]] || { echo "no postgres:18 image named in compose.yaml" >&2; exit 2; }
before=$(sha256sum "$served" | cut -d' ' -f1)

docker exec "$core" sh -c "rm -rf $dump && pg_dump -U \"\$POSTGRES_USER\" -Fd -j 6 -Z lz4 -f $dump $source"
say "dumped $source on core-db ($(docker exec "$core" du -sh "$dump" | cut -f1))"

docker exec "$test" rm -rf "$dump"
docker run --rm --entrypoint cp -v essenthos-core_corpus:/from:ro -v essenthos-core_test-scratch:/to "$image"     -a "/from/${target}_dump" /to/
docker exec "$core" rm -rf "$dump"
say "copied to core-test-db's volume"

on_test "DROP DATABASE IF EXISTS ${target}_new WITH (FORCE)"
on_test "CREATE DATABASE ${target}_new"
docker exec -e PGOPTIONS="-c maintenance_work_mem=1GB" "$test" \
    pg_restore -U essenthos -d "${target}_new" -j 12 --no-owner --exit-on-error "$dump"
docker exec "$test" rm -rf "$dump"
say "restored as ${target}_new"

# Done once here rather than in every clone: a restored database has no statistics and no visibility map.
docker exec "$test" vacuumdb -U essenthos -d "${target}_new" -j 8 --analyze --quiet
say "vacuumed and analyzed"

[[ $(sha256sum "$served" | cut -d' ' -f1) == "$before" ]] || {
    on_test "DROP DATABASE ${target}_new WITH (FORCE)"
    echo "$served changed while copying: the seed was replaced meanwhile; run again" >&2
    exit 1
}

# Nobody may connect to it: Postgres refuses to clone a database with a session on it.
on_test "ALTER DATABASE ${target}_new WITH ALLOW_CONNECTIONS false"
on_test "DROP DATABASE IF EXISTS ${target}_old WITH (FORCE)"
if [[ -n $(on_test "SELECT 1 FROM pg_database WHERE datname = '${target}'") ]]; then
    on_test "ALTER DATABASE ${target} RENAME TO ${target}_old"
fi
on_test "ALTER DATABASE ${target}_new RENAME TO ${target}"
on_test "DROP DATABASE IF EXISTS ${target}_old WITH (FORCE)"
say "${target} replaced"

Dataset__ResourcesPath="$resources" dotnet "$forge/Essenthos.Forge.dll" clone "$source" "$target" --served-only || {
    on_test "DROP DATABASE ${target} WITH (FORCE)"
    echo "build/served/${target} not written; ${target} dropped so no proof applies against a wrong state" >&2
    exit 1
}
say "build/served/${target} written; done"
