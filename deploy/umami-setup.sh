#!/bin/sh
# Umami's place on the shared Postgres, in the two steps either side of its migrations. Each may run again.
#
#   database   as postgres: its role and database, which can reach no other. Stops the whole start when
#              a value in .env is missing, rather than starting Umami on an empty password or with its
#              hashes keyed by nothing but the database's address.
#   site       as that role: the admin's password from .env in place of the default the migrations write,
#              and the site's row, under the id the reader's build sends (analytics.ts). A password
#              changed since in the dashboard is left alone: only the migration's own hash of the default,
#              admin/umami, is replaced.
#
# Passwords reach psql as variables, so they are in no command line and no log line.
set -eu

SITE_ID=5e1f2a50-7c3b-4d8e-9a61-3f0c8b2d7e41

case "${1:-}" in
database)
	for name in UMAMI_DB_PASSWORD UMAMI_APP_SECRET UMAMI_ADMIN_PASSWORD; do
		eval "value=\${$name:-}"
		if [ -z "$value" ]; then
			echo "umami: $name is not set in .env (deploy/README.md, \"Page counts\")" >&2
			exit 1
		fi
	done

	psql -v ON_ERROR_STOP=1 -d postgres -v password="$UMAMI_DB_PASSWORD" <<'SQL'
SELECT 'CREATE ROLE essenthos_umami LOGIN PASSWORD ' || quote_literal(:'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'essenthos_umami') \gexec
SELECT 'CREATE DATABASE essenthos_umami OWNER essenthos_umami'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'essenthos_umami') \gexec
REVOKE ALL ON DATABASE essenthos_umami FROM PUBLIC;
SQL
	;;
site)
	psql -v ON_ERROR_STOP=1 -v password="$UMAMI_ADMIN_PASSWORD" -v id="$SITE_ID" \
		-v domain="${SITE_DOMAIN:-essenthos.org}" <<'SQL'
UPDATE "user"
SET password = crypt(:'password', gen_salt('bf', 10))
WHERE username = 'admin' AND password = '$2b$10$BUli0c.muyCW1ErNJc3jL.vFRFtFJWrT8/GcR4A.sUdCznaXiqFXa';

INSERT INTO website (website_id, name, domain, user_id, created_by)
SELECT :'id'::uuid, 'Essenthos', :'domain', user_id, user_id
FROM "user" WHERE username = 'admin'
ON CONFLICT (website_id) DO NOTHING;
SQL
	;;
*)
	echo "usage: umami-setup.sh database|site" >&2
	exit 2
	;;
esac
