"""Apply an explicit cumulative budget grant without replacing paid work."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import time

MAIN=Path('C:/Users/timon/Projects/Essenthos/essenthos-core')
RUN=MAIN/'Resources/Essenthos/generation/person-occupations-sonnet55-2026-10-27-v3'
PROFILE=RUN.with_name('person-occupations-budget-2026-10-27-v5')
SERVICE='person-occupations-sonnet55'
SOURCE='owner-approved-occupation-budget-125'
spec=importlib.util.spec_from_file_location('occupation_budget_reader',RUN/'code/resume_person_occupations.py')
r=importlib.util.module_from_spec(spec);spec.loader.exec_module(r)


def policy(auth):
    if (auth.get('total_cap_usd'),auth.get('draft_subcap_usd'))!=(125,80):
        raise ValueError('Only the explicit125USD cumulative /80USD draft grant is authorized')
    if auth.get('label')!=r.p.LABEL or auth.get('model')!=r.p.MODEL or auth.get('owner_approved') is not True:
        raise ValueError('Budget grant must bind the existing task/model and explicit owner approval')


def initialize():
    file=PROFILE/'authorization.json'
    if file.exists():raise ValueError('Budget authorization already frozen')
    r.frozen();PROFILE.mkdir(parents=True,exist_ok=True)
    auth=dict(at=r.p.now(),task='TSK-1250',total_cap_usd=125,draft_subcap_usd=80,owner_approved=True,
        owner_instruction='Окей, піднімай.',meaning='125USD total, not125USD additional; include every previous task charge',
        model=r.p.MODEL,label=r.p.LABEL,baseline_spent_usd=sum(r.p.budget().values()),
        script_sha256=r.p.sha(Path(__file__)),reader_sha256=r.p.sha(RUN/'code/resume_person_occupations.py'),
        reader_manifest_sha256=r.p.sha(RUN/'manifest.json'),client_sha256=r.p.sha(MAIN/'scripts/claude_api.py'),human_accepted=False)
    policy(auth);r.write(file,auth)
    (PROFILE/'code').mkdir(exist_ok=True)
    shutil.copyfile(__file__,PROFILE/'code/occupation_budget.py')
    print(json.dumps(auth,ensure_ascii=False),flush=True)


def frozen():
    auth=r.read(PROFILE/'authorization.json');policy(auth)
    for file,expected in ((Path(__file__),auth['script_sha256']),
        (RUN/'code/resume_person_occupations.py',auth['reader_sha256']),
        (RUN/'manifest.json',auth['reader_manifest_sha256']),(MAIN/'scripts/claude_api.py',auth['client_sha256'])):
        if r.p.sha(file)!=expected:raise ValueError('Changed budget/dependency source '+str(file))
    r.frozen();return auth


def service_state():
    result=subprocess.run(['avioniq','services','list','--json'],cwd=MAIN.parent,
        shell=os.name=='nt',check=True,capture_output=True,text=True,encoding='utf-8')
    return next(s['state'] for s in json.loads(result.stdout)['services'] if s['name']==SERVICE)


def checkpoint():
    return r.read(RUN/'checkpoint.json') if (RUN/'checkpoint.json').exists() else dict(batches=[])


def submission_acknowledged(batch_ids):
    result=subprocess.run(['avioniq','services','logs',SERVICE,'--lines','0'],cwd=MAIN.parent,
        shell=os.name=='nt',check=True,capture_output=True,text=True,encoding='utf-8')
    acknowledged=set()
    for line in result.stdout.splitlines():
        try:row=json.loads(line)
        except ValueError:continue
        if isinstance(row,dict) and row.get('submitted'):acknowledged.add(row['submitted'])
    return set(batch_ids)<=acknowledged


def mark_handoff(state,grant):
    active=[b for b in checkpoint()['batches'] if b['status']!='collected']
    known=active and all(b.get('batch_id') and b['status']=='processing' for b in active)
    if state!='stopped' and not (known and submission_acknowledged([b['batch_id'] for b in active])):return False
    marker=dict(at=r.p.now(),source=SOURCE,authorization_sha256=grant,
        preserve_provider_batches=[b.get('batch_id') for b in active],cancel_provider_batches=False)
    with (RUN/'stop.json').open('x',encoding='utf-8') as file:json.dump(marker,file,ensure_ascii=False)
    return True


def handoff():
    auth=frozen();stop=RUN/'stop.json';grant=r.p.sha(PROFILE/'authorization.json');deadline=time.monotonic()+150
    if (PROFILE/'handoff-complete.json').exists():raise ValueError('Handoff already recorded; use the registered budgeted service')
    while True:
        state=service_state()
        if state=='failed':raise ValueError('Old worker failed; inspect its native failure before transfer')
        if stop.exists():
            marker=r.read(stop)
            if marker.get('source')!=SOURCE or marker.get('authorization_sha256')!=grant:
                raise ValueError('An operator cancellation exists; do not erase it for a budget change')
        else:
            mark_handoff(state,grant)
        if state=='stopped' and stop.exists() and not (RUN/'controller.lock').exists() and not (r.BASE/'controller.lock').exists():
            status=r.read(RUN/'status.json')
            if status.get('stop_reason') not in ('operator_stop','finished','budget_reservation_limit'):
                raise ValueError('Old worker completion is not accounted for')
            r.write(PROFILE/'handoff-complete.json',dict(at=r.p.now(),authorization_sha256=grant,
                stop_sha256=r.p.sha(stop),checkpoint_sha256=r.p.sha(RUN/'checkpoint.json'),
                outstanding=[b.get('batch_id') for b in checkpoint()['batches'] if b['status']!='collected'],
                spent_usd=sum(r.p.budget().values()),native_service_state=state))
            print('Old controller stopped cleanly; remote batches and all checkpoints preserved.',flush=True);return
        if time.monotonic()>deadline:raise ValueError('Handoff not complete; preserve marker/checkpoint and inspect service before restarting')
        time.sleep(2)


def apply_caps(auth):
    policy(auth);r.p.CAP=auth['total_cap_usd'];r.p.DRAFT_CAP=auth['draft_subcap_usd']
    original=r.p.cli
    def cli(*args,**kwargs):
        args=list(args)
        if args[:2]==['claim','TSK-1250'] and '--note' in args:
            index=args.index('--note')+1
            if args[index]=='Occupation v3 recovery, unchanged100USD total cap':
                args[index]='Occupation recovery, owner-approved125USD cumulative cap'
        return original(*args,**kwargs)
    r.p.cli=cli


def activate(auth):
    receipt=r.read(PROFILE/'handoff-complete.json');grant=r.p.sha(PROFILE/'authorization.json')
    if receipt['authorization_sha256']!=grant:raise ValueError('Handoff grant changed')
    with r.exclusive():
        r.stopped_base();stop=RUN/'stop.json'
        if stop.exists():
            marker=r.read(stop)
            if marker.get('source')!=SOURCE or r.p.sha(stop)!=receipt['stop_sha256']:
                raise ValueError('Operator stop must not be cleared by the budget launcher')
            archived=PROFILE/'applied-handoff-stop.json'
            stop.replace(archived)
            if r.p.sha(archived)!=receipt['stop_sha256']:
                try:os.link(archived,stop)
                except FileExistsError:pass
                raise ValueError('Operator stop raced with activation; preserved and not resumed')
        elif not (PROFILE/'budget-applied.json').exists():raise ValueError('No owned handoff marker to activate')
        r.write(PROFILE/'budget-applied.json',dict(at=r.p.now(),pid=os.getpid(),authorization_sha256=grant,
            total_cap_usd=125,draft_subcap_usd=80,existing_spent_usd=sum(r.p.budget().values()),
            existing_batches=[b.get('batch_id') for b in checkpoint()['batches']],human_accepted=False))
    apply_caps(auth)


def run():
    auth=frozen();activate(auth);r.run()


def report():
    auth=frozen();apply_caps(auth);r.report()


def cancel():
    auth=frozen();apply_caps(auth);r.cancel()


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=['initialize','handoff','run','report','cancel'])
    getattr(__import__(__name__),parser.parse_args().mode)()
