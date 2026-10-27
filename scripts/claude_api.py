"""
The project's own Claude API client, paid by the owner's API key rather than a subscription.

The key is read from ANTHROPIC_API_KEY in the environment, or else from the ANTHROPIC_API_KEY line of
essenthos-core/.env (git-ignored; the owner writes it there himself). Nothing here prints, logs or
returns the key. Every response is written to .claude-api/usage.jsonl with its tokens, its cache
reads and writes and an estimated cost, so what a run spent can be read back without the console.

    pip install anthropic          # once
    python scripts/claude_api.py check
        one small request; prints the model, the answer, the tokens and the cost
    python scripts/claude_api.py report [label]
        requests, tokens, cache hit rate and cost from the ledger, for one label or all

From a script:

    from claude_api import ask, build_params, run_batch
    answer = ask("…", system=SYSTEM, label="known-for")                  # one request now
    params = build_params("<this item's data>", system=SYSTEM, shared=CATALOGUE,
                          model="claude-sonnet-5-5", schema=SCHEMA, ttl="1h")
    answers, failures = run_batch([("id-1", params), …], label="known-for", cap_usd=20)

Requests are laid out stable-first so the API can cache them: tools, then the system prompt, then
the context every request of the run shares, each closed by a cache breakpoint, and only then what
is unique to this request. The guide `avioniq guide claude-api` says which TTL, model and limits
a run should use.
"""
import datetime as dt
import hashlib
import json
import os
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
ENV_FILE = REPO / '.env'
LEDGER = REPO / '.claude-api' / 'usage.jsonl'
DEFAULT_MODEL = 'claude-opus-5-5'

# USD per million tokens, Claude API first-party rates, read from
# platform.claude.com/docs/en/about-claude/pricing on 2026-10-28. Cache writes cost 1.25x input for the
# 5-minute TTL and 2x for the 1-hour one; a batch halves every price, cache reads and writes included.
PRICES = {
    'claude-opus-5-5': {'input': 4.00, 'output': 20.00, 'cache_read': 0.20},
    'claude-sonnet-5-5': {'input': 2.00, 'output': 10.00, 'cache_read': 0.10},
    'claude-haiku-5-5': {'input': 0.10, 'output': 0.50, 'cache_read': 0.01,
                         'long': {'over': 100_000, 'input': 0.50, 'output': 2.50, 'cache_read': 0.05}},
}
WRITE_MULTIPLIER = {'5m': 1.25, '1h': 2.0}
# The shortest prefix the API caches; a shorter one is sent uncached without an error.
MIN_CACHEABLE_TOKENS = {'claude-opus-5-5': 512, 'claude-sonnet-5-5': 512, 'claude-haiku-5-5': 512}
MAX_BREAKPOINTS = 4


class BudgetExceeded(RuntimeError):
    """A request or batch would take a label past its spending cap; nothing was sent."""


def api_key():
    """The key, from the environment or the .env file. Never printed."""
    key = os.environ.get('ANTHROPIC_API_KEY', '').strip()
    if key:
        return key
    if ENV_FILE.is_file():
        for line in ENV_FILE.read_text(encoding='utf-8').splitlines():
            name, _, value = line.partition('=')
            if name.strip() == 'ANTHROPIC_API_KEY':
                value = value.strip().strip('"').strip("'")
                if value:
                    return value
    raise SystemExit(
        'No Anthropic API key: put a line ANTHROPIC_API_KEY=<key> in ' + str(ENV_FILE)
        + ' (git-ignored), or set ANTHROPIC_API_KEY in the environment.')


_client = None


def client():
    global _client
    if _client is None:
        import anthropic
        _client = anthropic.Anthropic(api_key=api_key(), max_retries=4)
    return _client


def _field(obj, name, default=0):
    if obj is None:
        return default
    value = obj.get(name, default) if isinstance(obj, dict) else getattr(obj, name, default)
    return default if value is None else value


def usage_counts(usage):
    """The usage meters of one response as plain integers, the cache write split by TTL."""
    written = _field(usage, 'cache_creation_input_tokens')
    split = _field(usage, 'cache_creation', None)
    write_1h = _field(split, 'ephemeral_1h_input_tokens')
    write_5m = _field(split, 'ephemeral_5m_input_tokens') if split is not None else written
    if split is not None and write_5m + write_1h != written:
        write_5m = written - write_1h
    return {'input': _field(usage, 'input_tokens'), 'output': _field(usage, 'output_tokens'),
            'cache_read': _field(usage, 'cache_read_input_tokens'), 'cache_write': written,
            'cache_write_5m': write_5m, 'cache_write_1h': write_1h,
            'thinking': _field(_field(usage, 'output_tokens_details', None), 'thinking_tokens')}


def cost(model, usage, batch=False):
    """Estimated USD for one response's usage (an SDK object or a dict); None for an unpriced model."""
    price = PRICES.get(model)
    if price is None:
        return None
    n = usage_counts(usage)
    prompt = n['input'] + n['cache_write'] + n['cache_read']
    long = price.get('long')
    if long and prompt > long['over']:
        price = long
    total = (n['input'] * price['input']
             + n['cache_write_5m'] * price['input'] * WRITE_MULTIPLIER['5m']
             + n['cache_write_1h'] * price['input'] * WRITE_MULTIPLIER['1h']
             + n['cache_read'] * price['cache_read'] + n['output'] * price['output']) / 1_000_000
    return round(total / 2 if batch else total, 6)


def record(label, model, usage, batch=False, request_id=None, **extra):
    """
    One ledger line per response. `id` is the message id of a live request and the custom id of a
    batch request, as it always was; extra fields (message_id, batch_id, stop_reason) are added.
    """
    LEDGER.parent.mkdir(exist_ok=True)
    n = usage_counts(usage)
    line = {'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'label': label, 'model': model,
            'batch': batch, 'id': request_id, 'input': n['input'], 'output': n['output'],
            'cache_read': n['cache_read'], 'cache_write': n['cache_write'],
            'cache_write_5m': n['cache_write_5m'], 'cache_write_1h': n['cache_write_1h'],
            'thinking': n['thinking'], 'usd': cost(model, usage, batch)}
    line.update({k: v for k, v in extra.items() if v is not None})
    with LEDGER.open('a', encoding='utf-8') as ledger:
        ledger.write(json.dumps(line) + '\n')
    return line


def text_of(message):
    return ''.join(block.text for block in message.content if block.type == 'text')


# ---------------------------------------------------------------------------------------------------
# Request layout

def _text(part):
    """A shared or system part as text: dicts and lists as canonical JSON, so the bytes never vary."""
    if isinstance(part, str):
        return part
    return json.dumps(part, ensure_ascii=False, sort_keys=True, separators=(',', ':'))


def estimate_tokens(text):
    """
    A deliberately high estimate (UTF-8 bytes / 3): English runs ~4 bytes a token, Hebrew and
    Greek fewer. Used to decide whether a prefix can reach the cache minimum and to reserve budget,
    never to bill; count_tokens is exact but is a call to the API.
    """
    return len(text.encode('utf-8')) // 3 + 1


def _marker(ttl):
    if ttl not in WRITE_MULTIPLIER:
        raise ValueError(f'cache ttl must be one of {sorted(WRITE_MULTIPLIER)}, not {ttl!r}')
    return {'type': 'ephemeral'} if ttl == '5m' else {'type': 'ephemeral', 'ttl': ttl}


def build_params(prompt, *, system=None, shared=None, tools=None, model=DEFAULT_MODEL, effort='medium',
                 max_tokens=16000, schema=None, ttl='5m', shared_ttl=None, cache=True):
    """
    Messages API params for one request, stable-first: tools (sorted by name), the system prompt,
    the context shared by every request of the run (`shared`, a string, a dict or a list of them),
    then `prompt`, the part unique to this request. A cache breakpoint closes each stable layer once
    the prefix up to it can reach the model's minimum, and none is placed on the unique part. Shared
    context sent after the unique part is never cached: the cache is a prefix match.

    ttl is the system prefix's TTL and shared_ttl the shared context's (default: the same). A longer
    TTL must not follow a shorter one, so ('5m', '1h') is refused.
    """
    shared_ttl = shared_ttl or ttl
    _marker(ttl), _marker(shared_ttl)
    if cache and WRITE_MULTIPLIER[shared_ttl] > WRITE_MULTIPLIER[ttl]:
        raise ValueError('a 1h cache entry cannot follow a 5m one: give the system prefix the longer TTL')
    minimum = MIN_CACHEABLE_TOKENS.get(model, 1024)
    params = {'model': model, 'max_tokens': max_tokens, 'output_config': {'effort': effort}}
    if schema is not None:
        params['output_config']['format'] = {'type': 'json_schema', 'schema': schema}
    prefix = 0
    tool_list = [dict(t) for t in sorted(tools or [], key=lambda t: t['name'])]
    for tool in tool_list:
        tool.pop('cache_control', None)
        prefix += estimate_tokens(_text(tool))
    if tool_list:
        params['tools'] = tool_list
    system_parts = [system] if isinstance(system, (str, dict)) else list(system or [])
    system_blocks = [{'type': 'text', 'text': _text(p)} for p in system_parts if _text(p)]
    prefix += sum(estimate_tokens(b['text']) for b in system_blocks)
    if schema is not None:
        prefix += estimate_tokens(_text(schema))
    if system_blocks:
        params['system'] = system_blocks
    last_stable = system_blocks[-1] if system_blocks else (tool_list[-1] if tool_list else None)
    if cache and last_stable is not None and prefix >= minimum:
        last_stable['cache_control'] = _marker(ttl)
    shared_parts = [shared] if isinstance(shared, (str, dict)) else list(shared or [])
    content = [{'type': 'text', 'text': _text(p)} for p in shared_parts if _text(p)]
    prefix += sum(estimate_tokens(b['text']) for b in content)
    if cache and content and prefix >= minimum:
        content[-1]['cache_control'] = _marker(shared_ttl)
    content.append({'type': 'text', 'text': _text(prompt)})
    params['messages'] = [{'role': 'user', 'content': content}]
    check_cache_layout(params)
    return params


def _blocks_in_order(params):
    for tool in params.get('tools') or []:
        yield tool
    system = params.get('system')
    if isinstance(system, list):
        yield from system
    for message in params.get('messages') or []:
        if isinstance(message.get('content'), list):
            yield from message['content']


def check_cache_layout(params):
    """Refuse what the API would refuse: more than four breakpoints, or a 1h entry after a 5m one."""
    markers = [b['cache_control'] for b in _blocks_in_order(params) if isinstance(b, dict) and b.get('cache_control')]
    if params.get('cache_control'):
        markers.append(params['cache_control'])
    if len(markers) > MAX_BREAKPOINTS:
        raise ValueError(f'{len(markers)} cache breakpoints; the API allows {MAX_BREAKPOINTS}')
    seen_5m = False
    for marker in markers:
        ttl = marker.get('ttl', '5m')
        if ttl == '1h' and seen_5m:
            raise ValueError('a 1h cache breakpoint follows a 5m one; longer TTLs must come first')
        seen_5m = seen_5m or ttl == '5m'
    return len(markers)


def cached_prefix_key(params):
    """A hash of everything up to the last breakpoint: requests with equal keys can share one cache entry."""
    head, last = [], 0
    for i, block in enumerate(_blocks_in_order(params)):
        head.append(block)
        if isinstance(block, dict) and block.get('cache_control'):
            last = i + 1
    if not last:
        return None
    stable = [{k: v for k, v in b.items() if k != 'cache_control'} for b in head[:last]]
    body = json.dumps([params.get('model'), params.get('output_config'), stable], sort_keys=True, ensure_ascii=False)
    return hashlib.sha256(body.encode('utf-8')).hexdigest()[:16]


# ---------------------------------------------------------------------------------------------------
# Spending

def estimate_usd(params, batch=False):
    """
    An upper bound for one request: every input token at the uncached price (high estimate) and the
    whole max_tokens of output. Caching only lowers the real figure.
    """
    price = PRICES.get(params['model'])
    if price is None:
        raise ValueError(f'no price for {params["model"]}: add it to PRICES before spending on it')
    text = json.dumps({k: params.get(k) for k in ('tools', 'system', 'messages', 'output_config')}, ensure_ascii=False)
    tokens = estimate_tokens(text)
    long = price.get('long')
    if long and tokens > long['over']:
        price = long
    total = (tokens * price['input'] + params['max_tokens'] * price['output']) / 1_000_000
    return total / 2 if batch else total


def guard(label, reserve_usd, cap_usd):
    """Stop before sending when what the label has spent plus this reserve would pass its cap."""
    if cap_usd is None:
        return
    already = spent(label)
    if already + reserve_usd > cap_usd:
        raise BudgetExceeded(f'{label}: ${already:.2f} spent + ${reserve_usd:.2f} reserved for this send '
                             f'> cap ${cap_usd:.2f}; nothing was sent')


def ask(prompt, *, system=None, shared=None, model=DEFAULT_MODEL, effort='medium', max_tokens=16000,
        label='ask', ttl='5m', schema=None, cap_usd=None):
    """
    One request, answered now. Streams, so a long answer does not time out. On a safety refusal the
    API reruns it on a fallback model by itself (server-side fallback, "default"); a refusal that
    survives that raises. The system prompt and shared context are cached (see build_params), which
    pays when the next ask with the same prefix starts within the TTL.
    """
    params = build_params(prompt, system=system, shared=shared, model=model, effort=effort,
                          max_tokens=max_tokens, schema=schema, ttl=ttl)
    guard(label, estimate_usd(params), cap_usd)
    with client().beta.messages.stream(**params, betas=['server-side-fallback-2026-07-01'],
                                       extra_body={'fallbacks': 'default'}) as stream:
        message = stream.get_final_message()
    record(label, message.model, message.usage, request_id=message.id, stop_reason=message.stop_reason)
    if message.stop_reason == 'refusal':
        details = getattr(message, 'stop_details', None)
        raise RuntimeError(f'refused: {getattr(details, "category", None)}')
    if message.stop_reason == 'max_tokens':
        raise RuntimeError(f'cut off at max_tokens={max_tokens}')
    return text_of(message)


def batch_plan(requests):
    """What a batch will cost at most and how its requests share cached prefixes, before it is sent."""
    prefixes = {}
    for _, params in requests:
        key = cached_prefix_key(params)
        prefixes[key] = prefixes.get(key, 0) + 1
    return {'requests': len(requests),
            'reserve_usd': round(sum(estimate_usd(p, batch=True) for _, p in requests), 4),
            'cached_prefixes': {k: v for k, v in prefixes.items() if k},
            'uncached_requests': prefixes.get(None, 0)}


def batch_submit(requests, *, label='batch', cap_usd=None):
    """
    requests: (custom_id, params) pairs, params as for messages.create (model, max_tokens,
    messages, system, output_config…), best made by build_params. Half price, cache reads and writes
    included; most batches end within an hour, all within 24. With cap_usd the batch is refused
    before sending when its upper-bound cost would take the label past the cap. Returns the batch
    id, also written to the ledger folder so a lost terminal does not lose it.
    """
    from anthropic.types.message_create_params import MessageCreateParamsNonStreaming
    from anthropic.types.messages.batch_create_params import Request
    for _, params in requests:
        check_cache_layout(params)
    plan = batch_plan(requests)
    guard(label, plan['reserve_usd'], cap_usd)
    batch = client().messages.batches.create(requests=[
        Request(custom_id=custom_id, params=MessageCreateParamsNonStreaming(**params))
        for custom_id, params in requests])
    LEDGER.parent.mkdir(exist_ok=True)
    with (LEDGER.parent / 'batches.jsonl').open('a', encoding='utf-8') as log:
        log.write(json.dumps({'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'label': label,
                              'batch': batch.id, 'requests': len(requests), 'reserve_usd': plan['reserve_usd'],
                              'cached_prefixes': len(plan['cached_prefixes'])}) + '\n')
    return batch.id


def batch_wait(batch_id, poll=60):
    while True:
        batch = client().messages.batches.retrieve(batch_id)
        if batch.processing_status == 'ended':
            return batch
        time.sleep(poll)


def batch_results(batch_id, *, label='batch'):
    """{custom_id: text} for the requests that succeeded, and {custom_id: why} for the rest."""
    answers, failures = {}, {}
    for result in client().messages.batches.results(batch_id):
        if result.result.type == 'succeeded':
            message = result.result.message
            record(label, message.model, message.usage, batch=True, request_id=result.custom_id,
                   message_id=message.id, batch_id=batch_id, stop_reason=message.stop_reason)
            answers[result.custom_id] = text_of(message)
        else:
            failures[result.custom_id] = result.result.type
    return answers, failures


def run_batch(requests, *, label, cap_usd, poll=60, report=True):
    """
    The unattended path: refuse past the cap, submit, wait, collect, and print the label's summary
    (cache hit rate and cost). A run that needs no answer within minutes uses this, not ask().
    """
    batch_id = batch_submit(requests, label=label, cap_usd=cap_usd)
    batch_wait(batch_id, poll=poll)
    answers, failures = batch_results(batch_id, label=label)
    if report:
        print(format_summary(summary(label)))
    return answers, failures


# ---------------------------------------------------------------------------------------------------
# Reading the ledger back

def ledger_rows(label=None):
    if not LEDGER.is_file():
        return []
    rows = [json.loads(line) for line in LEDGER.read_text(encoding='utf-8').splitlines() if line.strip()]
    return [row for row in rows if label is None or row['label'] == label]


def spent(label=None):
    """Estimated USD in the ledger, for one label or all, as each line recorded it."""
    return round(sum(row['usd'] or 0 for row in ledger_rows(label)), 4)


def _row_usage(row):
    write_1h = row.get('cache_write_1h', 0)
    return {'input_tokens': row['input'], 'output_tokens': row['output'],
            'cache_read_input_tokens': row['cache_read'], 'cache_creation_input_tokens': row['cache_write'],
            'cache_creation': {'ephemeral_5m_input_tokens': row.get('cache_write_5m', row['cache_write'] - write_1h),
                               'ephemeral_1h_input_tokens': write_1h}}


def summary(label=None, rows=None):
    """
    Requests, tokens, cache hit rate (cache reads / all prompt tokens) and cost, priced again from the
    tokens at today's PRICES, beside what the same tokens would have cost uncached and unbatched.
    """
    rows = ledger_rows(label) if rows is None else rows
    s = {'requests': len(rows), 'batch_requests': sum(1 for r in rows if r['batch']),
         'input': 0, 'cache_write': 0, 'cache_read': 0, 'output': 0, 'thinking': 0,
         'usd': 0.0, 'usd_recorded': 0.0, 'usd_without_cache': 0.0, 'usd_without_batch': 0.0,
         'truncated': sum(1 for r in rows if r.get('stop_reason') == 'max_tokens'),
         'refused': sum(1 for r in rows if r.get('stop_reason') == 'refusal'), 'unpriced': 0}
    for r in rows:
        for key in ('input', 'cache_write', 'cache_read', 'output'):
            s[key] += r[key]
        s['thinking'] += r.get('thinking', 0)
        s['usd_recorded'] += r['usd'] or 0
        usage = _row_usage(r)
        now = cost(r['model'], usage, r['batch'])
        if now is None:
            s['unpriced'] += 1
            continue
        flat = {'input_tokens': r['input'] + r['cache_write'] + r['cache_read'], 'output_tokens': r['output']}
        s['usd'] += now
        s['usd_without_cache'] += cost(r['model'], flat, r['batch'])
        s['usd_without_batch'] += cost(r['model'], usage, False)
    prompt = s['input'] + s['cache_write'] + s['cache_read']
    s['prompt_tokens'] = prompt
    s['cache_hit_rate'] = round(s['cache_read'] / prompt, 4) if prompt else 0.0
    s['saved_by_cache'] = s['usd_without_cache'] - s['usd']
    s['saved_by_batch'] = s['usd_without_batch'] - s['usd']
    for key in ('usd', 'usd_recorded', 'usd_without_cache', 'usd_without_batch', 'saved_by_cache', 'saved_by_batch'):
        s[key] = round(s[key], 4)
    return s


def format_summary(s, label=None):
    head = f'{label}: ' if label else ''
    return (f'{head}{s["requests"]} requests ({s["batch_requests"]} batched); prompt {s["prompt_tokens"]:,} tokens, '
            f'cache read {s["cache_read"]:,} ({s["cache_hit_rate"]:.1%}), written {s["cache_write"]:,}, '
            f'uncached {s["input"]:,}; output {s["output"]:,} (thinking {s["thinking"]:,})\n'
            f'  ${s["usd"]:.2f} at current prices (recorded ${s["usd_recorded"]:.2f}); caching saved '
            f'${s["saved_by_cache"]:.2f}, batching saved ${s["saved_by_batch"]:.2f}'
            + (f'; {s["truncated"]} cut off at max_tokens' if s['truncated'] else '')
            + (f'; {s["refused"]} refused' if s['refused'] else ''))


def main(argv):
    if argv[:1] == ['report']:
        labels = argv[1:] or sorted({row['label'] for row in ledger_rows()})
        for label in labels:
            print(format_summary(summary(label), label))
        if len(labels) > 1:
            print(format_summary(summary(), 'all'))
        return 0
    if argv[:1] != ['check']:
        print(__doc__)
        return 0
    started = time.time()
    answer = ask('In which book of the Bible is the verse "In the beginning God created the heaven and '
                 'the earth"? Answer with the book name only.', effort='low', max_tokens=2000, label='check')
    last = ledger_rows()[-1]
    print(f'model {last["model"]}: {answer.strip()!r}')
    print(f'{last["input"]} in, {last["output"]} out, ~${last["usd"]}, {time.time() - started:.1f}s; '
          f'ledger total ~${spent()}')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
