#!/bin/sh
# Runs once, when the data directory is first created, and never again: an existing data directory
# skips everything in docker-entrypoint-initdb.d. Changing a password later is ALTER ROLE, not an edit
# to .env.
#
# Four roles, one pair per environment, and no role shared between two things:
#
#   essenthos_reader[_dev]   the API's view of the corpus. `forge publish` grants it SELECT on each
#                            release it restores, and nothing else, so the API cannot write the corpus
#                            however it is broken.
#   essenthos_app[_dev]      owns the app database — accounts and, later, what readers write — which
#                            the API migrates and writes.
#
# The corpus databases themselves are not created here. They arrive with the first publication.
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
	-v reader="$READER_PASSWORD" -v reader_dev="$READER_DEV_PASSWORD" \
	-v app="$APP_PASSWORD" -v app_dev="$APP_DEV_PASSWORD" <<'SQL'
CREATE ROLE essenthos_reader LOGIN PASSWORD :'reader';
CREATE ROLE essenthos_reader_dev LOGIN PASSWORD :'reader_dev';
CREATE ROLE essenthos_app LOGIN PASSWORD :'app';
CREATE ROLE essenthos_app_dev LOGIN PASSWORD :'app_dev';

CREATE DATABASE essenthos_app OWNER essenthos_app;
CREATE DATABASE essenthos_app_dev OWNER essenthos_app_dev;
REVOKE ALL ON DATABASE essenthos_app FROM PUBLIC;
REVOKE ALL ON DATABASE essenthos_app_dev FROM PUBLIC;
SQL
