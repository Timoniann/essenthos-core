# The server

One machine running everything in containers: Postgres, production and dev side by side, Caddy in
front, and optionally the page counter. The design and its reasons are DOC-0205, except where it put
the data on the machine's own disk — it now lives on an attached volume, as below; the publication of
the corpus is DOC-0203; the page counter is DOC-0211.

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
  release naming a picture the server does not have is refused before it is swapped in. The smaller
  copies a list or an avatar is served are made by the API itself, on the first request for each, and
  kept in the `image-cache` volume; any made ahead with `python scripts/picture-sizes.py` travel with
  the pictures and are served instead, which only spares their first readers the wait.

Everything below can be rehearsed on a workstation first, and should be before anything changes on the
server:

    docker compose --env-file .env.rehearsal -f compose.yaml -f compose.rehearsal.yaml up -d
    forge publish --to rehearsal-dev ; forge publish --to rehearsal

## The machine

**Data on a volume, everything else replaceable.** The machine has two disks:

| | What is on it | If it is lost |
|---|---|---|
| the machine's own disk | Ubuntu, Docker, the images and containers it pulled | make a new machine; nothing on it is unique |
| an attached **Block Storage volume**, mounted at `/srv/essenthos` | Postgres's data directory, the releases, the pictures, the backups, the certificates, the rclone and backup-key folders, and `deploy/` with its `.env` | this is the server; it is what the backups below protect |

So the machine can be replaced at any time: create a new one, attach the volume, install Docker, run
the deploy — the site comes back with its corpus, its accounts, its certificates and its settings
(**Moving to a new machine**, below). `HOST_DATA_ROOT` in `.env` is the volume's mount point.

**Backups do not come from the provider's machine backups.** DigitalOcean's droplet backups (and
Hetzner's server backups) copy the machine's own disk and **not an attached volume**, and the machine's
own disk holds nothing worth restoring. What protects the data:

1. **The encrypted nightly dumps** of the accounts databases (and the page counts, where the counter is
   on), written to the volume by the `backup` service — **Backups**, below;
2. **their off-site copy**: the owner's console copies the newest of them to his own machine every day
   it runs (**Backups**, below), and the `backup-offsite` service can copy them to an rclone remote as
   well — either is what survives the volume itself being lost;
3. optionally, **volume snapshots**, which catch the whole volume at once — the corpus releases and the
   pictures included, which saves re-publishing them after a disaster. Take one by hand before anything
   risky, or schedule them; keep them **four weeks at most**, since the privacy page promises that no
   copy of deleted account data outlives about six weeks.

The corpus itself needs no backup: it is an artefact, re-published from the workstation that built it.

**Size.** A published corpus is about **12–15 GB per copy** in Postgres, and the server holds four:
production's current release and the previous one a rollback puts back, and dev's pair. A publication
restores a fifth beside them before the swap, so plan for five copies, 60–75 GB, plus the pictures
(about 180 MB per environment), the releases uploaded for restore, the backups and the logs.

- **Volume: 100 GB or more.** A volume can be grown later without moving anything (resize it at the
  provider, then `resize2fs` on the machine); it cannot be shrunk.
- **Machine: start with a shared-CPU 8 GB / 4 vCPU.** `compose.yaml` caps Postgres at 4 GB,
  with `shared_buffers=2GB`, `effective_cache_size=3GB`, `work_mem=32MB`,
  `maintenance_work_mem=256MB` and two parallel workers. The remaining machine memory serves both
  API environments and the proxy. A **4 GB / 2 vCPU** machine needs a separately measured smaller
  database limit that leaves room for those other containers and the operating system; the present
  compose settings are not a whole-machine 4 GB budget.
- **Later**, when readers are many or the CPU graph stays high, move to a dedicated-CPU plan: that is a
  new machine and the same volume, which is exactly the move the layout above makes cheap.

Check current pricing at the provider; nothing here depends on a particular plan.

**Production and dev run on the same machine**, in the same compose project and the same Postgres, as
separate databases, roles and containers. Dev is behind a password and tells search engines to stay
away. The cost is honest: a publication to dev competes with production for disk and memory while it
runs, and restarting Postgres restarts both.

**TLS is automatic.** Caddy obtains a certificate for every hostname from Let's Encrypt on its first
start and renews it on its own; there is no certificate to buy or to copy. It needs the DNS records to
point at the machine and ports 80 and 443 to be open when it starts, and `ACME_EMAIL` in `.env` for the
expiry warnings Let's Encrypt sends if renewal ever fails. The certificates are kept on the volume, so a
new machine does not ask for them again.

## Once: the machine (DigitalOcean)

1. **The volume.** Create a Block Storage volume of 100 GB or more, in the region nearest the readers,
   ext4, "manually format and mount" — this step formats it.
2. **The droplet.** Ubuntu 24.04, Basic, 8 GB / 4 vCPU (see **Size**), in **the same region**, with your
   SSH key, and attach the volume to it. Droplet backups can stay off: they would not include the volume.
3. **Mount the volume at `/srv/essenthos`**, by its id so a second volume never takes its place:

        ls /dev/disk/by-id/ | grep DO_Volume          # scsi-0DO_Volume_<name>
        sudo mkfs.ext4 /dev/disk/by-id/scsi-0DO_Volume_<name>   # a new, empty volume only — never on one with data
        sudo mkdir -p /srv/essenthos
        echo '/dev/disk/by-id/scsi-0DO_Volume_<name> /srv/essenthos ext4 defaults,nofail,discard,noatime 0 2' | sudo tee -a /etc/fstab
        sudo mount -a && df -h /srv/essenthos

   `nofail` lets the machine boot without the volume; Docker then finds `/srv/essenthos` empty, so if
   the site comes up with no corpus after a reboot, check `df -h /srv/essenthos` first.
4. **Firewall.** A DigitalOcean cloud firewall on the droplet allowing inbound 22, 80 and 443 (TCP) and
   443 (UDP), and nothing else. Postgres and the page counter's dashboard are bound to the droplet's
   loopback and are reached through ssh.
5. **DNS.** A records (and AAAA, if the droplet has IPv6) for `essenthos.org`, `api`, `dev` and `devapi`
   pointing at the droplet. Caddy obtains the certificates on first start; it needs the names to
   resolve first.
6. **Docker**, from Docker's own apt repository (docs.docker.com/engine/install/ubuntu), and a user for
   deploying:

        adduser --disabled-password deploy && usermod -aG docker deploy
        mkdir -p /srv/essenthos/deploy && chown -R deploy /srv/essenthos
        # then put your public key in /home/deploy/.ssh/authorized_keys

7. **The .env.** Copy `env.example` to `/srv/essenthos/deploy/.env` and fill it in. Every password
   fresh: `openssl rand -base64 32 | tr -d '/+='`. The Postgres passwords are read once, when the
   database is first created; changing one later is `ALTER ROLE`, not an edit here.
8. **On this machine**, the addresses Forge publishes to:

        dotnet user-secrets set "Publish:Targets:dev:Ssh" "deploy@<droplet>" --project Essenthos.Forge
        dotnet user-secrets set "Publish:Targets:prod:Ssh" "deploy@<droplet>" --project Essenthos.Forge
        dotnet user-secrets set "Publish:Targets:dev:Password" "<POSTGRES_PASSWORD>" --project Essenthos.Forge
        dotnet user-secrets set "Publish:Targets:prod:Password" "<POSTGRES_PASSWORD>" --project Essenthos.Forge

9. **First deploy and first corpus:**

        ./scripts/deploy.ps1 -Server deploy@<droplet> -Environment prod -Commit <core sha> -WebCommit <web sha>
        forge release
        forge publish --to dev
        forge publish --to prod

   Until the first publication the API answers `/v1/health/ready` with 503: there is no corpus yet.
   The first publication to each environment also sends every picture, about 180 MB.

## Moving to a new machine

A bigger plan, a dedicated CPU, or a machine that died: the volume carries the server.

1. On the old machine, if it still runs: `cd /srv/essenthos/deploy && docker compose down`, then
   `sudo umount /srv/essenthos`, and detach the volume at the provider.
2. Create the new machine in the volume's region and attach the volume. Do **not** format it.
3. Mount it at `/srv/essenthos` exactly as in step 3 above, without the `mkfs` line.
4. Install Docker and the `deploy` user as in step 6, without `mkdir` and `chown`; the ids match on a
   fresh Ubuntu, and if they do not, `chown -R deploy /srv/essenthos` — except `postgres/`, which
   belongs to the container's own user and must be left alone.
5. Point the DNS records at the new address, update `Publish:Targets:*:Ssh` if the address changed,
   and run `scripts/deploy.ps1` for prod and for dev. The corpus, the accounts, the pictures and the
   certificates are already there.

## Optional: Hetzner Cloud instead of DigitalOcean

Hetzner Cloud offers the same shape — a VM, a Volume, a Firewall — usually for less; check current
pricing and pick the location nearest the readers (its EU locations keep the data in the EU). Only the
creation steps differ; `compose.yaml`, the Caddyfile, the `.env` and every command after step 5 are the
same:

- **Volume**: create it in the server's location; Hetzner can format it (ext4) and mount it itself,
  but mount it at `/srv/essenthos` by hand as in step 3 so the path is the one everything expects —
  its device is `/dev/disk/by-id/scsi-0HC_Volume_<id>`.
- **Server**: Ubuntu 24.04, a shared-vCPU plan with 8 GB (or 4 GB, see **Size**), your SSH key. Its
  own backups, like DigitalOcean's, do not include the volume.
- **Firewall**: a Hetzner Cloud Firewall with the same rules as step 4, applied to the server.
- **Snapshots**: Hetzner snapshots servers, not volumes (check whether that has changed). There the
  nightly dumps and their off-site copy are the whole backup, and after a disaster the corpus is
  re-published from the workstation.

## Optional: Cloudflare in front

Cloudflare's free plan can sit in front of the machine as DNS and a caching, filtering proxy. It is not
needed, and it changes three things that have to be done together:

- **Certificates**: set SSL/TLS to **Full (strict)**, and leave "Always Use HTTPS" off — Caddy already
  redirects, and Let's Encrypt's renewal must reach Caddy over plain HTTP. Create the records grey
  (DNS only) first, let Caddy obtain its certificates, then turn the proxy on.
- **Readers' addresses**: with the proxy on, every connection comes from Cloudflare. Before turning it
  on, Caddy must be told to trust Cloudflare's published address ranges and to take the reader's address
  from `CF-Connecting-IP` (Caddy's global `servers { trusted_proxies … client_ip_headers … }`) —
  otherwise the API's per-reader limits treat every reader as one, and the page counter sees one
  visitor. That change is not in the Caddyfile yet.
- **The privacy page** must then name Cloudflare, which sees every request.

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

## Pages for search engines and link previews

The reader is one script, and crawlers and link-preview bots do not reliably run it. So every page of
the site is written out by the API first — its title, description, canonical address and its
addresses in the other three languages, Open Graph and Twitter tags, and the page's essentials (the
verse, the chapter's text in the language's own Bible, a record's name, line and first verses) inside
`#root`, where the application's first render replaces them. Caddy routes it (`(pages)` in the
Caddyfile):

| Request | Goes to |
|---|---|
| `/v1/*` | the API, as before |
| `/robots.txt`, `/sitemap.xml`, `/sitemaps/*` | the API, as `/v1/robots.txt` and so on |
| `/assets/*`, and anything with a file extension | the web container, as before |
| every other `GET` — a page | the API, as `/v1/pages/<the address>` |

A page the API answers with a server error or a refusal, or does not answer within two seconds, is
served by the web container as the plain application, so the API being down or restarting costs the
preview and never the page. An address that names nothing is the API's `404`, carrying the
application's own not-found page.

The API reads the reader's `index.html` from the web container (`Site__Shell` in `compose.yaml`) and
every absolute link names `Site__Origin`; both are set there from `SITE_DOMAIN` and `DEV_DOMAIN`, so
`.env` needs nothing new. The page wording — each language's titles and descriptions — is the reader's
own `meta` namespace, read from `/meta/<language>.json` beside `index.html` where the web build
publishes it, and from the copy compiled into the API (`Essenthos.Api/Pages/Wording/`) where it does
not. A page is kept ten minutes once written; a reader redeploy is picked up within a minute.

When the reader gains a public page at a new address, add it to `PageSections` in
`Essenthos.Api/Pages/PageAddress.cs` — until then that address is answered `404` to a search engine,
although a reader who follows a link still gets the page.

To see what a crawler sees:

    curl -s https://essenthos.org/uk/read/mark/3/3 | grep -E '<title>|canonical|hreflang'
    curl -s https://essenthos.org/robots.txt

and give `https://essenthos.org/sitemap.xml` to Google Search Console and Bing Webmaster Tools once.
Dev asks for a password and says `noindex`, so nothing there is indexed or previewed.

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

## Page counts

Off until turned on. When on, production's pages are counted by a GoatCounter on this machine — which
page, the referring site, the country, the kind of browser, system and screen, and the language — with
no cookie, nothing kept in the reader's browser, and the reader's address never written anywhere. Why
this and not Attriax, and what was checked, is DOC-0211. Dev is never counted.

It takes four switches, because the reader's bundle, the proxy, the counter and the privacy page must
agree:

1. **The reader's image**: in the essenthos-web repository's GitHub settings, add the Actions variable
   `ANALYTICS_ENDPOINT` = `/count`. CI then builds the bundle with it, which starts sending one request
   per page and makes the privacy page say what is counted. Without it nothing is sent and the privacy
   page says nothing is counted.
2. **The counter's settings in `.env`**:

        COMPOSE_PROFILES=analytics          # offsite,analytics if the off-site copy is on too
        SITE_ANALYTICS=on
        STATS_PASSWORD=<a fresh password>

3. **Its database.** On a new server `initdb` creates it on the first start from `STATS_PASSWORD`. On a
   server whose Postgres already exists, once, from `/srv/essenthos/deploy`:

        docker compose exec -T db psql -U postgres -v ON_ERROR_STOP=1 \
          -v stats="$(grep '^STATS_PASSWORD=' .env | cut -d= -f2-)" <<'SQL'
        CREATE ROLE essenthos_stats LOGIN PASSWORD :'stats';
        CREATE DATABASE essenthos_stats OWNER essenthos_stats;
        REVOKE ALL ON DATABASE essenthos_stats FROM PUBLIC;
        SQL

   (psql fills in `:'stats'` in what it reads, never in a `-c` command, hence the here-document.)
   Then `docker compose up -d`.

4. **The counter's settings**, through an ssh tunnel — its dashboard is on the machine's loopback only:

        ssh -L 8081:127.0.0.1:8081 deploy@<droplet>
        # then open http://localhost:8081 on this machine

   The first visit asks for a site name (`essenthos.org`), an email and a password: that is the owner's
   sign-in to the dashboard, which never leaves the tunnel. Then, under **Settings**:
   - **Data retention**: `730` days. The privacy page says counts are kept two years; the default keeps
     them forever.
   - **Data collection**: tick **Language** if the readers' languages are wanted (the privacy page says
     they are counted); leave **Sessions** on, which is what tells one visit from another, in memory.

The nightly backup dumps `essenthos_stats` beside the accounts databases once it exists, and skips it
while it does not.

To turn counting off: `SITE_ANALYTICS=off` and `docker compose up -d proxy` stops it at once; removing
the Actions variable stops the next bundle sending anything and takes the section off the privacy page.

## Backups

The corpus needs none: it is an artefact, rebuilt from sources this machine keeps. `essenthos_app` —
accounts, and later what readers write — is the only thing on the server that cannot be rebuilt; the
page counts in `essenthos_stats` are backed up beside it where the counter is on.

- The `backup` service dumps them every night into `/srv/essenthos/backups`, on the volume, and keeps
  each dump **14 days** (`BACKUP_KEEP_DAYS`), not a day more — **except while that database's backups
  are failing**: a database whose latest run failed keeps every dump it has until a run succeeds again,
  so an outage (the database down, the key expired, the disk full) never ages out the last good dump.
  That is the only way a dump outlives 14 days, and it lasts only as long as the outage; the first
  successful run removes everything past the window.
- **Every dump is encrypted as it is written**, with gpg, to a public key in `/srv/essenthos/backup-key/`.
  No dump is ever on the disk in the clear, so neither a volume snapshot nor a stolen disk yields a
  reader's email or reading history. The private key is never on the server. **Without a key a run
  writes nothing and fails**, every database `FAILED` in its log; plain dumps are written only when
  `.env` sets `BACKUP_ALLOW_UNENCRYPTED=yes`, and the next run with a key encrypts and removes the plain
  dumps it replaces.
- **A failure is visible**: each database that succeeds gets `/srv/essenthos/backups/.last-success-<database>`,
  and the container's healthcheck (`backup.sh --check`) turns it **unhealthy** once any of them is older
  than 26 hours (`BACKUP_STALE_HOURS`) — `docker compose ps backup` shows it. `backup.sh --once` exits
  non-zero on a failed run, as does `backup-offsite.sh --once`.
- **Off the machine**: the `backup-offsite` service copies the encrypted dumps to an rclone remote every
  hour and removes a copy there once it is 14 days old and its dump is gone from here, so the off-site
  copy is kept exactly as long as the dump here — through an outage too. Only `*.dump.gpg` files are
  sent. It runs only when `.env` turns it on — and since the volume is the one place the dumps are
  written, it is what survives losing the volume.
- **To the owner's machine**: the console's Backups section (`Essenthos.Desk`) copies the newest
  encrypted dump of each database into `E:\Projects\Essenthos\server-backups` on a button, and on its
  own once a day while the console runs. It connects exactly as the deploy does — `ssh` with the
  owner's key to the `Publish:Targets:prod:Ssh` address — lists the folder, fetches each file with
  `cat` and checks it against the server's `sha256sum`. It never fetches a `*.dump`, so nothing in the
  clear ever leaves the server, and nothing is decrypted on the way. It keeps as many copies of each
  database as the owner sets, and none but the newest older than 14 days. The folders are
  `Desk:Backups:Folder` and `Desk:Backups:Remote` in its `appsettings.json`.
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

### Once: the off-site copy

Anything rclone can write to works: an S3-compatible bucket (DigitalOcean Spaces, Hetzner Object
Storage, Backblaze B2, Cloudflare R2) or a host reached over ssh. Make a bucket or a folder that holds
nothing else, and a key for it that can write, list and delete there and nowhere else. Then write the
remote into `/srv/essenthos/rclone/rclone.conf` on the server, `chmod 600` it, and name it in `.env`:

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
    COMPOSE_PROFILES=offsite                 # offsite,analytics with the page counter
    BACKUP_OFFSITE=offsite:<bucket or folder>/essenthos

    docker compose up -d backup-offsite && docker compose logs --tail 5 backup-offsite

Give the bucket a lifecycle rule deleting objects after 15 days as well, so a copy expires even if the
service stops — knowing that it also removes, during an outage, the off-site copy of a dump the server is
still holding. The privacy page promises that the backups are encrypted and that deleted data is gone
from every one of them within six weeks: a copy kept anywhere has to keep to that.

### Restoring

**A restore has to be run once on the server before the backup is believed.** Into a scratch database,
never over the live one. Only the private key can open a dump, so it is decrypted on your machine and
streamed back into `pg_restore`, and the dump is never on the server in the clear:

    scp deploy@<droplet>:/srv/essenthos/backups/essenthos_app-<stamp>.dump.gpg .   # or use the console's copy on E:
    gpg --import essenthos-backups-SECRET.asc        # once, on the machine that restores
    gpg --decrypt essenthos_app-<stamp>.dump.gpg | ssh deploy@<droplet> 'cd /srv/essenthos/deploy &&
      docker compose exec -T backup sh -c "createdb -h db restore_test && pg_restore -h db -d restore_test &&
        psql -h db -d restore_test -c \"\dt\" && dropdb -h db restore_test"'

A dump written through gpg was written to a pipe, so `pg_restore` reads it front to back: one table can
still be restored out of it with `-t`, but not in parallel with `-j`.

The unencrypted procedure was run on the rehearsal on 2026-09-18. The encrypted stream was also
rehearsed locally before release; neither rehearsal establishes recovery on a server that has not
been provisioned.

Run the current account-schema recovery check through the registered action:

    avioniq services run core-backup-rehearsal --set root=. --set output=backup-rehearsal

It uses synthetic accounts in isolated workstation databases, the pinned deployment Postgres client
image, and an ephemeral key pair. The private key is mounted only in the client that decrypts; the
backup process sees only the public key. The real backup script runs with `--once`: without a key it
fails and writes nothing, with `BACKUP_ALLOW_UNENCRYPTED=yes` it writes a plain dump, and with the key it
writes only encrypted files, prunes expired files, removes older plain backups and stamps
`.last-success-<database>`, which `--check` accepts and refuses once it is 27 hours old. An unreachable
database fails the run, keeps its three-week-old dump and stamps nothing, and the daemon reports the
failed run and carries on. Decryption streams directly
into a fresh database. Every restored table is compared with the original, and the check exercises
the restored data-protection key, existing cookie and bearer sessions, expired and revoked sessions,
the revision sequence and an unchanged migration rerun. A corrupt dump is refused in another isolated
candidate while the original account data stays intact. Temporary databases, files and private keys
are removed; the TRX result is kept under `TestResults/<output>`. The offsite script is also run once
against a local isolated rclone destination, checking encrypted-only copying, expired-copy removal, that
a copy of a dump still held here stays, and that an unconfigured remote exits non-zero.

This requires Docker and the registered workstation database to be available. It does not contact a
production host or external backup destination, publish a corpus or modify real accounts. Before the first
public release, repeat recovery against a real encrypted server backup in a scratch database and
verify the configured daily schedule and the owner's offsite download separately.
