#!/bin/sh
# Runs once, when the data directory is first created.
#
# The corpus compares spellings with similarity() from pg_trgm, and creating an extension needs a
# superuser or the owner of the database. So it is created here, as the superuser: in the corpus
# database the image has just created, and in template1, so that every database created later —
# the scratch database each test run builds, a copy made to measure something — has it from birth.
set -eu

for database in template1 "$POSTGRES_DB"; do
	psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$database" \
		-c 'CREATE EXTENSION IF NOT EXISTS pg_trgm;'
done
