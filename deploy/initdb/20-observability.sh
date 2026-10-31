#!/bin/sh
# What the database says about itself: statements over half a second in its log, time spent reading and
# writing blocks, and a table of what each kind of statement costs (deploy/README.md, "Reading the logs").
#
# Written with ALTER SYSTEM rather than as flags in compose.yaml, because a changed flag recreates the
# container at the next deploy, and a restart empties the database's cache. So this reaches a running
# server without one: the log and timing settings at once, on the reload below, and pg_stat_statements,
# which only a start can load, whenever the database next starts for its own reasons. Run by hand on a
# server initialised before it existed; safe to run again.
#
# A statement's parameters stay out of the log: a slow search's are a reader's own words.
set -eu

psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres <<'SQL'
ALTER SYSTEM SET log_min_duration_statement = '500ms';
ALTER SYSTEM SET log_parameter_max_length = 0;
ALTER SYSTEM SET track_io_timing = on;
ALTER SYSTEM SET shared_preload_libraries = 'pg_stat_statements';
ALTER SYSTEM SET pg_stat_statements.max = 1000;
SELECT pg_reload_conf();
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
SQL
