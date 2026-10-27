"""Resume source-bound readings without repeating valid drafts or resetting spend."""
import argparse
import collections
import contextlib
import copy
import importlib.util
import json
import os
from pathlib import Path
import shutil
import time

MAIN=Path('C:/Users/timon/Projects/Essenthos/essenthos-core')
BASE=MAIN/'Resources/Essenthos/generation/person-occupations-sonnet55-2026-10-27-v2'
OUT=BASE.with_name('person-occupations-sonnet55-2026-10-27-v3')
spec=importlib.util.spec_from_file_location('frozen_occupation_v2',BASE/'code/person_occupations.py')
p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)
MAX_ATTEMPTS=3
WAVE=80
PILOT_CAP=3.0


def path(phase,kind,job):return OUT/phase/kind/(job+'.json')
def read(file):return p.read(file)
def write(file,value):return p.dump(file,value)
def attempts(phase,job):
    return sorted((read(f) for f in (OUT/phase/'attempts').glob(job+'-*.json')),key=lambda r:r['attempt'])


def stopped_base():
    state=read(BASE/'checkpoint.json')
    if any(b['status']!='collected' for b in state['batches']):
        raise ValueError('Uncollected/unknown v2 submission: collect it before recovery')
    if (BASE/'stop.json').exists():raise ValueError('V2 was explicitly cancelled; inspect before recovery')


def pilot_idle():
    if (OUT/'stop.json').exists():raise ValueError('Recovery was explicitly cancelled')
    checkpoint=OUT/'checkpoint.json'
    if checkpoint.exists() and any(b['status']!='collected' for b in read(checkpoint)['batches']):
        raise ValueError('Active/unknown recovery batch must be reconciled before pilot')


@contextlib.contextmanager
def exclusive():
    owned=[]
    try:
        for file in (BASE/'controller.lock',OUT/'controller.lock'):
            fd=os.open(file,os.O_CREAT|os.O_EXCL|os.O_WRONLY)
            os.write(fd,str(os.getpid()).encode());os.close(fd);owned.append(file)
        yield
    finally:
        for file in reversed(owned):file.unlink(missing_ok=True)


def split_packet(packet,size):
    rows=[]
    for offset in range(0,len(packet['persons']),size):
        unit=copy.deepcopy(packet)
        unit['job_id']=packet['job_id']+f'-r{offset//size:02d}'
        unit['persons']=unit['persons'][offset:offset+size]
        rows.append(unit)
    assert collections.Counter(x['slug'] for u in rows for x in u['persons'])==collections.Counter(x['slug'] for x in packet['persons'])
    return rows


def prepare():
    OUT.mkdir(parents=True,exist_ok=True)
    with exclusive():prepare_locked()


def prepare_locked():
    if (OUT/'manifest.json').exists():raise ValueError('Recovery already frozen')
    stopped_base();base=p.frozen()
    OUT.mkdir(parents=True,exist_ok=True)
    parents=[];retained=[];new=[];source_index={j['job_id']:j for j in base['jobs']}
    for entry in base['jobs']:
        job=entry['job_id'];source=BASE/'draft/inputs'/(job+'.json')
        assert p.sha(source)==entry['sha256'];packet=read(source)
        checked=BASE/'draft/checked'/(job+'.json')
        if checked.exists():
            result=p.result_file('draft',job,packet)
            raw=BASE/'draft/raw'/(job+f"-{result['attempt']}-message.json")
            retained.append(dict(job_id=job,checked_sha256=p.sha(checked),raw_sha256=p.sha(raw),
                input_sha256=entry['sha256'],raw_file=raw.name,answer_sha256=p.digest(result['answer'])))
            units=[job]
        else:
            tried=list((BASE/'draft/attempts').glob(job+'-*.json'))
            units=[]
            for unit in split_packet(packet,4 if tried else 6):
                file=path('draft','inputs',unit['job_id'])
                if file.exists() and read(file)!=unit:raise ValueError('Conflicting recovery input')
                write(file,unit);units.append(unit['job_id'])
                new.append(dict(job_id=unit['job_id'],parent=job,sha256=p.sha(file),persons=[x['slug'] for x in unit['persons']]))
        parents.append(dict(job_id=job,input_sha256=entry['sha256'],units=units,persons=entry['persons']))
    costs=p.budget()
    manifest=dict(at=p.now(),task='TSK-1250',model=p.MODEL,budget_usd=p.CAP,draft_subcap_usd=p.DRAFT_CAP,
        baseline_spend_usd=sum(costs.values()),pilot_incremental_cap_usd=PILOT_CAP,
        original_manifest_sha256=p.sha(BASE/'run-manifest.json'),frozen_reader_sha256=p.sha(BASE/'code/person_occupations.py'),
        script_sha256=p.sha(Path(__file__)),client_sha256=p.sha(MAIN/'scripts/claude_api.py'),
        parents=parents,retained=retained,new_drafts=new,persons_sha256=p.sha(BASE/'persons.json'),
        max_attempts=MAX_ATTEMPTS,check_facts_per_packet=4,human_accepted=False,
        authorization='Owner approved recovery and continuation within the original100USD total; all previous usage retained')
    write(OUT/'manifest.json',manifest)
    (OUT/'code').mkdir(exist_ok=True)
    shutil.copyfile(__file__,OUT/'code/resume_person_occupations.py')
    print(json.dumps(dict(parents=len(parents),retained=len(retained),new_units=len(new),spent=manifest['baseline_spend_usd'])),flush=True)


def frozen():
    m=read(OUT/'manifest.json');p.frozen();stopped_base()
    for actual,expected in ((Path(__file__),m['script_sha256']),(MAIN/'scripts/claude_api.py',m['client_sha256']),
        (BASE/'code/person_occupations.py',m['frozen_reader_sha256']),(BASE/'run-manifest.json',m['original_manifest_sha256']),
        (BASE/'persons.json',m['persons_sha256'])):
        if p.sha(actual)!=expected:raise ValueError('Changed frozen recovery dependency '+str(actual))
    return m


def draft_packet(m,job):
    entry=next((r for r in m['retained'] if r['job_id']==job),None)
    if entry:
        file=BASE/'draft/inputs'/(job+'.json');expected=entry['input_sha256']
    else:
        entry=next(r for r in m['new_drafts'] if r['job_id']==job)
        file=path('draft','inputs',job);expected=entry['sha256']
    if p.sha(file)!=expected:raise ValueError('Changed draft packet '+job)
    return read(file)


def result_file(m,phase,job):
    retained=next((r for r in m['retained'] if r['job_id']==job),None) if phase=='draft' else None
    if retained:
        checked=BASE/'draft/checked'/(job+'.json');raw=BASE/'draft/raw'/retained['raw_file']
        if p.sha(checked)!=retained['checked_sha256'] or p.sha(raw)!=retained['raw_sha256']:
            raise ValueError('Changed retained paid result '+job)
        result=p.result_file('draft',job,draft_packet(m,job))
        assert p.digest(result['answer'])==retained['answer_sha256'];return result
    result=read(path(phase,'checked',job));packet=packet_for(m,phase,job)
    assert result['input_sha256']==p.sha(path(phase,'inputs',job))
    assert not p.validate(result['answer'],packet,phase)
    raw=read(path(phase,'raw',job+f"-{result['attempt']}-message"))
    assert raw['model']==p.MODEL and raw['stop_reason']=='end_turn' and raw['id']==result['message_id']
    assert json.loads(''.join(b['text'] for b in raw['content'] if b['type']=='text'))==result['answer']
    return result


def draft_ready(m,job):
    return any(r['job_id']==job for r in m['retained']) or path('draft','checked',job).exists()


def check_packets(packet,answer):
    facts=p.facts_from_draft(answer);rows=[]
    for offset in range(0,len(facts),4):
        unit=copy.deepcopy(packet);unit['job_id']=packet['job_id']+f'-v{offset//4:02d}'
        unit.update(draft_job=packet['job_id'],draft_sha256=p.digest(answer),draft_facts=facts[offset:offset+4],fact_offset=offset)
        unit['persons']=[r for r in unit['persons'] if r['slug'] in {f['person'] for f in unit['draft_facts']}]
        rows.append(unit)
    assert [f['id'] for r in rows for f in r['draft_facts']]==[f['id'] for f in facts]
    return rows


def make_checks(m):
    entries=[]
    for parent in m['parents']:
        for job in parent['units']:
            if not draft_ready(m,job):continue
            result=result_file(m,'draft',job)
            for packet in check_packets(draft_packet(m,job),result['answer']):
                file=path('check','inputs',packet['job_id'])
                if file.exists():
                    if read(file)!=packet:raise ValueError('Changed verifier input')
                else:write(file,packet)
                entries.append(dict(job_id=packet['job_id'],draft_job=job,sha256=p.sha(file)))
    return entries


def packet_for(m,phase,job):
    if phase=='draft':return draft_packet(m,job)
    file=path('check','inputs',job);packet=read(file)
    draft=result_file(m,'draft',packet['draft_job'])
    expected=next(r for r in check_packets(draft_packet(m,packet['draft_job']),draft['answer']) if r['job_id']==job)
    if packet!=expected:raise ValueError('Verifier subset is not the exact draft partition')
    return packet


def feedback(packet,previous):
    if not previous:return None
    rows={v['key']:v for v in packet['verses']};addresses=set()
    answer=previous.get('answer')
    if not isinstance(answer,dict):answer={}
    def seq(value):return value if isinstance(value,list) else []
    findings=[f for r in seq(answer.get('persons')) if isinstance(r,dict) for f in seq(r.get('findings')) if isinstance(f,dict)]
    findings += [v['finding'] for v in seq(answer.get('verdicts')) if isinstance(v,dict) and isinstance(v.get('finding'),dict)]
    for finding in findings:
        for quote in seq(finding.get('evidence'))+seq(finding.get('identity_evidence')):
            if not isinstance(quote,dict) or not isinstance(quote.get('verse'),str):continue
            verse=rows.get(quote.get('verse'))
            if verse:addresses.update(tuple(a) for a in verse.get('canonicalAddresses',[verse.get('canonical')]) if a)
    witnesses=[dict(verse=v['key'],body=v['body']) for v in packet['verses']
        if v['key'].startswith(('BHSA:','NESTLE1904:')) and any(tuple(a) in addresses for a in v.get('canonicalAddresses',[v.get('canonical')]) if a)]
    return dict(errors=previous['errors'],held_original_verses_to_recopy=witnesses,
        instruction='Copy actual contiguous source characters, including marks. A supported role needs an original focus witness. If the profession is not established in focus, keep it for review rather than inventing proof. Keep reasons concise.')


def request(m,phase,job):
    prior=attempts(phase,job);attempt=len(prior)+1
    if attempt>MAX_ATTEMPTS:return None
    packet=packet_for(m,phase,job)
    previous=prior[-1] if prior else None
    if previous is None and phase=='draft':
        entry=next(r for r in m['new_drafts'] if r['job_id']==job)
        old=sorted((read(f) for f in (BASE/'draft/attempts').glob(entry['parent']+'-[0-9].json')),key=lambda r:r['attempt'])
        previous=old[-1] if old else None
    params=p.parameters(packet,phase,feedback(packet,previous))
    size=len(packet['persons']) if phase=='draft' else len(packet['draft_facts'])
    params['max_tokens']=32000 if prior else min(32000,(6000+size*2200) if phase=='draft' else (5000+size*2300))
    raw=path(phase,'raw',job+f'-{attempt}-request')
    if raw.exists():raise ValueError('Unknown request outcome '+phase+'/'+job)
    return attempt,params,raw


def store(m,client,message,phase,job,attempt,batch):
    rows=[json.loads(s) for s in client.LEDGER.read_text(encoding='utf-8').splitlines() if s.strip()] if client.LEDGER.exists() else []
    if not any(r.get('id')==message.id and r.get('label')==p.LABEL+'-'+phase for r in rows):
        client.record(p.LABEL+'-'+phase,message.model,message.usage,batch=batch,request_id=message.id)
    write(path(phase,'raw',job+f'-{attempt}-message'),message.model_dump(mode='json'))
    errors=[];answer=None
    if message.model!=p.MODEL:errors.append('actual model differs')
    if message.stop_reason!='end_turn':errors.append('stop reason '+str(message.stop_reason))
    try:answer=json.loads(client.text_of(message))
    except (TypeError,ValueError):errors.append('invalid JSON')
    if answer is not None:
        try:errors+=p.validate(answer,packet_for(m,phase,job),phase)
        except (TypeError,KeyError,ValueError,AttributeError) as exc:errors.append('malformed answer '+type(exc).__name__)
    result=dict(at=p.now(),job_id=job,phase=phase,attempt=attempt,message_id=message.id,model=message.model,
        input_sha256=p.sha(path(phase,'inputs',job)),answer=answer,errors=errors,
        estimated_usd=client.cost(message.model,message.usage,batch),usage=message.usage.model_dump(mode='json'),human_accepted=False)
    write(path(phase,'attempts',job+f'-{attempt}'),result)
    if not errors:write(path(phase,'checked',job),result)
    print(json.dumps(dict(job=job,phase=phase,valid=not errors,errors=errors[:4],usd=result['estimated_usd']),ensure_ascii=False),flush=True)
    return errors


def collect(m,client,batch):
    seen=set()
    for row in client.client().messages.batches.results(batch['batch_id']):
        if row.custom_id not in batch['entries'] or row.custom_id in seen:raise ValueError('Unexpected/duplicate batch result')
        seen.add(row.custom_id);job,attempt=batch['entries'][row.custom_id];phase=batch['phase']
        write(path(phase,'raw',job+f'-{attempt}-batch-result'),row.model_dump(mode='json'))
        if row.result.type=='succeeded':store(m,client,row.result.message,phase,job,attempt,True)
        else:write(path(phase,'attempts',job+f'-{attempt}'),dict(job_id=job,attempt=attempt,errors=[row.result.type],at=p.now()))
    if seen!=set(batch['entries']):raise ValueError('Batch results omitted submitted requests; preserve reservation')


def eligible(m,phase,checks):
    entries=m['new_drafts'] if phase=='draft' else checks
    return [e['job_id'] for e in entries if not path(phase,'checked',e['job_id']).exists() and len(attempts(phase,e['job_id']))<MAX_ATTEMPTS]


def pilot():
    m=frozen()
    with exclusive():
        stopped_base()
        pilot_idle()
        client=p.api()
        targets=[]
        for prefix in ('KJV-011-002-00','KJV-040-','KJV-044-'):
            match=next((r['job_id'] for r in m['new_drafts'] if r['job_id'].startswith(prefix)),None)
            if match and match not in targets:targets.append(match)
        assert len(targets)==3
        for job in targets:
            work=[('draft',job)]
            while work:
                phase,unit=work.pop(0)
                if not path(phase,'checked',unit).exists():
                    req=request(m,phase,unit)
                    if req is None:raise ValueError('Pilot attempts exhausted; inspect before release')
                    attempt,params,raw=req;receipt=p.reserve([(unit,params)],phase,False)
                    if receipt['spent_usd']-m['baseline_spend_usd']+receipt['reserved_usd']>PILOT_CAP:raise ValueError('Pilot incremental budget exhausted')
                    write(raw,params)
                    with client.client().messages.stream(**params) as stream:message=stream.get_final_message()
                    if store(m,client,message,phase,unit,attempt,False):raise ValueError('Pilot invalid: inspect and retry within cap')
                if phase=='draft':
                    make_checks(m)
                    work.extend(('check',r['job_id']) for r in check_packets(draft_packet(m,unit),result_file(m,'draft',unit)['answer']))
        write(OUT/'pilot-results.json',dict(at=p.now(),targets=targets,manifest_sha256=p.sha(OUT/'manifest.json'),human_accepted=False))
        report()


def report(reason=None):
    m=frozen();checks=make_checks(m);people=read(BASE/'persons.json')
    outputs={r['slug']:dict(person=r['slug'],findings=[],needs_review=[],not_occupations=[],jobs_seen=[]) for r in people}
    parent_complete=0;completed_parents=set();draft_complete=0;check_complete=0;draft_findings=0;terminal=[]
    checked_ids={r['job_id'] for r in checks if path('check','checked',r['job_id']).exists()}
    for parent in m['parents']:
        ready=True
        for unit in parent['units']:
            if not draft_ready(m,unit):
                ready=False
                if len(attempts('draft',unit))>=MAX_ATTEMPTS:terminal.append(dict(phase='draft',job=unit,errors=attempts('draft',unit)[-1]['errors']))
                continue
            draft=result_file(m,'draft',unit);draft_complete+=1
            for person in draft['answer']['persons']:
                target=outputs[person['person']];target['jobs_seen'].append(parent['job_id'])
                draft_findings+=len(person['findings'])
                for field in ('needs_review','not_occupations'):target[field]+=[dict(v,job_id=unit) for v in person[field]]
            for check in check_packets(draft_packet(m,unit),draft['answer']):
                job=check['job_id']
                if job not in checked_ids:
                    ready=False
                    if len(attempts('check',job))>=MAX_ATTEMPTS:terminal.append(dict(phase='check',job=job,errors=attempts('check',job)[-1]['errors']))
                    continue
                result=result_file(m,'check',job);check_complete+=1;facts={r['id']:r for r in check['draft_facts']}
                for verdict in result['answer']['verdicts']:
                    fact=facts[verdict['id']];target=outputs[fact['person']]
                    if verdict['verdict'] in ('accept','correct'):
                        target['findings'].append(dict(verdict['finding'],job_id=unit,parent_job=parent['job_id'],
                            draft_message_id=draft['message_id'],check_message_id=result['message_id'],model=p.MODEL,human_accepted=False))
                    else:target['needs_review'].append(dict(kind=verdict['verdict'],draft_finding=fact['finding'],reason_uk=verdict['reason_uk'],job_id=unit))
        parent_complete+=ready
        if ready:completed_parents.add(parent['job_id'])
    zero=set(read(BASE/'run-manifest.json')['zero_packet_persons'])
    expected=collections.defaultdict(set)
    for parent in m['parents']:
        for person in parent['persons']:expected[person].add(parent['job_id'])
    for target in outputs.values():
        groups={}
        for finding in target['findings']:
            key=(finding['category'],finding['role_key'])
            groups.setdefault(key,dict(category=key[0],role_key=key[1],contexts=[],human_accepted=False))['contexts'].append(finding)
        target['occupations']=list(groups.values())
        target['jobs_seen']=sorted(set(target['jobs_seen']))
        target['coverage_status']='no_indexed_passage' if target['person'] in zero else 'complete' if expected[target['person']]<=completed_parents else 'in_progress'
    status=dict(at=p.now(),task='TSK-1250',model=p.MODEL,budget_usd=p.CAP,spent_usd=round(sum(p.budget().values()),6),
        stage_spend_usd=p.budget(),original_jobs=len(m['parents']),original_jobs_fully_checked=parent_complete,
        retained_draft_jobs=len(m['retained']),new_draft_units=len(m['new_drafts']),
        draft_units_complete=draft_complete,check_units=len(checks),check_units_complete=check_complete,
        draft_findings=draft_findings,checked_findings=sum(len(r['findings']) for r in outputs.values()),
        persons=len(outputs),zero_indexed_passage=len(zero),
        terminal_failures=terminal,complete=parent_complete==len(m['parents']),stop_reason=reason,
        human_accepted=False,live_corpus_changed=False,budget_scope='All v1/v2/v3 task labels including pilots and all failures')
    write(OUT/'status.json',status);write(OUT/'results.json',dict(summary=status,persons=list(outputs.values())))
    print(json.dumps(status,ensure_ascii=False),flush=True);return status


def cancel_batch(client,batch_id):
    remote=client.client().messages.batches.retrieve(batch_id)
    if remote.processing_status not in ('ended','canceling'):
        client.client().messages.batches.cancel(batch_id)


def submit(client,state,statefile,active,requests):
    if (OUT/'stop.json').exists():
        for job,attempt in active['entries'].values():
            write(path(active['phase'],'attempts',job+f'-{attempt}'),dict(job_id=job,attempt=attempt,
                errors=['cancelled before submission'],submitted=False,at=p.now()))
        active.update(status='collected',not_submitted=True);write(statefile,state);return False
    batch_id=client.batch_submit(requests,label=p.LABEL+'-'+active['phase'])
    active.update(status='processing',batch_id=batch_id);write(statefile,state)
    if (OUT/'stop.json').exists():
        cancel_batch(client,batch_id);active['cancellation_requested_at']=p.now();write(statefile,state);return False
    return True


def run():
    m=frozen();gate=read(OUT/'generation-readiness.json')
    assert gate['generation_gate']=='proceed' and gate['human_accepted'] is False and gate['manifest_sha256']==p.sha(OUT/'manifest.json')
    with exclusive():
        stopped_base()
        client=p.api();statefile=OUT/'checkpoint.json'
        state=read(statefile) if statefile.exists() else dict(batches=[],next_phase='check')
        p.cli('claim','TSK-1250','--agent','sonnet-occupations','--ttl','30m','--note','Occupation v3 recovery, unchanged100USD total cap')
        renewed=time.monotonic()
        try:
            while True:
                if time.monotonic()-renewed>600:
                    p.cli('claim','TSK-1250','--agent','sonnet-occupations','--ttl','30m');renewed=time.monotonic()
                if (OUT/'stop.json').exists():report('operator_stop');return
                active=next((b for b in state['batches'] if b['status']!='collected'),None)
                if active:
                    if not active.get('batch_id'):raise ValueError('Unknown submission outcome; inspect provider ledger before resubmission')
                    remote=client.client().messages.batches.retrieve(active['batch_id'])
                    if remote.processing_status!='ended':time.sleep(45);continue
                    collect(m,client,active);active.update(status='collected',collected_at=p.now());write(statefile,state)
                    report();p.cli('services','run','backup');continue
                checks=make_checks(m);requests=[];entries={};paths=[];receipt=None;selected=None
                for phase in (state['next_phase'],'draft' if state['next_phase']=='check' else 'check'):
                    for job in eligible(m,phase,checks)[:WAVE]:
                        attempt,params,raw=request(m,phase,job);custom=f'{job}-{phase}-{attempt}'
                        try:reservation=p.reserve(requests+[(custom,params)],phase,True)
                        except ValueError as exc:
                            if str(exc)!='Occupation budget reservation refused':raise
                            break
                        requests.append((custom,params));entries[custom]=[job,attempt];paths.append(raw);receipt=reservation
                    if requests:selected=phase;break
                if not requests:
                    pending=eligible(m,'draft',checks)+eligible(m,'check',checks)
                    status=report('budget_reservation_limit' if pending else 'finished')
                    p.cli('services','run','backup')
                    if not pending:p.cli('set','TSK-1250','--status','review','--evidence','executed')
                    return
                if (OUT/'stop.json').exists():report('operator_stop');return
                active=dict(at=p.now(),phase=selected,status='submitting',entries=entries,reservation=receipt)
                state['batches'].append(active);state['next_phase']='check' if selected=='draft' else 'draft';write(statefile,state)
                for raw,(_,params) in zip(paths,requests):write(raw,params)
                if not submit(client,state,statefile,active,requests):report('operator_stop');return
                print(json.dumps(dict(submitted=active['batch_id'],phase=selected,requests=len(requests),reservation=receipt)),flush=True)
        except Exception as exc:
            write(OUT/'controller-error.json',dict(at=p.now(),type=type(exc).__name__,message=str(exc)));raise
        finally:p.cli('release','TSK-1250','--agent','sonnet-occupations',check=False)


def cancel():
    write(OUT/'stop.json',dict(at=p.now(),source='operator cancellation'))
    client=p.api()
    deadline=time.monotonic()+55
    while True:
        state=read(OUT/'checkpoint.json') if (OUT/'checkpoint.json').exists() else dict(batches=[])
        for batch in state['batches']:
            if batch['status']!='collected' and batch.get('batch_id'):cancel_batch(client,batch['batch_id'])
        unknown=[b for b in state['batches'] if b['status']=='submitting' and not b.get('batch_id')]
        if not unknown:return
        if time.monotonic()>deadline:raise ValueError('Submission outcome still unknown; preserve checkpoint and reconcile provider before resume')
        time.sleep(1)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=['prepare','pilot','report','run','cancel'])
    getattr(__import__(__name__),parser.parse_args().mode)()
