# The server

One DigitalOcean droplet running everything in containers: Postgres, production and dev side by side,
and Caddy in front. The design and its reasons are DOC-0205; the publication of the corpus is DOC-0203.

    essenthos.org          the reader, and its API at /v1
    api.essenthos.org      the same API, for the mobile app and other clients
    dev.essenthos.org      dev's reader and API, behind a password
    devapi.essenthos.org   dev's API alone, behind the same password

Two things reach the server, separately:

- **the code** — `scripts/deploy.ps1`, which copies this folder's files as that commit holds them,
  never as they stand on disk, and sets the image tags CI pushed for it;
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

Each of these can be tried first: `scripts/deploy.ps1 … -WhatIf` and `forge release | publish | rollback
… --dry-run` say what they would do and touch nothing.

The owner's console (`Essenthos.Desk`) has a Deployment section that runs the same commands, behind a
confirmation that says what will happen (production also wants its name typed), and records every run
in `Resources/Essenthos/owner-changes.jsonl` under section `deploy`. It shows what each environment
runs by asking `/v1/health/version` (the API's commit and corpus release) and `/version.json` (the
reader's commit), both of which the images carry from the `SOURCE_COMMIT` build argument CI passes.
Dev is behind a password; to let the console read dev's versions, give it the same user and password
in user secrets as `Desk:Deploy:Environments:dev:User` and `…:Password` — otherwise it reports dev as
answering behind its password and nothing more.

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

- The `backup` service dumps it every night into `/srv/essenthos/backups` and keeps each dump **14 days**
  (`BACKUP_KEEP_DAYS`), not a day more. The droplet's own daily backup covers that folder.
- **Every dump is encrypted as it is written**, with gpg, to a public key in `/srv/essenthos/backup-key/`.
  No dump is ever on the disk in the clear, so neither the droplet's backups nor a stolen disk yield a
  reader's email or reading history. The private key is never on the server. Until a key is there the
  service keeps writing plain dumps and says so in its log on every run; once one is, the next run
  encrypts and removes the plain dumps it replaces.
- **Off the droplet**, optionally: the `backup-offsite` service copies the encrypted dumps to an rclone
  remote every hour and removes the copies there once they are 14 days old, so the off-site copy is kept
  exactly as long as the dump here. Only `*.dump.gpg` files are sent. It runs only when `.env` turns it on.
- A change to `backup.sh` or `backup-offsite.sh` reaches a running container only when it restarts:
  `docker compose up -d --force-recreate backup backup-offsite` after a deploy that changed either.

### Once: the key

On your own machine, never on the server, make a key pair that exists only for these backups:

    gpg --quick-generate-key "Essenthos backups" rsa4096 encr never
    gpg --armor --export "Essenthos backups" > essenthos-backups.asc
    gpg --armor --export-secret-keys "Essenthos backups" > essenthos-backups-SECRET.asc

Keep `essenthos-backups-SECRET.asc` and its passphrase where the rest of what cannot be rebuilt is kept —
E:, and one more place that is not this PC. Without it no backup can be read, by anyone, including you.
Then put the **public** half on the server and restart the service:

    ssh deploy@<droplet> 'mkdir -p /srv/essenthos/backup-key'
    scp essenthos-backups.asc deploy@<droplet>:/srv/essenthos/backup-key/
    ssh deploy@<droplet> 'cd /srv/essenthos/deploy && docker compose up -d --force-recreate backup &&
      docker compose logs --tail 5 backup'

The log says `encrypted` beside each dump. Only one `.asc` file belongs in that folder.

### Once, if the dumps are to leave the droplet: the off-site copy

Anything rclone can write to works: an S3-compatible bucket (DigitalOcean Spaces, Backblaze B2,
Cloudflare R2) or a host reached over ssh. Make a bucket or a folder that holds nothing else, and a key
for it that can write, list and delete there and nowhere else. Then write the remote into
`/srv/essenthos/rclone/rclone.conf` on the server, `chmod 600` it, and name it in `.env`:

    # /srv/essenthos/rclone/rclone.conf — a bucket (provider: DigitalOcean, Cloudflare, Other; B2 speaks S3 too)
    [offsite]
    type = s3
    provider = DigitalOcean
    endpoint = fra1.digitaloceanspaces.com
    access_key_id = <key>
    secret_access_key = <secret>

    # …or a host over ssh, with a key made for this and nothing else
    [offsite]
    type = sftp
    host = <host>
    user = <user>
    key_file = /config/rclone/id_ed25519

    # .env
    COMPOSE_PROFILES=offsite
    BACKUP_OFFSITE=offsite:<bucket or folder>/essenthos

    docker compose up -d backup-offsite && docker compose logs --tail 5 backup-offsite

Give the bucket a lifecycle rule deleting objects after 15 days as well, so a copy expires even if the
service stops. **The privacy page says the backups are copied nowhere else: change it in the same step,**
naming where they go and that they are kept 14 days there.

### Restoring

**A restore has to be run once on the server before the backup is believed.** Into a scratch database,
never over the live one. Only the private key can open a dump, so it is decrypted on your machine and
streamed back into `pg_restore`, and the dump is never on the server in the clear:

    scp deploy@<droplet>:/srv/essenthos/backups/essenthos_app-<stamp>.dump.gpg .
    gpg --import essenthos-backups-SECRET.asc        # once, on the machine that restores
    gpg --decrypt essenthos_app-<stamp>.dump.gpg | ssh deploy@<droplet> 'cd /srv/essenthos/deploy &&
      docker compose exec -T backup sh -c "createdb -h db restore_test && pg_restore -h db -d restore_test &&
        psql -h db -d restore_test -c \"\dt\" && dropdb -h db restore_test"'

A dump written through gpg was written to a pipe, so `pg_restore` reads it front to back: one table can
still be restored out of it with `-t`, but not in parallel with `-j`.

The unencrypted procedure was run on the rehearsal on 2026-09-18: a row written to `essenthos_app`,
backed up, restored into `restore_test` and read back. The encrypted one has not been run yet.
