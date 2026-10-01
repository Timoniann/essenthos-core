"""
What the corpus database feels like on a server that gives Postgres little memory (TSK-0920).

Runs against a private copy of essenthos_core in a container of its own, never the live one: a
restored dump in the volume essenthos-pgmeasure-data, served on 127.0.0.1:5439 with trust
authentication (loopback only, a throwaway copy, so there is no password to keep). Each setting
recreates that container with shared_buffers=N GB under a Docker memory limit of N+2 GB, so the
page cache cannot hide a miss, and evicts the volume's files from the page cache before every cold
phase, which is what a machine that has just started feels.

    python scripts/pg-measure.py run 3            # one setting, every phase, JSON into --out
    python scripts/pg-measure.py up 3             # only (re)create the container, cold
    python scripts/pg-measure.py down             # remove the container (the volume stays)

Phases, each cold (right after the restart) and warm (run again):
  verify  `forge verify`, the core-verify action's work
  api     a private Essenthos.Api on 5292: the reader's heavy endpoints, first hit and repeats
  write   `forge interlinear-join AVD1865 --replace`, a recipe step safe to repeat

The Forge runs with its own Resources (junctions to the main checkout's sources and a copy of
Resources/Essenthos, MST-0203), passed as --resources.

The volume is filled once beforehand: `pg_dump -Fc -Z lz4` inside the live container, the file
copied into this one, `pg_restore -j 6 --no-owner --no-privileges`, then `VACUUM ANALYZE` as a
publication does. `--unlimited` drops the memory limit, which is the control. The other knobs are
read from the environment: PGMEASURE_WORK_MEM, PGMEASURE_MAINTENANCE_WORK_MEM, PGMEASURE_PARALLEL
(workers per gather), PGMEASURE_CPUS and PGMEASURE_MEMORY_MB.
"""

import argparse
import json
import os
import statistics
import subprocess
import sys
import time
import urllib.error
import urllib.request

CONTAINER = "essenthos-pgmeasure"
VOLUME = "essenthos-pgmeasure-data"
IMAGE = "postgres:18.1@sha256:5773fe724c49c42a7a9ca70202e11e1dff21fb7235b335a73f39297d200b73a2"
PORT = 5439
API_PORT = 5292
CPUS = os.environ.get("PGMEASURE_CPUS", "4")
HEADROOM_GB = 2
DEVICE = "8:48"

WORKTREE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FORGE = os.path.join(WORKTREE, "Essenthos.Forge", "bin", "s45", "Release", "net10.0", "Essenthos.Forge.exe")
API_DIR = os.path.join(WORKTREE, "Essenthos.Api")
API = os.path.join(API_DIR, "bin", "s45", "Release", "net10.0", "Essenthos.Api.exe")

CONNECTION = f"Host=127.0.0.1;Port={PORT};Database=essenthos_core;Username=essenthos"
ACCOUNTS = f"Host=127.0.0.1;Port={PORT};Database=essenthos_accounts;Username=essenthos"
# Trust authentication: the code insists on a password being present, not on its value.
UNCHECKED_PASSWORD = "trust"

ENDPOINTS = [
    "/v1/parallel/psalms/119?corpora=BHSA,KJV,RUSV,UBIO",
    "/v1/parallel/genesis/1?corpora=BHSA,LXX,KJV,RUSV",
    "/v1/parallel/matthew/5?corpora=NESTLE1904,TR1894,KJV,RUSV",
    "/v1/strong/H430/renderings?corpus=KJV",
    "/v1/strong/H3068/renderings?corpus=RUSV",
    "/v1/strong/G2316/renderings?corpus=KJV",
    "/v1/entities/moses",
    "/v1/entities/david",
    "/v1/entities/jerusalem",
    "/v1/entities/david/references?skip=0&take=50",
    "/v1/context/genesis/10",
    "/v1/search?q=love&corpus=KJV",
    "/v1/search?q=%D0%BB%D1%8E%D0%B1%D0%BE%D0%B2&corpus=RUSV&match=substring",
    "/v1/search?q=shepherd&corpus=KJV&testament=old",
    "/v1/cross-references/psalms/119",
]


def log(message):
    print(f"{time.strftime('%H:%M:%S')} {message}", flush=True)


def docker(*args, check=True, capture=True):
    result = subprocess.run(["docker", *args], capture_output=capture, text=True, encoding="utf-8")
    if check and result.returncode != 0:
        raise RuntimeError(f"docker {' '.join(args[:2])} failed: {result.stderr.strip()}")
    return result.stdout if capture else ""


def psql(sql, database="essenthos_core"):
    return docker("exec", "-i", CONTAINER, "psql", "-U", "essenthos", "-d", database, "-AtqX", "-c", sql).strip()


def settings(shared_gb, unlimited=False):
    total_mb = None if unlimited else (shared_gb + HEADROOM_GB) * 1024
    if os.environ.get("PGMEASURE_MEMORY_MB"):
        total_mb = int(os.environ["PGMEASURE_MEMORY_MB"])
    # What a machine of that size would really have in cache: the buffers and the page cache,
    # less what the backends themselves take. Unlimited is the workstation, as core-db runs on it.
    cache_mb = 8 * 1024 if unlimited else shared_gb * 1024 + (total_mb - shared_gb * 1024) * 3 // 4
    return total_mb, [
        f"shared_buffers={shared_gb}GB",
        f"effective_cache_size={cache_mb}MB",
        f"work_mem={os.environ.get('PGMEASURE_WORK_MEM', '64MB')}",
        f"maintenance_work_mem={os.environ.get('PGMEASURE_MAINTENANCE_WORK_MEM', '1GB')}",
        "max_wal_size=8GB",
        "random_page_cost=1.1",
        f"max_parallel_workers_per_gather={os.environ.get('PGMEASURE_PARALLEL', '4')}",
        "shared_preload_libraries=pg_stat_statements",
        "track_io_timing=on",
        "log_temp_files=0",
    ]


def down():
    # Stopped with a shutdown checkpoint first: a killed server replays its WAL on the next start,
    # gigabytes of it after a write step, and that replay would be measured as the cold phase.
    docker("stop", "-t", "300", CONTAINER, check=False)
    docker("rm", "-f", CONTAINER, check=False)


def evict():
    """Drop the copy's files from the page cache, so a cold phase reads from the disk."""
    docker("run", "--rm", "-v", f"{VOLUME}:/d", IMAGE, "sh", "-c",
           "find /d -type f -print0 | xargs -0 -n 64 -P 4 sh -c "
           "'for f; do dd if=\"$f\" iflag=nocache count=0 status=none; done' _")


def up(shared_gb, unlimited=False):
    total_mb, options = settings(shared_gb, unlimited)
    down()
    evict()
    limit = [] if total_mb is None else ["--memory", f"{total_mb}m", "--memory-swap", f"{total_mb}m"]
    command = []
    for option in options:
        command += ["-c", option]
    docker("run", "-d", "--name", CONTAINER,
           "-e", "POSTGRES_USER=essenthos", "-e", "POSTGRES_DB=essenthos_core",
           "-e", "POSTGRES_HOST_AUTH_METHOD=trust",
           "-p", f"127.0.0.1:{PORT}:5432", "-v", f"{VOLUME}:/var/lib/postgresql",
           "--shm-size", "4g", *limit, "--cpus", CPUS, IMAGE, "postgres", *command)
    for _ in range(120):
        if subprocess.run(["docker", "exec", CONTAINER, "pg_isready", "-U", "essenthos", "-d", "essenthos_core"],
                          capture_output=True).returncode == 0:
            break
        time.sleep(1)
    else:
        raise RuntimeError("the measuring container did not become ready")
    time.sleep(2)
    psql("CREATE EXTENSION IF NOT EXISTS pg_stat_statements")
    # The sampler lives in the container and reads its own cgroup, once a second.
    docker("exec", "-d", CONTAINER, "sh", "-c",
           "while :; do echo \"$(date +%s) $(cat /sys/fs/cgroup/memory.current) "
           "$(grep -E '^(anon|shmem|file) ' /sys/fs/cgroup/memory.stat | tr '\\n' ' ')\"; "
           "sleep 1; done > /tmp/memory.log")
    log(f"up: shared_buffers={shared_gb}GB, memory {total_mb or 'unlimited'} MB, {CPUS} CPUs, page cache evicted")


def snapshot():
    io = docker("exec", CONTAINER, "cat", "/sys/fs/cgroup/io.stat")
    device = {}
    for line in io.splitlines():
        if line.startswith(DEVICE + " "):
            device = dict(part.split("=") for part in line.split()[1:])
    events = dict(line.split() for line in docker("exec", CONTAINER, "cat", "/sys/fs/cgroup/memory.events").splitlines())
    row = psql("SELECT blks_read, blks_hit, temp_files, temp_bytes FROM pg_stat_database "
               "WHERE datname = 'essenthos_core'").split("|")
    return {
        "disk_read_bytes": int(device.get("rbytes", 0)),
        "disk_read_ops": int(device.get("rios", 0)),
        "blks_read": int(row[0]), "blks_hit": int(row[1]),
        "temp_files": int(row[2]), "temp_bytes": int(row[3]),
        "oom_kill": int(events.get("oom_kill", 0)), "memory_max_hits": int(events.get("max", 0)),
    }


def difference(before, after):
    return {key: after[key] - before[key] for key in before}


def memory_peaks(since):
    peak_total = peak_anon = peak_shmem = 0
    for line in docker("exec", CONTAINER, "cat", "/tmp/memory.log").splitlines():
        parts = line.split()
        if len(parts) < 8 or int(parts[0]) < since:
            continue
        values = dict(zip(parts[2::2], parts[3::2]))
        peak_total = max(peak_total, int(parts[1]))
        peak_anon = max(peak_anon, int(values.get("anon", 0)))
        peak_shmem = max(peak_shmem, int(values.get("shmem", 0)))
    mb = 1024 * 1024
    return {"peak_memory_mb": peak_total // mb, "peak_anon_mb": peak_anon // mb, "peak_shmem_mb": peak_shmem // mb}


def top_statements(limit=4):
    rows = psql(
        "SELECT round(total_exec_time)::bigint, calls, shared_blks_read, temp_blks_written, "
        "left(regexp_replace(query, '\\s+', ' ', 'g'), 140) FROM pg_stat_statements "
        f"ORDER BY total_exec_time DESC LIMIT {limit}")
    return [line.split("|", 4) for line in rows.splitlines() if line]


def measured(name, action):
    psql("SELECT pg_stat_statements_reset()")
    before = snapshot()
    since = int(time.time())
    started = time.perf_counter()
    outcome = action()
    seconds = round(time.perf_counter() - started, 1)
    time.sleep(1)
    result = {"phase": name, "seconds": seconds, **difference(before, snapshot()), **memory_peaks(since),
              "top": top_statements()}
    if isinstance(outcome, dict):
        result.update(outcome)
    log(f"{name}: {seconds}s, read {result['disk_read_bytes'] // (1024 * 1024)} MB from disk, "
        f"temp {result['temp_bytes'] // (1024 * 1024)} MB, peak {result['peak_memory_mb']} MB, "
        f"oom_kill {result['oom_kill']}")
    return result


def forge(resources, *args):
    environment = dict(os.environ,
                       Database__ConnectionString=CONNECTION, Database__Password=UNCHECKED_PASSWORD,
                       Dataset__ResourcesPath=resources, Dataset__OwnerDatabase="pgmeasure-copy",
                       DOTNET_ENVIRONMENT="Production")
    result = subprocess.run([FORGE, *args], cwd=os.path.dirname(FORGE), env=environment,
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    failed = result.returncode != 0 or "Unhandled exception" in result.stdout or "\nfail:" in result.stdout
    tail = [line for line in result.stdout.splitlines() if line.strip()][-12:]
    return {"exit": result.returncode, "failed": failed, "tail": tail}


def start_api():
    environment = dict(os.environ,
                       Urls=f"http://127.0.0.1:{API_PORT}", ASPNETCORE_ENVIRONMENT="Production",
                       Database__ConnectionString=CONNECTION, Database__Password=UNCHECKED_PASSWORD,
                       Accounts__ConnectionString=ACCOUNTS, Accounts__Password=UNCHECKED_PASSWORD,
                       RateLimits__Enabled="false")
    log_file = open(os.path.join(os.environ.get("PGMEASURE_LOGS", "."), "api.log"), "a", encoding="utf-8")
    process = subprocess.Popen([API], cwd=API_DIR, env=environment, stdout=log_file, stderr=subprocess.STDOUT)
    for _ in range(120):
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{API_PORT}/v1/health/live", timeout=2) as response:
                if response.status == 200:
                    return process
        except (urllib.error.URLError, ConnectionError, TimeoutError):
            pass
        if process.poll() is not None:
            raise RuntimeError("the private API exited while starting; see api.log")
        time.sleep(0.5)
    process.kill()
    raise RuntimeError("the private API did not answer on its port")


def fetch(path):
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(f"http://127.0.0.1:{API_PORT}{path}", timeout=120) as response:
            size = len(response.read())
            status = response.status
    except urllib.error.HTTPError as error:
        status, size = error.code, 0
    return round((time.perf_counter() - started) * 1000), status, size


def api_rounds(rounds):
    process = start_api()
    try:
        cold = {path: fetch(path) for path in ENDPOINTS}
        warm = {path: [fetch(path) for _ in range(rounds)] for path in ENDPOINTS}
    finally:
        process.kill()
        process.wait()
    table = {}
    for path in ENDPOINTS:
        times = [t for t, _, _ in warm[path]]
        statuses = sorted({s for _, s, _ in warm[path]} | {cold[path][1]})
        table[path] = {"cold_ms": cold[path][0], "warm_p50_ms": round(statistics.median(times)),
                       "warm_max_ms": max(times), "statuses": statuses, "bytes": cold[path][2]}
    return {"endpoints": table,
            "cold_total_ms": sum(v["cold_ms"] for v in table.values()),
            "warm_round_ms": round(sum(v["warm_p50_ms"] for v in table.values()))}


def run(shared_gb, resources, rounds, phases, unlimited=False):
    results = {"shared_buffers_gb": shared_gb, "memory_limit_mb": settings(shared_gb, unlimited)[0], "cpus": CPUS,
               "settings": settings(shared_gb, unlimited)[1], "phases": []}
    if "verify" in phases or "verify-cold" in phases:
        up(shared_gb, unlimited)
        results["phases"].append(measured("verify cold", lambda: forge(resources, "verify")))
        if "verify" in phases:
            results["phases"].append(measured("verify warm", lambda: forge(resources, "verify")))
    if "api" in phases:
        up(shared_gb, unlimited)
        results["phases"].append(measured("api", lambda: api_rounds(rounds)))
    if "write" in phases:
        up(shared_gb, unlimited)
        step = ("interlinear-join", "AVD1865", "--replace")
        results["phases"].append(measured("write cold", lambda: forge(resources, *step)))
        results["phases"].append(measured("write warm", lambda: forge(resources, *step)))
    results["memory_peak_lifetime_mb"] = int(docker("exec", CONTAINER, "cat", "/sys/fs/cgroup/memory.peak")) // (1024 * 1024)
    return results


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    up_parser = sub.add_parser("up")
    up_parser.add_argument("shared_gb", type=int)
    up_parser.add_argument("--unlimited", action="store_true")
    sub.add_parser("down")
    sub.add_parser("snapshot")
    run_parser = sub.add_parser("run")
    run_parser.add_argument("shared_gb", type=int)
    run_parser.add_argument("--resources", required=True)
    run_parser.add_argument("--out", required=True)
    run_parser.add_argument("--rounds", type=int, default=5)
    run_parser.add_argument("--phases", default="verify,api,write")
    run_parser.add_argument("--unlimited", action="store_true")
    api_parser = sub.add_parser("api")
    api_parser.add_argument("--rounds", type=int, default=5)
    args = parser.parse_args()

    if args.command == "up":
        up(args.shared_gb, args.unlimited)
    elif args.command == "down":
        down()
    elif args.command == "snapshot":
        print(json.dumps(snapshot(), indent=1))
    elif args.command == "api":
        print(json.dumps(api_rounds(args.rounds), indent=1, ensure_ascii=False))
    else:
        results = run(args.shared_gb, args.resources, args.rounds, args.phases.split(","), args.unlimited)
        with open(args.out, "w", encoding="utf-8") as file:
            json.dump(results, file, indent=1, ensure_ascii=False)
        log(f"written {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
