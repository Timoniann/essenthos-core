"""Budgeted source-bound occupation readings; outputs never load themselves into the corpus."""
import argparse
import collections
import datetime as dt
import hashlib
import importlib.util
import json
import os
import re
import shutil
import subprocess
import time
import unicodedata
from pathlib import Path

MAIN=Path('C:/Users/timon/Projects/Essenthos/essenthos-core')
OUT=MAIN/'Resources/Essenthos/generation/person-occupations-sonnet55-2026-10-27-v2'
MODEL='claude-sonnet-5-5'
LABEL='person-occupations-TSK-1250'
CAP=100.0
DRAFT_CAP=70.0
SYSTEM='''Read the whole supplied Scripture packet. Find the stated professions and vocational
roles of EACH requested exact person, not just famous people. Cover all requested slugs once.
Trades, offices, military roles and religious roles are separate categories. Existing descriptors,
names, index annotations, author hints and person descriptions are retrieval metadata, NEVER
independent evidence. Use supplied Scripture only; no tradition, Wikidata, guessed authorship,
outside biographical knowledge or invented Strong numbers. An empty finding means none found in
this context, not that the person had no profession.

Accept an explicit professional predicate/apposition or a clear identity chain connecting a named
member to a group explicitly practising that profession. Work done once, wealth, owning a boat,
gleaning, building by royal command, ancestry, descendants, guild membership inherited by a son,
or being a recipient is not automatically a profession. Do not infer author/scribe from writing
a letter once or its book title. Named actual writers need explicit identity verses, not hints.
Keep a job left behind as a sourced past role, not a lifetime/current claim. Plans, commands and
future promises alone do not establish a held role. A negated role is not a positive profession.
Use past only if the passage explicitly establishes that the occupation ceased. A description
of earlier work followed by a calling does not itself prove the trade ended; use unspecified.

Read the supplied BHSA for OT and NESTLE1904 for NT when available; compare the actual noun and
subject against translations. Quote the original for every supported finding when provided.
Do not make tekton more specific than its text warrants or silently equate different people with
the same name. 'The carpenter's son' does not independently name Joseph. Amos7:14 has temporal
and professional-prophet nuances: retain differences, not a false global 'never prophet'.

Literal trades and held secular/religious offices can count, including women's trades and former
roles. A prophet, apostle, priest, ruler, soldier or steward is a role in its proper category,
not necessarily a paid modern profession. An office already held in metadata still needs textual
proof here; do not drop a trade merely because the person also held an office.
An entrusted duty, including holding a group's money bag, does not by itself establish an office
or a professional appointment. Preserve such a duty in not_occupations, not office. Do not upgrade
one repeated task to a title merely to fit the four finding categories. The Greek tekton is a
craftsman; preserve its breadth instead of requiring a wood-specific carpenter trade.
Metaphorical shepherd/king for God, parable characters, generic groups and spiritual slavery are not human
jobs. Moral labels such as Judas 'a thief', social status, kinship, epithets and one-off deeds
belong in not_occupations, not trade. A literal label overlapping trade and epithet (harlot,
sorcerer) can remain needs_review; the owner has not decided its display policy.

Every supported finding needs exact contiguous quotes of professional evidence AND identity,
at least one professional witness in focus_keys; identity-only context cannot establish the job.
Copy actual edition verse keys, Hebrew/Greek letters and marks. Unicode NFC equivalent ordering
is allowed, no mark stripping, paraphrases, joined snippets or ellipses. Cite the whole essential
identity/context bridge. Source_term may be null if it cannot be copied exactly. Labels are
concise English and Ukrainian; role_key is a reusable lowercase ASCII hyphenated concept, never
a person's name. In attribution_uk state WHO attributes the role (narrator, self-description,
named speaker, opponents or crowd); preserve questions and accusations as such rather than
silently turning quoted speech into a narrator's categorical statement.
Keep genuine identity/vocation/text ambiguity in needs_review. Return Ukrainian
reasons. These are model readings for review, never human acceptance or live data admission.'''
CHECK_SYSTEM=SYSTEM+'''
You are an independent verifier. Proposed draft facts are hypotheses, not evidence. Decide every
provided fact ID exactly once: accept, correct, reject or uncertain. Read the actual original
language and full context, check subject/identity and professional vs moral/action categories.
For every verdict return the finding object (copy the draft if rejecting); accepted/corrected
findings must satisfy all evidence rules. Do not invent unrelated new facts in this check.
Fix over-specific labels, wrong category and missing identity links; use uncertain if the text
does not settle the claim. Give a concise Ukrainian reason. Never repair ambiguity by guessing.'''

Q={'type':'object','additionalProperties':False,'properties':{'verse':{'type':'string'},'quote':{'type':'string'}},'required':['verse','quote']}
F={'type':'object','additionalProperties':False,'properties':{
    'category':{'type':'string','enum':['trade','office','military_role','religious_role']},
    'role_key':{'type':'string'},'label_en':{'type':'string'},'label_uk':{'type':'string'},
    'source_term':{'type':['string','null']},
    'attribution_uk':{'type':'string'},
    'basis':{'type':'string','enum':['explicit','context_identity']},
    'time_scope':{'type':'string','enum':['unspecified','past','present_in_passage']},
    'evidence':{'type':'array','items':Q,'minItems':1},'identity_evidence':{'type':'array','items':Q,'minItems':1},
    'reason_uk':{'type':'string'}}}
F['required']=list(F['properties'])
OBS={'type':'object','additionalProperties':False,'properties':{
    'label':{'type':'string'},'kind':{'type':'string'},'reason_uk':{'type':'string'},
    'evidence':{'type':'array','items':Q,'minItems':1}},'required':['label','kind','reason_uk','evidence']}
PERSON={'type':'object','additionalProperties':False,'properties':{
    'person':{'type':'string'},'findings':{'type':'array','items':F},
    'needs_review':{'type':'array','items':OBS},'not_occupations':{'type':'array','items':OBS}},
    'required':['person','findings','needs_review','not_occupations']}
DRAFT={'type':'object','additionalProperties':False,'properties':{'job_id':{'type':'string'},
    'persons':{'type':'array','items':PERSON}},'required':['job_id','persons']}
VERDICT={'type':'object','additionalProperties':False,'properties':{'id':{'type':'string'},
    'verdict':{'type':'string','enum':['accept','correct','reject','uncertain']},'finding':F,
    'reason_uk':{'type':'string'}},'required':['id','verdict','finding','reason_uk']}
CHECK={'type':'object','additionalProperties':False,'properties':{'job_id':{'type':'string'},
    'verdicts':{'type':'array','items':VERDICT}},'required':['job_id','verdicts']}


def now():return dt.datetime.now(dt.timezone.utc).isoformat()
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def digest(value):return hashlib.sha256(json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def dump(path,value):
    path.parent.mkdir(parents=True,exist_ok=True);temp=path.with_suffix(path.suffix+'.tmp')
    temp.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for attempt in range(6):
        try:temp.replace(path);return
        except PermissionError:
            if attempt==5:raise
            time.sleep(.05*2**attempt)
def nfc(value):return unicodedata.normalize('NFC',value)
def quoted(quote,body):return isinstance(quote,str) and len(quote.strip())>=4 and nfc(quote) in nfc(body)
def phase_path(phase,kind,job):return OUT/phase/kind/(job+'.json')
def cli(*args,check=True):
    return subprocess.run(['avioniq',*args],cwd=MAIN.parent,shell=os.name=='nt',check=check)


def api():
    spec=importlib.util.spec_from_file_location('occupation_owner_client',MAIN/'scripts/claude_api.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    module._client=module.client().with_options(max_retries=0)
    return module


def quotes_ok(quotes,packet):
    verses={v['key']:v for v in packet['verses']};errors=[]
    if not isinstance(quotes,list) or not quotes:return ['missing evidence']
    for q in quotes:
        if not isinstance(q,dict) or set(q)!={'verse','quote'} or q.get('verse') not in verses:
            errors.append('unknown quote key');continue
        if not quoted(q.get('quote'),verses[q['verse']]['body']):errors.append('nonverbatim '+q['verse'])
    return errors


def finding_errors(finding,packet):
    if not isinstance(finding,dict) or set(finding)!=set(F['properties']):return ['finding shape']
    errors=[]
    for field in ('category','basis','time_scope'):
        if finding[field] not in F['properties'][field]['enum']:errors.append('invalid '+field)
    if not re.fullmatch('[a-z][a-z0-9-]{1,63}',finding['role_key']):errors.append('invalid role_key')
    for field in ('label_en','label_uk','attribution_uk','reason_uk'):
        if not isinstance(finding[field],str) or not finding[field].strip():errors.append('missing '+field)
    errors+=quotes_ok(finding['evidence'],packet)+quotes_ok(finding['identity_evidence'],packet)
    evidence=finding['evidence']
    if not any(q.get('verse') in packet['focus_keys'] for q in evidence if isinstance(q,dict)):errors.append('no focus profession witness')
    originals={v['key'] for v in packet['verses'] if v['key'].startswith(('BHSA:','NESTLE1904:')) and v['key'] in packet['focus_keys']}
    if originals and not any(q.get('verse') in originals for q in evidence if isinstance(q,dict)):errors.append('original language witness missing')
    term=finding['source_term']
    if term is not None and not any(isinstance(q,dict) and isinstance(q.get('quote'),str) and nfc(term) in nfc(q['quote']) for q in evidence):errors.append('source_term not quoted')
    return errors


def validate(answer,packet,phase):
    errors=[]
    if not isinstance(answer,dict) or answer.get('job_id')!=packet['job_id']:return ['wrong answer/job ID']
    if phase=='draft':
        if set(answer)!={'job_id','persons'} or not isinstance(answer['persons'],list):return ['draft shape']
        wanted=[p['slug'] for p in packet['persons']]
        actual=[p.get('person') for p in answer['persons'] if isinstance(p,dict)]
        if collections.Counter(wanted)!=collections.Counter(actual):errors.append('missing/extra/duplicate persons')
        for person in answer['persons']:
            if not isinstance(person,dict) or set(person)!=set(PERSON['properties']):errors.append('person shape');continue
            seen=set()
            for finding in person['findings']:
                errors += [person['person']+': '+e for e in finding_errors(finding,packet)]
                key=(finding.get('category'),finding.get('role_key'))
                if key in seen:errors.append('duplicate role for '+person['person'])
                seen.add(key)
            for kind in ('needs_review','not_occupations'):
                for observation in person[kind]:
                    if set(observation)!=set(OBS['properties']):errors.append('observation shape');continue
                    errors+=quotes_ok(observation['evidence'],packet)
                    if not observation['reason_uk']:errors.append('observation reason missing')
    else:
        if set(answer)!={'job_id','verdicts'} or not isinstance(answer['verdicts'],list):return ['check shape']
        expected={f['id']:f for f in packet['draft_facts']}
        if collections.Counter(expected.keys())!=collections.Counter(v.get('id') for v in answer['verdicts']):errors.append('missing/extra/duplicate fact IDs')
        for verdict in answer['verdicts']:
            if set(verdict)!=set(VERDICT['properties']) or verdict.get('verdict') not in VERDICT['properties']['verdict']['enum']:
                errors.append('verdict shape');continue
            if not verdict['reason_uk']:errors.append('verdict reason missing')
            if verdict['verdict'] in ('accept','correct'):errors+=finding_errors(verdict['finding'],packet)
    return errors


def parameters(packet,phase,feedback=None):
    body=json.dumps(packet,ensure_ascii=False,separators=(',',':'))
    if feedback:body+='\nPrevious structural errors; recopy the actual supplied evidence, never invent it:\n'+json.dumps(feedback,ensure_ascii=False)
    size=len(packet['persons']) if phase=='draft' else len(packet['draft_facts'])
    return {'model':MODEL,'max_tokens':min(32000,4000+size*(650 if phase=='draft' else 1400)),
        'system':[{'type':'text','text':SYSTEM if phase=='draft' else CHECK_SYSTEM,'cache_control':{'type':'ephemeral'}}],
        'messages':[{'role':'user','content':body}],
        'output_config':{'effort':'high','format':{'type':'json_schema','schema':DRAFT if phase=='draft' else CHECK}}}


def model_packet(job):
    return {'job_id':job['job_id'],'persons':[{k:p.get(k) for k in ('slug','name','identity_hint','aliasOf')} for p in job['persons']],
        'focus_keys':job['focus_keys'],
        'verses':[{k:v[k] for k in ('key','body','canonical','canonicalAddresses','role') if k in v} for v in job['verses']],
        'source_metadata':{k:job['source_metadata'].get(k) for k in ('primary_text','printed_focus','canonical_focus_chapters','identity_hint_is_not_authorship_proof','identity_bridges')}}


def prepare():
    source=read(OUT/'source-manifest.json');policy=read(OUT/'occupation-policy.json');pilot=read(OUT/'pilot-cases.json')
    assert sha(OUT/'source-manifest.json')=='1a1fa99342bf811cd238d65afece125cc15095f4e565bdad17dd4eb966e121d0'
    assert sha(OUT/'jobs.jsonl')=='0cb172d19bb57c6c35bbd76e0f116c6c343563bdca898e5cc5e9fbc01c8a52d8'
    jobs=[json.loads(line) for line in (OUT/'jobs.jsonl').read_text(encoding='utf-8').splitlines() if line.strip()]
    persons=read(OUT/'persons.json')
    if isinstance(persons,dict):persons=persons['persons']
    ids={p['slug'] for p in persons};seen=set();entries=[];pilot_entries=[];pilot_chapters=set()
    profiles={p['slug']:p for p in persons}
    for job in jobs:
        packet=model_packet(job)
        assert packet['persons'] and set(p['slug'] for p in packet['persons'])<=ids
        assert len({p['slug'] for p in packet['persons']})==len(packet['persons'])
        assert set(packet['focus_keys'])<={v['key'] for v in packet['verses']}
        seen.update(p['slug'] for p in packet['persons'])
        path=OUT/'draft/inputs'/(job['job_id']+'.json')
        if path.exists():
            if read(path)!=packet:raise ValueError('Frozen draft input changed')
        else:dump(path,packet)
        entries.append({'job_id':job['job_id'],'sha256':sha(path),'persons':[p['slug'] for p in packet['persons']]})
        focus={tuple(c) for c in job['source_metadata']['canonical_focus_chapters']}
        selected=[c for c in pilot['cases'] if focus.intersection(tuple(x) for x in c['focus_canonical_chapters'])]
        if selected and not focus<=pilot_chapters:
            pilot_chapters.update(focus)
            wanted={c['person'] for c in selected};subset=[p for p in packet['persons'] if p['slug'] in wanted]
            missing=wanted-{p['slug'] for p in subset}
            for slug in missing:
                case=next(c for c in selected if c['person']==slug)
                # Explicit pilot-only identity probe, never passed off as an indexed reference.
                profile=case['person_profile'];subset.append({'slug':slug,'name':profile['name'],
                    'identity_hint':profile.get('distinguisher'),'aliasOf':profile.get('aliasOf')})
            probe=dict(packet,job_id='pilot-'+job['job_id'],persons=subset)
            probe['source_metadata']=dict(packet['source_metadata'],pilot_only=True,outside_selected_packet_persons=sorted(missing))
            probe_path=OUT/'draft/inputs'/(probe['job_id']+'.json')
            if probe_path.exists():
                if read(probe_path)!=probe:raise ValueError('Frozen pilot changed')
            else:dump(probe_path,probe)
            pilot_entries.append({'job_id':probe['job_id'],'sha256':sha(probe_path),'persons':[p['slug'] for p in subset]})
    assert seen<=ids and len({j['job_id'] for j in entries+pilot_entries})==len(entries+pilot_entries)
    manifest={'at':now(),'task':'TSK-1250','model':MODEL,'budget_usd':CAP,'draft_subcap_usd':DRAFT_CAP,
        'jobs':entries,'pilot_jobs':pilot_entries,'persons':len(ids),'indexed_persons':len(seen),'zero_packet_persons':sorted(ids-seen),
        'source_sha256':{n:sha(OUT/n) for n in ('source-manifest.json','persons.json','jobs.jsonl','occupation-policy.json','pilot-cases.json')},
        'script_sha256':sha(Path(__file__)),'client_sha256':sha(MAIN/'scripts/claude_api.py'),
        'draft_system_sha256':digest(SYSTEM),'check_system_sha256':digest(CHECK_SYSTEM),
        'draft_schema_sha256':digest(DRAFT),'check_schema_sha256':digest(CHECK),'human_accepted':False}
    path=OUT/'run-manifest.json'
    if path.exists():
        old=read(path)
        for k in manifest.keys()-{'at'}:
            if old[k]!=manifest[k]:raise ValueError('Frozen manifest changed '+k)
    else:dump(path,manifest)
    (OUT/'code').mkdir(exist_ok=True)
    shutil.copy2(__file__,OUT/'code/person_occupations.py')
    shutil.copy2(MAIN/'scripts/claude_api.py',OUT/'code/claude_api.py')
    print(json.dumps({'jobs':len(entries),'pilot_jobs':len(pilot_entries),'persons':len(ids),'zero_packets':len(ids-seen),
        'max_packet_persons':max(len(j['persons']) for j in entries)}),flush=True)


def frozen():
    m=read(OUT/'run-manifest.json')
    if sha(Path(__file__))!=m['script_sha256'] or sha(MAIN/'scripts/claude_api.py')!=m['client_sha256']:raise ValueError('Frozen code/client changed')
    for n,h in m['source_sha256'].items():
        if sha(OUT/n)!=h:raise ValueError('Frozen source changed '+n)
    return m


def budget():
    path=MAIN/'.claude-api/usage.jsonl';rows=[]
    if path.exists():rows=[json.loads(line) for line in path.read_text(encoding='utf-8').splitlines() if line.strip()]
    unique={}
    for r in rows:
        if r.get('label') not in (LABEL+'-draft',LABEL+'-check'):continue
        if r.get('usd') is None or not r.get('id'):raise ValueError('Unknown occupation usage price/identity')
        key=(r['label'],r['id'])
        if key in unique and unique[key]['usd']!=r['usd']:raise ValueError('Conflicting duplicate usage')
        unique[key]=r
    return {phase:sum(r['usd'] for r in unique.values() if r['label']==LABEL+'-'+phase) for phase in ('draft','check')}


def reserve(requests,phase,batch,pilot=False):
    costs=budget();estimate=sum((len(json.dumps(p,ensure_ascii=False).encode())*2.5+p['max_tokens']*10)/1e6 for _,p in requests)/(2 if batch else 1)
    total=sum(costs.values())
    if total+estimate>CAP or (phase=='draft' and costs['draft']+estimate>DRAFT_CAP) or (pilot and total+estimate>10):
        raise ValueError('Occupation budget reservation refused')
    return {'at':now(),'spent_usd':total,'phase_spent_usd':costs[phase],'reserved_usd':estimate,'cap_usd':CAP}


def packet_for(job,phase):
    path=phase_path(phase,'inputs',job)
    packet=read(path)
    if phase=='draft':
        m=read(OUT/'run-manifest.json');entry=next(j for j in m['jobs']+m['pilot_jobs'] if j['job_id']==job)
        assert sha(path)==entry['sha256']
    else:
        draft=read(phase_path('draft','checked',job));assert packet['draft_sha256']==digest(draft['answer'])
        assert packet['draft_facts']==facts_from_draft(draft['answer'])
    return packet


def facts_from_draft(answer):
    facts=[]
    for person in answer['persons']:
        for finding in person['findings']:
            key={'person':person['person'],'category':finding['category'],'role_key':finding['role_key']}
            facts.append({'id':digest(key)[:24],'person':person['person'],'finding':finding})
    assert len({f['id'] for f in facts})==len(facts)
    return facts


def make_check(job):
    draft=read(phase_path('draft','checked',job));packet=packet_for(job,'draft');facts=facts_from_draft(draft['answer'])
    checked=dict(packet,draft_facts=facts,draft_sha256=digest(draft['answer']))
    path=phase_path('check','inputs',job)
    if path.exists() and read(path)!=checked:raise ValueError('Frozen verifier input changed')
    dump(path,checked)
    if not facts:dump(phase_path('check','checked',job),{'job_id':job,'automatic_empty':True,'draft_sha256':checked['draft_sha256'],'human_accepted':False})


def store(client,message,job,attempt,phase,batch):
    ledger=client.LEDGER;rows=[json.loads(s) for s in ledger.read_text(encoding='utf-8').splitlines() if s.strip()] if ledger.exists() else []
    if not any(r.get('id')==message.id and r.get('label')==LABEL+'-'+phase for r in rows):
        client.record(LABEL+'-'+phase,message.model,message.usage,batch=batch,request_id=message.id)
    dump(phase_path(phase,'raw',job+f'-{attempt}-message'),message.model_dump(mode='json'))
    errors=[];answer=None
    if message.model!=MODEL:errors.append('actual model differs')
    if message.stop_reason!='end_turn':errors.append('stop reason '+str(message.stop_reason))
    try:answer=json.loads(client.text_of(message))
    except (TypeError,ValueError):errors.append('invalid JSON')
    if answer is not None:errors+=validate(answer,packet_for(job,phase),phase)
    result={'at':now(),'job_id':job,'attempt':attempt,'phase':phase,'model':message.model,'message_id':message.id,
        'input_sha256':sha(phase_path(phase,'inputs',job)),'usage':message.usage.model_dump(mode='json'),
        'estimated_usd':client.cost(message.model,message.usage,batch),'batch':batch,'answer':answer,'errors':errors,'human_accepted':False}
    dump(phase_path(phase,'attempts',job+f'-{attempt}'),result)
    if not errors:dump(phase_path(phase,'checked',job),result)
    print(json.dumps({'phase':phase,'job':job,'valid':not errors,'errors':errors[:5],'usd':result['estimated_usd']},ensure_ascii=False),flush=True)
    return errors


def request(job,phase):
    previous=list((OUT/phase/'attempts').glob(job+'-[0-9].json'));attempt=len(previous)+1
    if attempt>3:raise ValueError('Attempts exhausted '+phase+'/'+job)
    old=read(phase_path(phase,'attempts',job+f'-{attempt-1}')) if previous else None
    params=parameters(packet_for(job,phase),phase,old['errors'] if old else None)
    path=phase_path(phase,'raw',job+f'-{attempt}-request')
    if path.exists():raise ValueError('Unknown prior request outcome, inspect checkpoint before retry')
    return attempt,params,path


def pilot():
    lock=OUT/'controller.lock';fd=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY);os.write(fd,str(os.getpid()).encode());os.close(fd)
    try:
        m=frozen();client=api()
        for entry in m['pilot_jobs']:
            job=entry['job_id']
            for phase in ('draft','check'):
                if phase=='check':make_check(job)
                if phase_path(phase,'checked',job).exists():continue
                attempt,params,path=request(job,phase);receipt=reserve([(job,params)],phase,False,True)
                dump(path,params);dump(OUT/'pilot-reservation.json',receipt)
                with client.client().messages.stream(**params) as stream:message=stream.get_final_message()
                if store(client,message,job,attempt,phase,False):raise ValueError('Pilot structural failure: inspect raw and retry within cap')
        report()
    finally:lock.unlink(missing_ok=True)


def collect(client,batch):
    for row in client.client().messages.batches.results(batch['batch_id']):
        job,attempt=batch['entries'][row.custom_id];phase=batch['phase']
        dump(phase_path(phase,'raw',job+f'-{attempt}-batch-result'),row.model_dump(mode='json'))
        if row.result.type=='succeeded':store(client,row.result.message,job,attempt,phase,True)
        else:dump(phase_path(phase,'attempts',job+f'-{attempt}'),{'job_id':job,'attempt':attempt,'errors':[row.result.type],'at':now()})


def run():
    m=frozen();gate=read(OUT/'pilot-semantic-review.json')
    assert gate['generation_gate']=='proceed' and gate['human_accepted'] is False and gate['manifest_sha256']==sha(OUT/'run-manifest.json')
    lock=OUT/'controller.lock';fd=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY);os.write(fd,str(os.getpid()).encode());os.close(fd)
    try:
        client=api();path=OUT/'checkpoint.json';state=read(path) if path.exists() else {'batches':[]}
        cli('claim','TSK-1250','--agent','sonnet-occupations','--ttl','30m','--note','Sonnet API draft/check with100USD total cap')
        renewed=time.monotonic()
        while True:
            if time.monotonic()-renewed>600:
                cli('claim','TSK-1250','--agent','sonnet-occupations','--ttl','30m');renewed=time.monotonic()
            if (OUT/'stop.json').exists():report();return
            active=next((b for b in state['batches'] if b['status']!='collected'),None)
            if active:
                if not active.get('batch_id'):raise ValueError('Unknown submission outcome; inspect owner batch ledger')
                remote=client.client().messages.batches.retrieve(active['batch_id'])
                if remote.processing_status!='ended':time.sleep(45);continue
                collect(client,active);active.update(status='collected',collected_at=now());dump(path,state);report()
                cli('services','run','backup');continue
            draft_pending=[j['job_id'] for j in m['jobs'] if not phase_path('draft','checked',j['job_id']).exists()]
            if draft_pending:phase='draft';eligible=draft_pending
            else:
                for j in m['jobs']:make_check(j['job_id'])
                phase='check';eligible=[j['job_id'] for j in m['jobs'] if not phase_path('check','checked',j['job_id']).exists()]
            if not eligible:break
            requests=[];entries={};paths=[]
            for job in eligible[:400]:
                attempt,params,raw=request(job,phase);custom=f'{job}-{phase}-{attempt}'
                try:receipt=reserve(requests+[(custom,params)],phase,True)
                except ValueError:
                    if not requests:raise
                    break
                requests.append((custom,params));entries[custom]=[job,attempt];paths.append(raw)
            active={'at':now(),'phase':phase,'status':'submitting','reservation':receipt,'entries':entries}
            state['batches'].append(active);dump(path,state)
            for raw,(_,params) in zip(paths,requests):dump(raw,params)
            batch_id=client.batch_submit(requests,label=LABEL+'-'+phase)
            active.update(batch_id=batch_id,status='processing');dump(path,state);report()
            print(json.dumps({'submitted':batch_id,'phase':phase,'requests':len(requests),'reservation':receipt}),flush=True)
        report();cli('services','run','backup')
        cli('set','TSK-1250','--status','review','--evidence','executed')
    except Exception as exc:
        dump(OUT/'controller-error.json',{'at':now(),'type':type(exc).__name__,'message':str(exc)})
        raise
    finally:
        lock.unlink(missing_ok=True)
        cli('release','TSK-1250','--agent','sonnet-occupations',check=False)


def cancel():
    dump(OUT/'stop.json',{'at':now(),'source':'operator cancellation'})
    state=read(OUT/'checkpoint.json') if (OUT/'checkpoint.json').exists() else {'batches':[]}
    client=api()
    for batch in state['batches']:
        if batch['status']!='collected' and batch.get('batch_id'):
            remote=client.client().messages.batches.retrieve(batch['batch_id'])
            if remote.processing_status!='ended':client.client().messages.batches.cancel(batch['batch_id'])
    report()


def result_file(phase,job,packet):
    result=read(phase_path(phase,'checked',job))
    if result.get('automatic_empty'):
        assert phase=='check' and not packet['draft_facts'] and result['draft_sha256']==packet['draft_sha256'];return result
    assert result['model']==MODEL and result['input_sha256']==sha(phase_path(phase,'inputs',job))
    assert not validate(result['answer'],packet,phase)
    raw=read(phase_path(phase,'raw',job+f"-{result['attempt']}-message"))
    assert raw['id']==result['message_id'] and raw['model']==MODEL and raw['stop_reason']=='end_turn'
    assert json.loads(''.join(b['text'] for b in raw['content'] if b['type']=='text'))==result['answer']
    return result


def report():
    m=frozen();people=read(OUT/'persons.json')
    if isinstance(people,dict):people=people['persons']
    result={p['slug']:{'person':p['slug'],'jobs_seen':[],'findings':[],'needs_review':[],'not_occupations':[]} for p in people}
    completed_draft=0;completed_check=0;draft_findings=0
    for entry in m['jobs']:
        job=entry['job_id']
        if not phase_path('draft','checked',job).exists():continue
        packet=packet_for(job,'draft');draft=result_file('draft',job,packet);completed_draft+=1
        for person in draft['answer']['persons']:
            draft_findings+=len(person['findings'])
            output=result[person['person']];output['jobs_seen'].append(job)
            for field in ('needs_review','not_occupations'):
                output[field]+=[dict(v,job_id=job) for v in person[field]]
        if not phase_path('check','checked',job).exists():continue
        check_packet=packet_for(job,'check');check=result_file('check',job,check_packet);completed_check+=1
        if check.get('automatic_empty'):continue
        facts={f['id']:f for f in check_packet['draft_facts']}
        for verdict in check['answer']['verdicts']:
            fact=facts[verdict['id']];output=result[fact['person']]
            if verdict['verdict'] in ('accept','correct'):
                output['findings'].append(dict(verdict['finding'],job_id=job,draft_message_id=draft['message_id'],
                    check_message_id=check['message_id'],model=MODEL,human_accepted=False))
            else:output['needs_review'].append({'kind':verdict['verdict'],'label':fact['finding']['label_en'],
                'reason_uk':verdict['reason_uk'],'job_id':job,'draft_finding':fact['finding']})
    costs=budget();complete=completed_check==len(m['jobs'])
    for person in result.values():
        person['coverage_status']='no_indexed_passage' if person['person'] in m['zero_packet_persons'] else 'complete' if complete else 'in_progress'
        grouped={}
        for finding in person['findings']:
            key=(finding['category'],finding['role_key'])
            item=grouped.setdefault(key,{'category':key[0],'role_key':key[1],'labels_en':[],
                'labels_uk':[],'contexts':[],'human_accepted':False})
            for field,label in (('labels_en','label_en'),('labels_uk','label_uk')):
                if finding[label] not in item[field]:item[field].append(finding[label])
            item['contexts'].append(finding)
        person['occupations']=[grouped[k] for k in sorted(grouped)]
    status={'task':'TSK-1250','at':now(),'model':MODEL,'budget_usd':CAP,'spent_usd':round(sum(costs.values()),6),
        'stage_spend_usd':costs,'jobs':len(m['jobs']),'draft_complete':completed_draft,'check_complete':completed_check,
        'persons':len(result),'zero_indexed_passage':len(m['zero_packet_persons']),'complete':complete,
        'draft_findings':draft_findings,'checked_findings':sum(len(p['findings']) for p in result.values()),
        'occupation_groups':sum(len(p['occupations']) for p in result.values()),
        'human_accepted':False,'live_corpus_changed':False}
    status['pilot_jobs']=len(m['pilot_jobs'])
    status['pilot_draft_complete']=sum(phase_path('draft','checked',j['job_id']).exists() for j in m['pilot_jobs'])
    status['pilot_check_complete']=sum(phase_path('check','checked',j['job_id']).exists() for j in m['pilot_jobs'])
    status['budget_scope']='Includes v1 pilot, v2 pilot, all drafts, checks and retries under shared task labels'
    dump(OUT/'results.json',{'summary':status,'persons':list(result.values())});dump(OUT/'status.json',status)
    print(json.dumps(status),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=['prepare','pilot','run','report','cancel'])
    getattr(__import__(__name__),parser.parse_args().mode)()
