# The server

One DigitalOcean droplet running everything in containers: Postgres, production and dev side by side,
and Caddy in front. The design and its reasons are DOC-0205; the publication of the corpus is DOC-0203.

    essenthos.org          the reader, and its API at /v1
    api.essenthos.org      the same API, for the mobile app and other clients
    dev.essenthos.org      dev's reader and API, behind a password
    devapi.essenthos.org   dev's API alone, behind the same password

Two things reach the server, separately:

- **the code** — `scripts/deploy.ps1`, which copies this folder's tracked files and sets the image tags
  CI pushed for a commit;
- **the corpus** — `forge publish --to dev`, then `--to prod`, from the machine that built it. The
  release carries the rows that name the pictures of people and places; publishing also sends the
  picture files, from this machine's `Resources/Images`, into `/srv/essenthos/images/corpus_dev` or
  `…/corpus`, which the API mounts read-only at `/images`. Only new or changed files travel, nothing
  is ever removed — the previous release, which a rollback puts back, names pictures too — and a
  release naming a picture the server does not have is refused before it is swapped in.

Everything below can be rehearsed on a workstation first, and should be before anything changes on the
server:

    docker compose --env-file .env.rehearsal -f compose.yaml -f compose.rehearsal.yaml up -d
    forge publish --to rehearsal-dev ; forge publish --to rehearsal

## Once: the droplet

1. **Create it.** Ubuntu 24.04, Basic, 8 GB / 4 vCPU / 160 GB, in the region nearest the readers.
   Add your SSH key. Turn on **backups**.
   Do not attach a Block Storage volume for the data: droplet backups do not include volumes.
2. **Firewall.** A DigitalOcean cloud firewall on the droplet allowing inbound 22, 80 and 443 (TCP) and
   443 (UDP), and nothing else. Postgres is bound to the droplet's loopback and is reached through ssh.
3. **DNS.** A records for `essenthos.org`, `api`, `dev` and `devapi` pointing at the droplet. Caddy
   obtains the certificates on first start; it needs the names to resolve first.
4. **Docker**, from Docker's own apt repository (docs.docker.com/engine/install/ubuntu), and a user for
   deploying:

        adduser --disabled-password deploy && usermod -aG docker deploy
        mkdir -p /srv/essenthos/deploy && chown -R deploy /srv/essenthos
        # then put your public key in /home/deploy/.ssh/authorized_keys

5. **The .env.** Copy `env.example` to `/srv/essenthos/deploy/.env` and fill it in. Every password
   fresh: `openssl rand -base64 32 | tr -d '/+='`. The Postgres passwords are read once, when the
   database is first created; changing one later is `ALTER ROLE`, not an edit here.
6. **On this machine**, the addresses Forge publishes to:

        dotnet user-secrets set "Publish:Targets:dev:Ssh" "deploy@<droplet>" --project Essenthos.Forge
        dotnet user-secrets set "Publish:Targets:prod:Ssh" "deploy@<droplet>" --project Essenthos.Forge
        dotnet user-secrets set "Publish:Targets:dev:Password" "<POSTGRES_PASSWORD>" --project Essenthos.Forge
        dotnet user-secrets set "Publish:Targets:prod:Password" "<POSTGRES_PASSWORD>" --project Essenthos.Forge

7. **First deploy and first corpus:**

        ./scripts/deploy.ps1 -Server deploy@<droplet> -Environment prod -Commit <core sha> -WebCommit <web sha>
        forge release
        forge publish --to dev
        forge publish --to prod

   Until the first publication the API answers `/v1/health/ready` with 503: there is no corpus yet.
   The first publication to each environment also sends every picture, about 180 MB.

## Every time

| To | Run |
|---|---|
| ship code to dev | `scripts/deploy.ps1 -Environment dev -Commit <sha> [-WebCommit <sha>]` |
| ship code to production | the same with `-Environment prod` |
| undo a code deploy | the same with the previous commit |
| ship a corpus | `forge release`, `forge publish --to dev`, look at dev, `forge publish --to prod` |
| undo a corpus | `forge rollback --to prod` |
| see what is where | `forge releases`, `forge releases --on prod` |

A publication stops the API for the second or two the rename takes; Caddy holds requests through it.
Measured on the rehearsal under continuous traffic: 2,230 requests, none failed, the slowest 5.4 s.

## The database's extensions

The corpus compares spellings with `similarity()` from `pg_trgm`, a contrib module of Postgres itself,
and one migration creates it. Creating an extension needs a superuser or the database's owner, and the
roles that restore and read a release are neither, so the container creates it instead:
`initdb/00-extensions.sh` puts it into `template1` on the first start, as the superuser, and every
database created afterwards has it — each release `forge publish` restores, and the app databases.
The workstation's own `compose.yaml` does the same through `../initdb`, into the corpus database and
`template1`.

Both run only when the data directory is created. On a server whose Postgres was initialised before
the script existed, run it once by hand:

    docker compose exec db psql -U postgres -d template1 -c 'CREATE EXTENSION IF NOT EXISTS pg_trgm;'

## Backups

The corpus needs none: it is an artefact, rebuilt from sources this machine keeps. `essenthos_app` —
accounts, and later what readers write — is the only thing on the server that cannot be rebuilt.

- The `backup` service dumps it every night into `/srv/essenthos/backups`, keeping 14 days, and the
  droplet's own daily backup covers that folder.
- **Not yet done:** a copy off the droplet. A backup on the machine it protects does not survive losing
  the machine.
- **A restore has to be run once on the server before the backup is believed.** Into a scratch
  database, never over the live one, from the `backup` service, which has the files and the password:

        docker compose exec backup sh -c 'createdb -h db restore_test &&
          pg_restore -h db -d restore_test /backups/essenthos_app-<stamp>.dump &&
          psql -h db -d restore_test -c "\dt" && dropdb -h db restore_test'

  Run on the rehearsal on 2026-09-18: a row written to `essenthos_app`, backed up, restored into
  `restore_test` and read back.
