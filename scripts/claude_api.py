"""
The project's own Claude API client, paid by the owner's API key rather than a subscription.

The key is read from ANTHROPIC_API_KEY in the environment, or else from the ANTHROPIC_API_KEY line of
essenthos-core/.env (git-ignored; the owner writes it there himself). Nothing here prints, logs or
returns the key. Every call is written to .claude-api/usage.jsonl with its tokens and an estimated
cost, so what a run spent can be read back without the console.

    pip install anthropic          # once
    python scripts/claude_api.py check
        one small request; prints the model, the answer, the tokens and the cost

From a script:

    from claude_api import ask, batch_submit, batch_wait, batch_results
    answer = ask("…", system="…", label="known-for")             # one request now
    batch = batch_submit([("id-1", params), …], label="known-for")  # half price, within 24 h
    batch_wait(batch); texts = batch_results(batch)
"""
import datetime as dt
import json
import os
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
ENV_FILE = REPO / '.env'
LEDGER = REPO / '.claude-api' / 'usage.jsonl'
DEFAULT_MODEL = 'claude-opus-5-5'

# USD per million tokens, Claude API first-party rates (2026-10). A batch costs half.
PRICES = {
    'claude-opus-5-5': {'input': 4.00, 'output': 20.00, 'cache_read': 0.20},
    'claude-sonnet-5-5': {'input': 2.00, 'output': 10.00, 'cache_read': 0.20},
    'claude-haiku-5-5': {'input': 0.10, 'output': 0.50, 'cache_read': 0.01},
}


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


def cost(model, usage, batch=False):
    """Estimated USD for one response's usage; None for a model without a price here."""
    price = PRICES.get(model)
    if price is None:
        return None
    cache_write = getattr(usage, 'cache_creation_input_tokens', 0) or 0
    cache_read = getattr(usage, 'cache_read_input_tokens', 0) or 0
    total = (usage.input_tokens * price['input'] + cache_write * price['input'] * 1.25
             + cache_read * price['cache_read'] + usage.output_tokens * price['output']) / 1_000_000
    return round(total / 2 if batch else total, 6)


def record(label, model, usage, batch=False, request_id=None):
    LEDGER.parent.mkdir(exist_ok=True)
    line = {'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'label': label, 'model': model,
            'batch': batch, 'id': request_id, 'input': usage.input_tokens, 'output': usage.output_tokens,
            'cache_read': getattr(usage, 'cache_read_input_tokens', 0) or 0,
            'cache_write': getattr(usage, 'cache_creation_input_tokens', 0) or 0,
            'usd': cost(model, usage, batch)}
    with LEDGER.open('a', encoding='utf-8') as ledger:
        ledger.write(json.dumps(line) + '\n')
    return line


def text_of(message):
    return ''.join(block.text for block in message.content if block.type == 'text')


def ask(prompt, *, system=None, model=DEFAULT_MODEL, effort='medium', max_tokens=16000, label='ask'):
    """
    One request, answered now. Streams, so a long answer does not time out. On a safety refusal the
    API reruns it on a fallback model by itself (server-side fallback, "default"); a refusal that
    survives that raises.
    """
    params = {'model': model, 'max_tokens': max_tokens,
              'messages': [{'role': 'user', 'content': prompt}],
              'output_config': {'effort': effort}}
    if system:
        params['system'] = system
    with client().beta.messages.stream(**params, betas=['server-side-fallback-2026-07-01'],
                                       extra_body={'fallbacks': 'default'}) as stream:
        message = stream.get_final_message()
    record(label, message.model, message.usage, request_id=message.id)
    if message.stop_reason == 'refusal':
        details = getattr(message, 'stop_details', None)
        raise RuntimeError(f'refused: {getattr(details, "category", None)}')
    if message.stop_reason == 'max_tokens':
        raise RuntimeError(f'cut off at max_tokens={max_tokens}')
    return text_of(message)


def batch_submit(requests, *, label='batch'):
    """
    requests: (custom_id, params) pairs, params as for messages.create (model, max_tokens,
    messages, system, output_config…). Half price; most batches end within an hour, all within 24.
    Returns the batch id, also written to the ledger folder so a lost terminal does not lose it.
    """
    from anthropic.types.message_create_params import MessageCreateParamsNonStreaming
    from anthropic.types.messages.batch_create_params import Request
    batch = client().messages.batches.create(requests=[
        Request(custom_id=custom_id, params=MessageCreateParamsNonStreaming(**params))
        for custom_id, params in requests])
    LEDGER.parent.mkdir(exist_ok=True)
    with (LEDGER.parent / 'batches.jsonl').open('a', encoding='utf-8') as log:
        log.write(json.dumps({'at': dt.datetime.now(dt.timezone.utc).isoformat(), 'label': label,
                              'batch': batch.id, 'requests': len(requests)}) + '\n')
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
            record(label, message.model, message.usage, batch=True, request_id=result.custom_id)
            answers[result.custom_id] = text_of(message)
        else:
            failures[result.custom_id] = result.result.type
    return answers, failures


def spent(label=None):
    """Estimated USD in the ledger, for one label or all."""
    if not LEDGER.is_file():
        return 0.0
    rows = [json.loads(line) for line in LEDGER.read_text(encoding='utf-8').splitlines() if line.strip()]
    return round(sum(row['usd'] or 0 for row in rows if label is None or row['label'] == label), 4)


def main(argv):
    if argv[:1] != ['check']:
        print(__doc__)
        return 0
    started = time.time()
    answer = ask('In which book of the Bible is the verse "In the beginning God created the heaven and '
                 'the earth"? Answer with the book name only.', effort='low', max_tokens=2000, label='check')
    with LEDGER.open(encoding='utf-8') as ledger:
        last = json.loads(ledger.read().splitlines()[-1])
    print(f'model {last["model"]}: {answer.strip()!r}')
    print(f'{last["input"]} in, {last["output"]} out, ~${last["usd"]}, {time.time() - started:.1f}s; '
          f'ledger total ~${spent()}')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
