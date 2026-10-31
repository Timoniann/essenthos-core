# The server

One Hetzner machine runs production in containers: Postgres, the API, the reader, Caddy in front, a
nightly backup, and optionally the page counter. No dev runs on it (TSK-1002): the rehearsal of a
publication is the same stack on the workstation (**Rehearsal**, below). Access, state and history of
the machine are in the guide `production-server`; the corpus's publication is DOC-0203; the page
counter is DOC-0211.

    http://2.29.125.205    the reader and its API at /v1, plain http, no sign-in   (SITE_IP)
    essenthos.org          the reader and its API at /v1, with TLS                  (SITE_DOMAIN)
    api.essenthos.org      the same API, for the mobile app and other clients      (API_DOMAIN)

Four things reach the server, separately, all from the workstation; the server pulls nothing of ours
and needs neither git nor a registry login:

- **the images**: `scripts/send-images.ps1` checks each commit, builds the API image from an
  essenthos-core commit and the reader's from an essenthos-web commit, and loads them into the server's
  Docker as `essenthos-api:<commit>` and `essenthos-web:<commit>`;
- **the code**: `scripts/deploy.ps1` copies this folder's files as that commit holds them and starts
  the images by those tags (**Shipping code**);
- **the corpus**: `forge publish`, which also sends the pictures its rows name into
  `/srv/essenthos/images/corpus` (**Shipping the corpus**);
- **the downloads**: `forge export`, then `forge publish` or `forge publish-downloads` (**The downloads**).

## The machine

Hetzner Cloud, Helsinki: x86_64, 2 vCPU, 3.7 GiB of memory with a 4 GB swap file, one 38 GB disk.
Everything the server keeps is under `HOST_DATA_ROOT=/srv/essenthos`, on that disk:

| Under /srv/essenthos | What |
|---|---|
| `deploy/` | this folder's files and `.env` |
| `postgres/` | the database: the one corpus copy, the accounts, the page counts |
| `releases/`, `images/corpus/`, `downloads/` | what `forge publish` sends |
| `backups/`, `backup-key/`, `rclone/` | the encrypted dumps, the public key, the off-site remote |
| `caddy/` | certificates |
| `maintenance/` | the updating page's switch (**The updating page**) |

Nothing on the disk is unique but the accounts, which the backups protect (**Backups**). The corpus is
an artefact, published again from the workstation; Hetzner's own server backups (+20%) are optional.

### Memory

The budget is chosen in `.env`, never by editing `compose.yaml`. Each value left out takes the 8 GB
machine's default; `env.example` holds the 4 GB machine's, which this server runs:

| `.env` | 4 GB (this server) | 8 GB (default) |
|---|---|---|
| `DB_MEM_LIMIT` | 2g | 4g |
| `DB_SHARED_BUFFERS` | 768MB | 2GB |
| `DB_EFFECTIVE_CACHE_SIZE` | 1536MB | 3GB |
| `DB_WORK_MEM` | 16MB | 32MB |
| `DB_MAINTENANCE_WORK_MEM` | 256MB | 256MB |
| `DB_PARALLEL_WORKERS` (per gather) | 1 | 2 |
| `DB_SHM_SIZE` | 512mb | 1gb |
| `API_MEM_LIMIT` | 768m | 1g |
| `WEB_MEM_LIMIT` | 128m | 128m |
| `ANALYTICS_MEM_LIMIT` | 128m | 256m |

The limits add up to 2.9 GiB (3.0 with the counter) of the machine's 3.7, which leaves about 850 MB
for Ubuntu, Docker, Caddy and the backup container, with the swap file as the safety net. Why the 8 GB figures are what
they are is in `compose.yaml` (TSK-0920, PRB-0926). On 4 GB, heavy requests (search, the parallel view,
entity pages) are slower; the owner accepts that for the alpha. Time them once on the server and record
the figures on TSK-1002; if they take seconds, rescale the machine and drop the `DB_*`/`*_MEM_LIMIT`
lines. Restoring a release builds one index per parallel `pg_restore` job, each with up to
`maintenance_work_mem`: four jobs at 256MB beside 768 MB of shared buffers come close to the 2g limit.

### Disk

The server keeps **one corpus copy** (owner, 2026-10-29): a publication drops the live corpus and
restores the new release in its place, with the updating page up meanwhile, and a rollback is publishing
the previous release again. A published copy is 12–15 GB in Postgres; with the images (about 0.4 GB
per API and 0.3 GB per reader kept), the pictures (~180 MB), the releases uploaded for restore, the
backups and the logs, it fits the 38 GB disk. Check `df -h /` before a publication. When it no longer
fits: **When more space is needed**, below.

### Firewall

Hetzner's Cloud Firewall, when the owner sets it up, allows 22, 80 and 443 (TCP) and 443 (UDP) inbound
and nothing else. ufw is no protection here: **Docker's published ports bypass ufw**, so whatever
compose publishes on `0.0.0.0` is open whatever ufw says. That is why Postgres (5432) and the counter's
dashboard (8081) are published on `127.0.0.1` only and reached through an ssh tunnel; keep any new port
the same.

## Addresses: the bare IP and the domain

`.env` names where the site answers, and Caddy serves only what it names:

| `.env` | Serves |
|---|---|
| `SITE_IP=2.29.125.205` | `http://2.29.125.205`: the reader, the API at `/v1`, the downloads. Plain http. |
| `SITE_DOMAIN`, `API_DOMAIN`, `ACME_EMAIL` | the reader on the domain and the API on its own hostname, with TLS |

Both can be on at once. **While there is no domain**, `.env` has `SITE_IP=2.29.125.205` and
`SITE_DOMAIN`, `API_DOMAIN` and `ACME_EMAIL` empty (the lines present, with nothing after `=`). The
reader calls the API on its own origin (`/v1`), so nothing in the images depends on the address.

On the bare address:

- **sign-in is not offered**: the proxy answers `/v1/auth/providers` with none, since Google and
  GitHub send a reader back only to a registered https address;
- **nothing is indexed**: every answer says `noindex` and `robots.txt` keeps crawlers out;
- with no domain set, each page's canonical and language links name the address it was asked on; once
  the domain is set, they name the domain on both.

**Switching the domain on**, once it is bought:

1. DNS: A records (and AAAA for `2a01:4f9:c015:b780::1`) for the domain and `api.` pointing at the
   server.
2. `.env`: `SITE_DOMAIN=essenthos.org`, `API_DOMAIN=api.essenthos.org`, `ACME_EMAIL=<address>`.
   Keep `SITE_IP` to go on answering on the address, or empty it.
3. `cd /srv/essenthos/deploy && docker compose up -d`: the proxy and the API are recreated with the
   new values, and Caddy obtains the certificates on its first request to each name (ports 80 and 443
   must be open and the names must resolve first). The certificates are kept in `caddy/`.
4. Sign-in, if wanted: the provider's callback is `https://<domain>/v1/auth/google/callback` (and
   `/github/callback`); then `GOOGLE_CLIENT_ID`/`_SECRET`, `GITHUB_CLIENT_ID`/`_SECRET` in `.env` and
   `docker compose up -d api`.
5. Search engines: **Search engines: once, by the owner**, below.

## The updating page

While the one corpus copy is replaced, every site answers every request with a short page in Ukrainian,
English, German and Spanish, status **503** with `Retry-After: 300`, so a reader sees "we are updating"
and a crawler comes back later. Only `/v1/health/*` still reaches the API, so a publisher can watch it
come back. The switch is a file; Caddy looks for it on every request, so it takes effect at once, with
no reload:

    ssh deploy@2.29.125.205 'mkdir -p /srv/essenthos/maintenance && touch /srv/essenthos/maintenance/on'   # on
    ssh deploy@2.29.125.205 'rm -f /srv/essenthos/maintenance/on'                                           # off

`forge publish` to a one-copy target turns it on and off itself (`Publish:Targets:<target>:Maintenance`,
default `maintenance/on` under the target's data root). The page is in the Caddyfile (`&(updating)`).
`deploy.ps1` makes the folder as the deploy user; a folder Docker makes for a mount is root's, and the
`touch` above would be refused.

## Shipping code

There is no CI: GitHub keeps the repositories and builds nothing (NOT-0213). The checks on the
workstation are the gate, and the images are built there from the exact commits that passed:

    $core = git -C essenthos-core rev-parse main
    $web  = git -C essenthos-web  rev-parse main

    # check, build and send: the checks on a temporary worktree at each commit, docker build from the
    # commit as committed, docker save | ssh docker load
    ./scripts/send-images.ps1 -Server deploy@2.29.125.205 -Commit $core -WebCommit $web

    # start them: deploy/ from the core commit, the two tags into .env, docker compose up -d
    ./scripts/deploy.ps1 -Server deploy@2.29.125.205 -Environment prod -Commit $core -WebCommit $web

Each image is optional (`-Commit` alone or `-WebCommit` alone). `-SkipChecks` sends without checking;
a tag the server already has is refused unless `-Force`; both scripts take `-WhatIf`. `deploy.ps1`
refuses before touching anything when `.env` is missing or a tag was not sent. Images are built for
`linux/amd64`. Measured 2026-10-29: about ten minutes for both, 110 MB sent per image at ~5 MB/s.

**Rollback** is `deploy.ps1` with the previous hashes: sent images stay until removed by hand
(`docker image rm essenthos-api:<old commit>`, never the two named in `.env`). Keep the last few and
prune when `df -h /` asks for it.

## Shipping the corpus

A corpus reaches production only after the **rehearsal** on the workstation has taken the same release
(owner, 2026-10-29): Forge's `prod` target names `rehearsal` as the one a release must reach first
(`Publish:Targets:prod:After`).

    forge release                    # verify this machine's corpus, label it, dump it into .releases/
    forge publish --to rehearsal     # the rehearsal stack below; look at it
    forge publish --to prod          # one copy: the updating page is up while the copy is replaced

Every Forge command takes `--dry-run`. The first publication sends every picture (~180 MB); later ones
only what changed. Until the first publication the API answers `/v1/health/ready` with 503. A rollback
is `forge publish --to prod --release <earlier>` (`forge releases` lists them).

## Rehearsal

The same `compose.yaml` on the workstation, with `compose.rehearsal.yaml` for what a Windows Docker
needs (Postgres in a named volume). Its `.env.rehearsal` beside them is `env.example` with
`COMPOSE_PROJECT_NAME=essenthos-rehearsal` (the container names Forge's `rehearsal` target uses),
`HOST_DATA_ROOT` at the checkout's `.rehearsal` folder (the target's `DataRoot`), ports of its own
(`PROXY_HTTP_PORT`, `PROXY_HTTPS_PORT`, `POSTGRES_HOST_PORT`, clear of 5278–5289 and 5437–5439),
`SITE_IP=localhost`, the images to rehearse, and passwords made for it:

    docker compose --env-file .env.rehearsal -f compose.yaml -f compose.rehearsal.yaml up -d

`SITE_IP=localhost` serves the address mode at `http://localhost:<PROXY_HTTP_PORT>`; a
`SITE_DOMAIN` ending in `.localhost` serves the domain mode with Caddy's own local certificates. A
deploy, a publication, the updating page and a rollback are run here before anything changes on the
server.

Docker Desktop does not show what an IPv6 reader's connection looks like: its machine has no listener on
`[::]` for a published port. To see that, run the same compose file in a `docker:<server's version>-dind`
container with a client network given a public-looking IPv6 subnet (**Readers on IPv6**).

## Once: the machine

Done on 2026-10-29 as root, on the owner's word (guide `production-server`): Ubuntu 26.04, Docker and
Compose from Docker's apt repository, the 4 GB swap file, fail2ban, Docker's log rotation, the user
`deploy` in the `docker` group with the owner's key, and `/srv/essenthos` owned by `deploy`. Left:

1. **`.env`**: copy `env.example` to `/srv/essenthos/deploy/.env` and fill it in. Every password
   fresh (`openssl rand -base64 32 | tr -d '/+='`). The Postgres passwords are read once, when the
   database is first created; changing one later is `ALTER ROLE`.
2. **On the workstation**, where Forge publishes:

        dotnet user-secrets set "Publish:Targets:prod:Ssh" "deploy@2.29.125.205" --project Essenthos.Forge
        dotnet user-secrets set "Publish:Targets:prod:Password" "<POSTGRES_PASSWORD>" --project Essenthos.Forge

3. **First deploy and first corpus**: `send-images.ps1`, `deploy.ps1`, then **Shipping the corpus**.
4. **The backup key** (**Backups**, below), and the firewall when the owner wants it.

## Every time

| To | Run |
|---|---|
| check, build and send the images | `scripts/send-images.ps1 -Server deploy@2.29.125.205 -Commit <sha> [-WebCommit <sha>]` |
| ship code | `scripts/deploy.ps1 -Server deploy@2.29.125.205 -Environment prod -Commit <sha> [-WebCommit <sha>]` |
| undo a code deploy | `scripts/deploy.ps1` with the previous commits |
| ship a corpus | `forge release`, `forge publish --to rehearsal`, a look, `forge publish --to prod` |
| undo a corpus | `forge publish --to prod --release <earlier>` |
| ship the downloads alone | `forge export`, `forge publish-downloads --to rehearsal`, then `--to prod` |
| see what is where | `forge releases`, `forge releases --on prod` |
| look at the server | `ssh deploy@2.29.125.205 'df -h /; free -h; cd /srv/essenthos/deploy && docker compose ps'` |

The owner's console (`Essenthos.Desk`) has a Deployment section that runs the same commands behind a
confirmation and records each run in `owner-changes.jsonl`; it reads what runs from
`/v1/health/version` and `/version.json`, which the images carry from `SOURCE_COMMIT`. It does not send
images: run `send-images.ps1` first.

## The downloads

The texts that may be passed on, one file per text with its attribution beside it, made on the
workstation by `forge export` (into `.exports`, with a `manifest.json` naming each file's size and
checksum) and sent by `forge publish` with the corpus or by `forge publish-downloads` alone. They land in
`/srv/essenthos/downloads` and are served at `/downloads/…` on every address of the site; the API lists
them at `/v1/downloads`.

The send checks the export against its manifest and sends nothing if a file is missing or differs; sends
only files the server lacks (a name carries the first digits of its checksum, so a changed file is a new
name); swaps the new manifest in, in one move, once every file it names is there; and only then removes
files no manifest names. So the list the site shows never names a missing file. A text's file is served
under its plain name and may be cached for a year; the manifest for five minutes; nothing lists a folder.

## Pages for search engines and link previews

Crawlers do not reliably run the reader's script, so the API writes every page first: title,
description, canonical and language links, Open Graph tags, structured data and the page's essentials
inside `#root`. Caddy routes it (`(pages)` in the Caddyfile):

| Request | Goes to |
|---|---|
| `/v1/*` | the API |
| `/robots.txt`, `/sitemap.xml`, `/sitemaps/*` | the API, as `/v1/robots.txt` and so on |
| `/assets/*`, and anything with a file extension | the web container |
| every other `GET` (a page) | the API, as `/v1/pages/<the address>` |

A page the API answers with a server error or a refusal, or not within two seconds, is served by the
web container as the plain application, so the API restarting costs the preview and never the page.
A new public page of the reader needs its address in `PageSections` (`Essenthos.Api/Pages/PageAddress.cs`).

### Search engines: once, by the owner

On the domain only; the bare address asks not to be indexed.

1. **Google Search Console**: a *Domain* property for the domain, verified by the DNS TXT record it
   shows; then submit `https://<domain>/sitemap.xml`.
2. **Bing Webmaster Tools**: *Import from Google Search Console*.
3. **IndexNow** (Bing, Yandex and others): `openssl rand -hex 16` into `.env` as `INDEXNOW_KEY`,
   `docker compose up -d proxy`, check `https://<domain>/<key>.txt`; after a publication,
   `./scripts/indexnow.ps1 -Key <key> -All` (or `-Sitemap <list>` for what changed).

## Readers on IPv6

Docker forwards a published port to a container's IPv6 address and keeps the client's address, but a
container with none gets the connection through `docker-proxy`, from the bridge's address, and every IPv6
reader looks like one. So the proxy alone also sits on the network `edge`, which is IPv6 and nothing else.
IPv4 readers and the proxy's way to the API are unchanged: the API believes only `10.89.0.2`, and its name
resolves only on `default`.

`dockerd` turns `net.ipv6.conf.all.forwarding` on when `edge` is created. On a host that takes its IPv6
default route from router advertisements that would lose the route; this server has `accept_ra=0` and a
static address. Check it before relying on this anywhere else.

From outside, on a network with IPv6 (a phone without Wi-Fi, a VPS): open a page, then look at the last
line of the access log, whose `remote_ip` must be that network's /48 and not `10.89.…`. Every IPv6 reader
then also has a rate-limit bucket per /64 and a country.

To undo it: deploy the previous commit, whose compose file has no `edge`, or remove `edge: {}` from the
proxy's networks in `/srv/essenthos/deploy/compose.yaml`, then `docker compose up -d proxy`, then
`docker network rm essenthos_edge`. Removing the AAAA records is the other way out: IPv6-only readers
without NAT64 lose the site, everyone else falls back to IPv4 at once.

## The database's extensions

The corpus uses `pg_trgm`. `initdb/00-extensions.sh` creates it in `template1` when the data directory
is first created, so every database restored later has it. On a Postgres created before that script:

    docker compose exec db psql -U postgres -d template1 -c 'CREATE EXTENSION IF NOT EXISTS pg_trgm;'

## Page counts

Off until turned on. When on, a GoatCounter on this machine counts production's pages (page, referring
site, country, browser, system, screen class, language) with no cookie and without writing the reader's
address anywhere (DOC-0211). Four switches, which must agree:

1. the reader's image built with `send-images.ps1 … -AnalyticsEndpoint /count` (`-Force` to resend a
   tag), then deployed;
2. `.env`: `COMPOSE_PROFILES=analytics` (`offsite,analytics` with the off-site copy), `SITE_ANALYTICS=on`,
   `STATS_PASSWORD=<fresh>`;
3. its database: created by initdb on a new Postgres; on an existing one, once, from
   `/srv/essenthos/deploy`:

        docker compose exec -T db psql -U postgres -v ON_ERROR_STOP=1 \
          -v stats="$(grep '^STATS_PASSWORD=' .env | cut -d= -f2-)" <<'SQL'
        CREATE ROLE essenthos_stats LOGIN PASSWORD :'stats';
        CREATE DATABASE essenthos_stats OWNER essenthos_stats;
        REVOKE ALL ON DATABASE essenthos_stats FROM PUBLIC;
        SQL

   then `docker compose up -d`;
4. its settings, through a tunnel (`ssh -L 8081:127.0.0.1:8081 deploy@2.29.125.205`, then
   `http://localhost:8081`): the first visit makes the owner's sign-in; then **Data retention** 730
   days, as the privacy page says, and **Language** ticked.

Off again: `SITE_ANALYTICS=off` and `docker compose up -d proxy`.

To read them without the tunnel: an A record for the hostname, then `STATS_DOMAIN=stats.essenthos.org` in
`.env` and `docker compose up -d proxy`. It asks for the counter's own sign-in.

## Backups

Only `essenthos_app` (accounts, and later what readers write) cannot be rebuilt; the page counts in
`essenthos_stats` are backed up beside it where the counter is on.

- The `backup` service dumps them every night into `/srv/essenthos/backups` and keeps each dump 14 days
  (`BACKUP_KEEP_DAYS`), except while a database's backups fail: then it keeps every dump it has until a
  run succeeds, so an outage never ages out the last good one.
- **Every dump is encrypted as it is written** to the public key in `/srv/essenthos/backup-key/`; the
  private key is never on the server. **Without a key a run writes nothing and fails**, unless `.env`
  says `BACKUP_ALLOW_UNENCRYPTED=yes`.
- A failure is visible: the container turns **unhealthy** once a database's last good dump is older
  than 26 hours (`BACKUP_STALE_HOURS`); `docker compose ps backup` shows it.
- **Off the machine**: the owner's console copies the newest encrypted dump of each database to
  `E:\Projects\Essenthos\server-backups` on a button and once a day while it runs (over ssh to
  `Publish:Targets:prod:Ssh`, checked against the server's `sha256sum`). The `backup-offsite` service can
  copy them to an rclone remote as well.
- A changed `backup.sh` or `backup-offsite.sh` reaches its container only on
  `docker compose up -d --force-recreate backup backup-offsite`.

### Once: the key

On the owner's machine, never on the server:

    gpg --quick-generate-key "Essenthos backups" rsa4096 encr never
    gpg --armor --export "Essenthos backups" > essenthos-backups.asc
    gpg --armor --export-secret-keys "Essenthos backups" > essenthos-backups-SECRET.asc

Keep the secret half and its passphrase on E: and in one more place that is not this PC: without it no
backup can be read by anyone. Then send the **public** half:

    ssh deploy@2.29.125.205 'mkdir -p /srv/essenthos/backup-key'
    scp essenthos-backups.asc deploy@2.29.125.205:/srv/essenthos/backup-key/
    ssh deploy@2.29.125.205 'cd /srv/essenthos/deploy && docker compose up -d --force-recreate backup &&
      docker compose logs --tail 5 backup'

The log says `encrypted` beside each dump. Only one `.asc` belongs in that folder.

### Once: the off-site copy (optional)

Any rclone remote that holds nothing else, with a key that can write, list and delete there only
(Hetzner Object Storage, Backblaze B2, Cloudflare R2, or a host over sftp). Write it into
`/srv/essenthos/rclone/rclone.conf` as `[offsite]`, `chmod 600` it, then in `.env`
`COMPOSE_PROFILES=offsite` and `BACKUP_OFFSITE=offsite:<bucket or folder>/essenthos`, and
`docker compose up -d backup-offsite`. Copies there are removed once 14 days old and gone here; give the
bucket a 15-day lifecycle rule as well. The privacy page promises deleted data is gone from every backup
within six weeks: a copy kept anywhere keeps to that.

### Restoring

Run once before the backup is believed, into a scratch database, never over the live one. The dump is
decrypted on the owner's machine and streamed in, never in the clear on the server:

    scp deploy@2.29.125.205:/srv/essenthos/backups/essenthos_app-<stamp>.dump.gpg .   # or the console's copy on E:
    gpg --decrypt essenthos_app-<stamp>.dump.gpg | ssh deploy@2.29.125.205 'cd /srv/essenthos/deploy &&
      docker compose exec -T backup sh -c "createdb -h db restore_test && pg_restore -h db -d restore_test &&
        psql -h db -d restore_test -c \"\dt\" && dropdb -h db restore_test"'

The recovery path is rehearsed on the workstation with synthetic accounts by
`avioniq services run core-backup-rehearsal --set root=. --set output=backup-rehearsal`; that does not
replace one restore from the real server.

## When more space is needed

When one copy plus everything else no longer fits the 38 GB disk, or two copies are wanted again
(`Publish:Targets:prod:Copies`): attach a Hetzner Volume (network storage, slower than the local disk,
priced per GB), formatted ext4, and move the data onto it.

    ls /dev/disk/by-id/ | grep HC_Volume                       # scsi-0HC_Volume_<id>
    sudo mkfs.ext4 /dev/disk/by-id/scsi-0HC_Volume_<id>        # a new, empty volume only
    sudo mkdir -p /mnt/essenthos && echo '/dev/disk/by-id/scsi-0HC_Volume_<id> /mnt/essenthos ext4 defaults,nofail,discard,noatime 0 2' | sudo tee -a /etc/fstab
    sudo mount -a
    cd /srv/essenthos/deploy && docker compose down
    sudo rsync -aHAX /srv/essenthos/ /mnt/essenthos/           # postgres/ keeps its owner
    # then mount the volume at /srv/essenthos instead (edit fstab), docker compose up -d

A volume can be grown later (resize at Hetzner, then `resize2fs`), never shrunk. Hetzner's server
backups do not include a volume. Rescaling the machine instead ("CPU and RAM only" keeps the disk) is
a few minutes in the console; afterwards drop the 4 GB lines from `.env`.

## Optional: Cloudflare in front

Not needed. If ever: SSL/TLS **Full (strict)** with "Always Use HTTPS" off; the records grey until Caddy
has its certificates; Caddy told to trust Cloudflare's ranges and read `CF-Connecting-IP` (global
`servers { trusted_proxies … client_ip_headers … }`, not in the Caddyfile yet), or every reader is one
address to the API's limits and the counter; and the privacy page names Cloudflare.
