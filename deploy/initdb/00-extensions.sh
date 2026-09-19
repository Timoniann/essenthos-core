#!/bin/sh
# Runs once, when the data directory is first created, before the roles.
#
# The corpus compares spellings with similarity() from pg_trgm, and creating an extension needs a
# superuser or the owner of the database. Nothing that restores or migrates a corpus later is either
# for certain, so the extension is put into template1 here, as the superuser, and every database
# created afterwards — each release `forge publish` restores, and the app databases — has it from
# birth. The migration that asks for it then finds it there.
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname template1 \
	-c 'CREATE EXTENSION IF NOT EXISTS pg_trgm;'
