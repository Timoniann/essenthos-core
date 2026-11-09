#!/bin/sh
# Tells the owner in Telegram when production needs him (TSK-1282), and once a day that it does not.
#
# It runs on the host as deploy, from deploy's crontab, every five minutes:
#   */5 * * * * /bin/sh /srv/essenthos/deploy/watch.sh >> /srv/essenthos/logs/watch.log 2>&1
# The host rather than a container, because it needs docker (which containers run, the API's log),
# the disk of the volume and of the system, and curl; none of our images has all of them.
#
# What it watches:
#   site       the home page and /v1/health/ready, through the proxy on this machine
#   container  each of the services that should run is up and not unhealthy
#   disk       / and /srv/essenthos under 85%
#   backup     a dump of the readers' accounts written in the last 26 hours
#   api-5xx    ten or more server errors from the API in ten minutes
#   browser    readers' browsers reporting crashes (3+ visits) or failed API calls (30+) in an hour
#   tls        the certificate valid for 14 days more (Caddy renews it at 30)
# A problem is told once it is seen on two runs in a row, again every six hours while it lasts,
# and once more when it is gone. While /srv/essenthos/maintenance/on exists the site checks rest.
#
# Telegram: TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID in the .env beside this file. Neither is ever
# printed: the token goes to curl on its standard input, never on its command line.
#   watch.sh --hello   finds the group the bot was added to, writes its TELEGRAM_CHAT_ID, says hello
#   watch.sh --dry     checks and prints what it found, sends nothing, keeps no state

set -u
DIR=$(cd "$(dirname "$0")" && pwd)
ENV_FILE="$DIR/.env"
DATA=/srv/essenthos
STATE="$DATA/watch"
SITE=https://essenthos.org
MODE=${1:-}

env_value() { sed -n "s/^$1=//p" "$ENV_FILE" 2>/dev/null | tail -n 1 | sed 's/^"\(.*\)"$/\1/'; }
TOKEN=$(env_value TELEGRAM_BOT_TOKEN)
CHAT=$(env_value TELEGRAM_CHAT_ID)

telegram() { # method, then curl data arguments
  method=$1
  shift
  printf 'url = "https://api.telegram.org/bot%s/%s"\n' "$TOKEN" "$method" | curl -sS -m 20 -K - "$@"
}

say() {
  if [ "$MODE" = "--dry" ]; then
    printf 'would send: %s\n' "$1"
    return
  fi
  if [ -z "$TOKEN" ] || [ -z "$CHAT" ]; then
    printf '%s no Telegram set, not sent: %s\n' "$(date -u +%FT%TZ)" "$1"
    return
  fi
  telegram sendMessage -o /dev/null --data-urlencode "chat_id=$CHAT" --data-urlencode "text=$1" \
    || printf '%s Telegram did not take: %s\n' "$(date -u +%FT%TZ)" "$1"
}

umami() { # one number from the counter's database; empty where the counter is off
  printf '%s\n' "set default_transaction_read_only = on; $1" \
    | docker exec -i essenthos-db-1 sh -c 'psql -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d essenthos_umami' 2>/dev/null \
    | tail -n 1
}

if [ "$MODE" = "--hello" ]; then
  [ -n "$TOKEN" ] || { echo "Put TELEGRAM_BOT_TOKEN=<token> into $ENV_FILE first."; exit 1; }
  if [ -z "$CHAT" ]; then
    CHAT=$(telegram getUpdates | grep -o '"chat":{"id":-[0-9]*' | tail -n 1 | sed 's/.*://')
    if [ -z "$CHAT" ]; then
      echo "No group seen yet. Add the bot to the group, write /start in it, and run this again."
      exit 1
    fi
    printf '\nTELEGRAM_CHAT_ID=%s\n' "$CHAT" >> "$ENV_FILE"
    echo "Found the group and wrote TELEGRAM_CHAT_ID into .env."
  fi
  MODE=
  say "Essenthos: сторож на зв'язку. Писатиму сюди, коли сайт, бекап, диск чи помилки потребуватимуть уваги, і раз на день о 9:00, що все гаразд."
  exit 0
fi

problems=""
problem() { problems="$problems$1|$2
"; }

# The site, unless it is closed for an update on purpose.
if [ ! -e "$DATA/maintenance/on" ]; then
  for path in / /v1/health/ready; do
    code=$(curl -s -o /dev/null -m 15 -w '%{http_code}' --resolve essenthos.org:443:127.0.0.1 "$SITE$path")
    [ "$code" = "200" ] || problem "site$(echo "$path" | tr '/' '-')" "Сайт не відповідає: $path дає $code."
  done
fi

# The services that should run, by what docker says of them.
running=$(docker ps -a --filter name=essenthos- --format '{{.Names}} {{.Status}}')
for service in api web proxy db backup housekeeping umami; do
  line=$(printf '%s\n' "$running" | grep "^essenthos-$service-1 " || true)
  case "$line" in
    "") problem "container-$service" "Контейнера $service немає." ;;
    *"(unhealthy)"*) problem "container-$service" "Контейнер $service нездоровий: ${line#* }." ;;
    *" Up "* | *" Up"*) ;;
    *) problem "container-$service" "Контейнер $service не працює: ${line#* }." ;;
  esac
done

# Disks.
full=$(df -P / "$DATA" | awk 'NR > 1 { sub("%", "", $5); if ($5 + 0 >= 85) { key = $6; gsub("/", "-", key); print "disk" key "|Диск " $6 " заповнений на " $5 "%." } }')
[ -z "$full" ] || problems="$problems$full
"

# The nightly dump of readers' accounts.
if [ -z "$(find "$DATA/backups" -name 'essenthos_app-*.dump.gpg' -mmin -1560 2>/dev/null | head -n 1)" ]; then
  newest=$(ls -t "$DATA/backups" 2>/dev/null | grep '^essenthos_app-' | head -n 1)
  problem backup "Нічний бекап акаунтів не з'явився за 26 годин. Останній: ${newest:-немає}."
fi

# Server errors from the API.
errors=$(docker logs --since 10m essenthos-api-1 2>&1 | awk '/RequestTiming\[/ && $5 ~ /^5/ { n++ } END { print n + 0 }')
[ "$errors" -lt 10 ] || problem api-5xx "API дав $errors помилок 5xx за 10 хвилин."

# What readers' browsers reported.
crashes=$(umami "select count(distinct session_id) from website_event where event_name = 'crash' and created_at > now() - interval '1 hour';")
failed=$(umami "select count(*) from website_event where event_name = 'api-failed' and created_at > now() - interval '1 hour';")
[ "${crashes:-0}" -lt 3 ] || problem browser-crash "У $crashes відвідувачів сторінка впала за останню годину."
[ "${failed:-0}" -lt 30 ] || problem browser-api "Браузери читачів повідомили про $failed невдалих запитів до API за годину."

# The certificate.
until=$(echo | openssl s_client -connect 127.0.0.1:443 -servername essenthos.org 2>/dev/null | openssl x509 -noout -enddate 2>/dev/null | sed 's/notAfter=//')
if [ -n "$until" ]; then
  left=$(( ($(date -d "$until" +%s) - $(date +%s)) / 86400 ))
  [ "$left" -ge 14 ] || problem tls "Сертифікат essenthos.org спливає через $left дн."
fi

if [ "$MODE" = "--dry" ]; then
  printf '%s' "${problems:-nothing wrong
}"
  echo "errors=$errors crashes=${crashes:-} failed=${failed:-} tls_until=${until:-}"
  exit 0
fi

# Tell what is new, what lasts, and what is gone.
mkdir -p "$STATE"
now=$(date +%s)
printf '%s' "$problems" | while IFS='|' read -r key message; do
  [ -n "$key" ] || continue
  file="$STATE/$key"
  count=1
  sent=0
  [ -f "$file" ] && { count=$(( $(sed -n 1p "$file") + 1 )); sent=$(sed -n 2p "$file"); }
  if [ "$count" -ge 2 ] && { [ "$sent" -eq 0 ] || [ $((now - sent)) -ge 21600 ]; }; then
    if [ "$sent" -eq 0 ]; then say "⚠️ $message"; else say "⚠️ Досі: $message"; fi
    sent=$now
  fi
  printf '%s\n%s\n%s\n' "$count" "$sent" "$message" > "$file"
done
for file in "$STATE"/*; do
  [ -f "$file" ] || continue
  key=$(basename "$file")
  if ! printf '%s' "$problems" | grep -q "^$key|"; then
    [ "$(sed -n 2p "$file")" -eq 0 ] || say "✅ Минуло: $(sed -n 3p "$file")"
    rm -f "$file"
  fi
done

# Once a day, at 7:00 UTC (9:00 in Kyiv), that all is well, with yesterday in two numbers.
today=$(date -u +%F)
if [ "$(date -u +%H)" = "07" ] && [ "$(cat "$STATE/.daily" 2>/dev/null)" != "$today" ]; then
  echo "$today" > "$STATE/.daily"
  if [ -z "$problems" ]; then
    visits=$(umami "select count(distinct session_id) from website_event where event_type = 1 and created_at >= current_date - 1 and created_at < current_date;")
    disk=$(df -P "$DATA" | awk 'NR == 2 { print $5 }')
    say "Essenthos працює. Учора відвідувачів: ${visits:-?}. Диск даних: $disk. Бекап свіжий."
  fi
fi

# The log stays small.
log="$DATA/logs/watch.log"
if [ -f "$log" ] && [ "$(wc -c < "$log")" -gt 1000000 ]; then
  tail -n 2000 "$log" > "$log.tmp" && mv "$log.tmp" "$log"
fi
