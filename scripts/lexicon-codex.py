"""Codex Sol translation transport for the shared lexicon batch/check/publish workflow."""

import datetime
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import uuid


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('lexicon_shared', HERE / 'lexicon.py')
lexicon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lexicon)
# Beside this file in scripts/ and in a run's frozen code/ folder alike; a run frozen before it
# existed finds the repository's copy above it.
_cli = next((folder / 'codex_cli.py' for folder in (HERE, *(parent / 'scripts' for parent in HERE.parents))
             if (folder / 'codex_cli.py').is_file()), None)
if _cli is None:
    raise RuntimeError('codex_cli.py is missing beside lexicon-codex.py and in the repository scripts/')
_spec = importlib.util.spec_from_file_location('codex_cli', _cli)
codex_cli = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(codex_cli)
MODEL = 'gpt-6.1-sol'
TRANSPORT_VERSION = 'codex-lexicon-2'
DISABLED = ('shell_tool', 'multi_agent', 'apps', 'plugins', 'computer_use',
            'browser_use', 'code_mode_host', 'view_image', 'memories', 'hooks',
            'image_generation', 'sleep_tool', 'workspace_dependencies', 'shell_snapshot')
ENTRY_PROPERTIES = {
    'strong_number': {'type': 'string'},
    **{field: {'type': ['string', 'null']} for field in lexicon.PROSE},
    'uncertain': {'type': 'array', 'items': {'type': 'string'}},
}
SCHEMA = {
    'type': 'object', 'additionalProperties': False,
    'properties': {'entries': {'type': 'array', 'items': {
        'type': 'object', 'additionalProperties': False,
        'properties': ENTRY_PROPERTIES, 'required': list(ENTRY_PROPERTIES)}}},
    'required': ['entries'],
}
JUDGE_PROPERTIES = {
    'strong_number': {'type': 'string'},
    **{axis: {'type': 'integer'} for axis in ('terminology', 'completeness', 'fluency')},
    'errors': {'type': 'array', 'items': {
        'type': 'object', 'additionalProperties': False,
        'properties': {key: {'type': 'string'} for key in ('field', 'quote', 'problem')},
        'required': ['field', 'quote', 'problem']}},
}
JUDGE_SCHEMA = {
    'type': 'object', 'additionalProperties': False,
    'properties': {'entries': {'type': 'array', 'items': {
        'type': 'object', 'additionalProperties': False,
        'properties': JUDGE_PROPERTIES, 'required': list(JUDGE_PROPERTIES)}}},
    'required': ['entries'],
}
DISABLED_CODE_NOTICE = ('Code Mode is unavailable because code-mode host is disabled. '
                        'Code mode will fail closed;')


def executable():
    # Asked again for every call: Codex moves its CLI to a new folder on each update (codex_cli.py).
    return codex_cli.executable()


def decode(value):
    return value.decode('utf-8', 'replace') if isinstance(value, bytes) else value or ''


def events_of(stdout):
    events = []
    for line in stdout.splitlines():
        if line.strip():
            event = json.loads(line)
            if not isinstance(event, dict):
                raise ValueError('Codex emitted a non-object event')
            events.append(event)
    return events


def usage_result(events, model):
    completed = [event for event in events if event.get('type') == 'turn.completed']
    usage = completed[-1].get('usage') if completed else None
    return {'modelUsage': {model: usage or {}}, 'usage': usage,
            'total_cost_usd': None, 'provider': 'codex-cli',
            'requested_model': model, 'model_identity_source': 'explicit-cli-request',
            'transport_version': TRANSPORT_VERSION, 'completed': bool(completed)}


def normalize(stdout, answer_text, model):
    events = events_of(stdout)
    errors = [str(event.get('error') or event.get('message') or event)
              for event in events if event.get('type') in ('error', 'turn.failed')]
    if errors:
        raise ValueError('; '.join(errors)[:700])
    completed = [event for event in events if event.get('type') == 'turn.completed']
    if len(completed) != 1:
        raise ValueError('Expected exactly one completed translation turn')
    for event in events:
        item = event.get('item') or {}
        if item.get('type') == 'error' and str(item.get('message', '')).startswith(DISABLED_CODE_NOTICE):
            continue
        if event.get('type', '').startswith('item.') and item.get('type') not in (
                'agent_message', 'reasoning'):
            raise ValueError(f'Unexpected tool activity: {item.get("type")}')
    answer = json.loads(answer_text)
    if not isinstance(answer, dict) or set(answer) != {'entries'}:
        raise ValueError('The final response must contain only entries')
    if not isinstance(answer['entries'], list) or not all(
            isinstance(row, dict) for row in answer['entries']):
        raise ValueError('The final entries must be an array of objects')
    return {**usage_result(events, model),
            'result': json.dumps(answer['entries'], ensure_ascii=False)}


def call(prompt, system, model, effort='medium', record_dir=None, provenance=None):
    if model != MODEL:
        return None, f'This transport requires {MODEL}; refusing model substitution: {model}'
    if effort not in ('low', 'medium', 'high', 'xhigh', 'max'):
        return None, f'Unsupported Codex reasoning effort: {effort}'
    started = time.monotonic()
    judging = (provenance or {}).get('phase') == 'judge'
    schema_value = JUDGE_SCHEMA if judging else SCHEMA
    record = {'started_at': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'provider': 'codex-cli', 'requested_model': model, 'effort': effort,
              'provenance': provenance, 'prompt': prompt, 'system': system,
              'schema': schema_value, 'transport_version': TRANSPORT_VERSION}

    def finish(outcome=None, error=None, stdout='', stderr='', returncode=None, answer=''):
        record.update(outcome=outcome, error=error, stdout=stdout, stderr=stderr,
                      returncode=returncode, answer=answer,
                      elapsed_seconds=time.monotonic() - started)
        if record_dir:
            directory = Path(record_dir)
            directory.mkdir(parents=True, exist_ok=True)
            (directory / (uuid.uuid4().hex + '.json')).write_text(
                json.dumps(record, ensure_ascii=False, indent=1), encoding='utf-8')
        return (None if error else outcome), error

    try:
        with tempfile.TemporaryDirectory(prefix='essenthos-lexicon-codex-') as temporary:
            work = Path(temporary)
            instructions = work / 'instructions.txt'
            schema = work / 'schema.json'
            last = work / 'answer.json'
            effective = system + '''

TRANSPORT CONTRACT: Perform the supplied dictionary translation or review task. Use only the supplied
entry data; do not use tools, inspect files, browse, or perform repository work.
Return one JSON object with an entries array conforming to the output schema.
All other translation, reference and sense-preservation instructions still apply.
'''
            if not judging:
                effective += 'For source prose fields not supplied, return null; never invent prose for them.\n'
            instructions.write_text(effective, encoding='utf-8')
            schema.write_text(json.dumps(schema_value), encoding='utf-8')
            command = [executable(), 'exec', '--ignore-user-config', '--ignore-rules',
                       '--ephemeral', '--skip-git-repo-check', '--sandbox', 'read-only',
                       '--model', model, '--json', '--color', 'never',
                       '--output-schema', str(schema), '--output-last-message', str(last),
                       '--cd', str(work), '-c', 'approval_policy="never"',
                       '-c', 'web_search="disabled"', '-c', 'project_doc_max_bytes=0',
                       '-c', 'mcp_servers={}',
                       '-c', 'model_reasoning_effort=' + json.dumps(effort),
                       '-c', 'model_instructions_file=' + json.dumps(instructions.as_posix())]
            for feature in DISABLED:
                command.extend(['--disable', feature])
            command.append('-')
            record.update(command=command, effective_system=effective)
            process = subprocess.run(command, input=prompt.encode('utf-8'),
                                     stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                     timeout=lexicon.CALL_TIMEOUT_SECONDS, cwd=work)
            stdout, stderr = decode(process.stdout), decode(process.stderr)
            answer = last.read_text(encoding='utf-8') if last.exists() else ''
            retained_usage = None
            try:
                retained_usage = usage_result(events_of(stdout), model)
            except (ValueError, TypeError):
                pass
            try:
                outcome = normalize(stdout, answer, model)
            except (ValueError, TypeError) as error:
                return finish(outcome=retained_usage,
                              error=f'Codex exit {process.returncode}: {error}; {stderr[-600:]}',
                              stdout=stdout, stderr=stderr, returncode=process.returncode,
                              answer=answer)
            if process.returncode:
                return finish(outcome=outcome, error=f'Codex exited {process.returncode}',
                              stdout=stdout, stderr=stderr, returncode=process.returncode,
                              answer=answer)
            return finish(outcome, stdout=stdout, stderr=stderr,
                          returncode=process.returncode, answer=answer)
    except subprocess.TimeoutExpired as error:
        return finish(error=f'Codex timed out after {lexicon.CALL_TIMEOUT_SECONDS}s',
                      stdout=decode(error.stdout), stderr=decode(error.stderr))
    except (OSError, RuntimeError) as error:
        return finish(error=f'{type(error).__name__}: {error}')


lexicon.call = call


def main():
    if len(sys.argv) > 1 and sys.argv[1] in ('ask', 'judge') and '--model' not in sys.argv:
        sys.argv.extend(['--model', MODEL])
    lexicon.main()


if __name__ == '__main__':
    main()
