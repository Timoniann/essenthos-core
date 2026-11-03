# Historical (TSK-1452): the run folders this script names under Resources/Essenthos/generation were archived on
# 2026-11-04 to E:\EssenthosArchive\generation\<run>, with a verified manifest beside it. The script is kept as the record
# of how the run was made and no longer runs as written; to run it again, point it at a copy of the run folder, never
# at the archive itself.
"""Source-bound final disposition of the remaining presence cases, with no corpus writes."""
import argparse
import collections
import datetime as dt
import hashlib
import importlib.util
import json
import os
import shutil
import time
import unicodedata
from pathlib import Path

MAIN = Path('C:/Users/timon/Projects/Essenthos/essenthos-core')
OUT = MAIN / 'Resources/Essenthos/generation/presence-closure-2026-10-26-v2'
REVIEW = MAIN / 'Resources/Essenthos/generation/presence-refinement-2026-10-26-v1'
MODEL = 'claude-sonnet-5-5'
LABEL = 'person-presence-closure-TSK-1249'
LABELS = [LABEL, 'person-presence-TSK-1242', 'person-presence-adjudication-TSK-1244']
CAP, TOTAL_CAP = 50.0, 200.0
VERSION = 'presence-closure-v2'
CONTEXT_FILE = 'contexts-expanded.json'
SYSTEM = '''Read the supplied Bible witnesses, original disputed event, exact registered-place
identities and source research carefully. This is the final disposition of unresolved Biblical
person-presence cases, NOT a fresh whole-corpus extraction. Return one decision per case.

supported: the printed witnesses and a defensible contextual chain establish this exact person's
actual bodily presence in the exact existing target place, or burial if category=buried. At least
one quoted event witness must concern the original disputed episode (its focus_keys). A different
episode can clarify identity or place but does not prove THIS event. Original global support is
metadata, not event proof. Exact context chains and explicit parallel narratives are allowed.
closed_ambiguous: the question has been examined but identity/location/event remains uncertain,
competing, contradictory or underdetermined. Close without admitting a presence; NEVER say the
person was absent or never visited. closed_unsupported: the inspected event does not establish
this proposed visit (ownership, intention, vision, addressee, collective descendants, namesake,
adjacency only, etc.). This also never denies a visit in another event. There is no unresolved
output state: uncertainty is an explicit closed_ambiguous conclusion, not erased data.

Hebrew BHSA is an edition with its own numbering and surface text. For OT cases read it, compare
the actual Hebrew location/subject/action against translations, and quote at least one exact
BHSA witness. Keep Hebrew/Aramaic, proper-name/polysemy and textual variants explicit. Hebrew can
say city of Judah while another passage says city of David: demonstrate the parallel episode,
do not silently correct either witness. A translation making an implicit location explicit is
evidence of that translation, not proof that BHSA literally names it. BHSA may be uninformative
about a modern geographical identity. Never manufacture Hebrew evidence for an NT case.

External research can support a geographical/name/period identification, not manufacture bodily
presence. Use only provided source IDs, distinguish source statements from agent inferences, and
do not declare certainty by majority vote, familiar tradition or an unverified search snippet.
If the exact ancient referent remains disputed or an essential bridge is missing, close_ambiguous.
Missing modern coordinates alone do NOT invalidate a clear named ancient referent: a known ancient
district, room or house can be supported by Scripture even if its precise modern point is unknown. Context
may still support a broad exact registered region when the printed containment is explicit.

Only use exact existing target slugs from registered_places. A house's ownership/name is not its
owner's presence. A later king's house is not automatically Solomon's own-time palace. Near a
city is not inside it; a temple compartment, building, city and region are distinct. Creation
of a record does not prove a visit. Birth and explicitly narrated past actions can count. Burial
is separate. Do not treat visions, heaven or a literary figure as a bodily geographical visit.
For source_kind=place, resolve the ORIGINAL place_name's exact referent. Never substitute its
city, region or a whole building for an uncertain house/room merely to obtain supported. If only
the broader area is established, explain it but close this exact-site case as ambiguous.

Every decision needs nonempty quoted evidence anchoring the inspected episode, identity_context_uk
and reason_uk in Ukrainian. Supported decisions require quoted event, identity and location links
(quote roles event, identity, location; one witness may serve several roles as separate entries).
Copy exact supplied verse keys and contiguous source substrings, including raw Hebrew marks;
no paraphrases, assembled quotations, omitted accents, ellipses or invented verses. Explain all
essential bridges and competing readings. Cite BHSA in evidence for every OT case for which its
focus context is supplied. Do not claim human acceptance or external-source consensus without
support. Return concise decisions, not working notes or chains of hidden reasoning.'''

QUOTE = {'type':'object','additionalProperties':False,'properties':{
    'verse':{'type':'string'},'quote':{'type':'string'},
    'role':{'type':'string','enum':['event','identity','location','context','hebrew']}},
    'required':['verse','quote','role']}
FIELDS = {
    'case_id':{'type':'string'},
    'decision':{'type':'string','enum':['supported','closed_ambiguous','closed_unsupported']},
    'target_place':{'type':['string','null']},
    'category':{'type':'string','enum':['present','buried']},
    'reason_code':{'type':'string','enum':['stated','context_chain','textual_variant','uncertain_identity',
        'uncertain_place','source_conflict','nearby_only','ownership_only','plan_only','vision',
        'collective','wrong_identity','wrong_category','insufficient_evidence','other']},
    'reason_uk':{'type':'string'},'identity_context_uk':{'type':'string'},
    'checked_hebrew':{'type':'boolean'},
    'evidence':{'type':'array','items':QUOTE,'minItems':1},
    'external_source_ids':{'type':'array','items':{'type':'string'}}}
SCHEMA = {'type':'object','additionalProperties':False,'properties':{
    'job_id':{'type':'string'},'decisions':{'type':'array','items':{
        'type':'object','additionalProperties':False,'properties':FIELDS,'required':list(FIELDS)}}},
    'required':['job_id','decisions']}


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def dump(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix+'.tmp')
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    temp.replace(path)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def object_sha(value):
    return hashlib.sha256(json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',',':')).encode()).hexdigest()


def api():
    spec = importlib.util.spec_from_file_location('presence_owner_client',MAIN/'scripts/claude_api.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module._client = module.client().with_options(max_retries=0)
    return module


def wanted():
    disagreements = [r for r in read(REVIEW/'disagreements/decisions.json')['decisions'] if r['decision']=='unresolved']
    places = [r for r in read(REVIEW/'places/classifications.json') if r['name_resolution']['category']=='ambiguous']
    assert len(disagreements)==128 and len(places)==73
    ids = [r['case_id'] for r in disagreements+places]
    assert len(set(ids))==201
    return set(ids)


def validate(answer, packet):
    errors=[]
    if not isinstance(answer,dict) or set(answer)!={'job_id','decisions'}:
        return ['answer shape']
    if answer['job_id']!=packet['job_id']:
        errors.append('wrong job_id')
    cases={c['case_id']:c for c in packet['cases']}
    decisions=answer.get('decisions')
    if not isinstance(decisions,list):
        return errors+['decisions not a list']
    if collections.Counter(d.get('case_id') for d in decisions if isinstance(d,dict))!=collections.Counter(cases.keys()):
        errors.append('case IDs missing, extra or duplicate')
    verses={v['key']:v for v in packet['verses']}
    slugs={p['slug'] for p in packet['registered_places']}
    for d in decisions:
        if not isinstance(d,dict) or d.get('case_id') not in cases:
            continue
        cid=d['case_id']; case=cases[cid]
        if set(d)!=set(FIELDS): errors.append(cid+': fields')
        for field in ('decision','reason_code','category'):
            if d.get(field) not in FIELDS[field]['enum']: errors.append(cid+': '+field)
        if d.get('category')!=case['category']: errors.append(cid+': category changed')
        if d.get('target_place') is not None and d['target_place'] not in slugs:
            errors.append(cid+': unknown exact target slug')
        if case.get('place_slug') and d.get('target_place') not in (case['place_slug'],None):
            errors.append(cid+': disputed place silently replaced')
        for field in ('reason_uk','identity_context_uk'):
            if not isinstance(d.get(field),str) or len(d[field])<20:
                errors.append(cid+': missing '+field)
        allowed_sources=set(case['allowed_external_sources'])
        if not isinstance(d.get('external_source_ids'),list) or not set(d['external_source_ids'])<=allowed_sources:
            errors.append(cid+': unsupported external source ID')
        quotes=d.get('evidence')
        if not isinstance(quotes,list) or not quotes:
            errors.append(cid+': missing evidence'); continue
        for q in quotes:
            if not isinstance(q,dict) or set(q)!={'verse','quote','role'} or q.get('verse') not in verses:
                errors.append(cid+': invalid witness key'); continue
            if q.get('role') not in QUOTE['properties']['role']['enum']:
                errors.append(cid+': evidence role')
            quote=q.get('quote')
            if not isinstance(quote,str) or len(quote.strip())<4 or not quoted(quote,verses[q['verse']]['body']):
                errors.append(cid+': nonverbatim quote '+q['verse'])
        if not any(q.get('verse') in case['focus_keys'] for q in quotes):
            errors.append(cid+': no original episode witness')
        bhsa=[q for q in quotes if q.get('verse','').startswith('BHSA:')]
        if case['hebrew_available'] and (d.get('checked_hebrew') is not True or not bhsa):
            errors.append(cid+': supplied Hebrew was not quoted')
        if not case['hebrew_available'] and d.get('checked_hebrew') is not False:
            errors.append(cid+': invented Hebrew review')
        if d.get('decision')=='supported':
            if not d.get('target_place'): errors.append(cid+': supported without target')
            if d.get('reason_code') not in ('stated','context_chain','textual_variant'):
                errors.append(cid+': supported without evidence chain')
            if not {'event','identity','location'}<={q.get('role') for q in quotes}:
                errors.append(cid+': missing supported event/identity/location evidence')
    return errors


def quoted(quote, body):
    # Canonical Unicode equivalence preserves every Hebrew letter and mark.
    return unicodedata.normalize('NFC',quote) in unicodedata.normalize('NFC',body)


def parameters(packet, feedback=None):
    content=json.dumps(packet,ensure_ascii=False,separators=(',',':'))
    if feedback:
        content+='\nRepair prior structural errors without inventing evidence:\n'+json.dumps(feedback,ensure_ascii=False)
    return {'model':MODEL,'max_tokens':min(24000,5000+len(packet['cases'])*1700),
        'system':[{'type':'text','text':SYSTEM,'cache_control':{'type':'ephemeral'}}],
        'messages':[{'role':'user','content':content}],
        'output_config':{'effort':'high','format':{'type':'json_schema','schema':SCHEMA}}}


def prepare():
    context=read(OUT/CONTEXT_FILE); web=read(OUT/'web-research.json')
    for name,expected in context['source_hashes'].items():
        if sha(MAIN/name)!=expected: raise ValueError('Context source changed '+name)
    cases=context['cases']
    assert {c['case_id'] for c in cases}==wanted() and len(cases)==201
    research={r['case_id']:r for r in web['cases']}
    assert set(research)==wanted() and len(web['cases'])==201
    sources={s['id']:s for s in web['sources']}
    assert len(sources)==len(web['sources'])
    # The context extractor owns the exact immutable catalogue snapshot.
    catalogue=read(OUT/'catalogue.json')
    if isinstance(catalogue,dict): catalogue=catalogue.get('records',catalogue.get('places'))
    registered=[{k:p.get(k) for k in ('slug','name','kind','distinguisher','aliasOf')}
        for p in catalogue if p['kind'] in ('place','object')]
    definitions={p['slug']:p for p in catalogue if p['kind'] in ('place','object')}
    assert registered and len({p['slug'] for p in registered})==len(registered)
    groups=collections.defaultdict(list)
    for case in cases:
        assert case['focus_keys'] and case['verses']
        verse_map={v['key']:v for v in case['verses']}
        assert set(case['focus_keys'])<=set(verse_map)
        focus=verse_map[case['focus_keys'][0]]['canonical']
        groups[tuple(focus[:2])].append(case)
    entries=[]
    for (book,chapter),group in sorted(groups.items()):
        for offset in range(0,len(group),6):
            chunk=group[offset:offset+6]
            job_id=f'B{book:02}-C{chapter:03}-{offset//6:02}'
            verses={}; inputs=[]; used_sources=set(); detailed=set()
            for case in chunk:
                if case.get('place_slug'): detailed.add(case['place_slug'])
                detailed.update(p['slug'] for p in case.get('candidate_places',[]) if p.get('kind') in ('place','object'))
                for verse in case['verses']:
                    old=verses.get(verse['key'])
                    if old and (old['body']!=verse['body'] or old['canonicalAddresses']!=verse['canonicalAddresses']):
                        raise ValueError('Conflicting witness '+verse['key'])
                    verses[verse['key']]={k:verse[k] for k in ('key','body','canonical','canonicalAddresses')}
                external=research[case['case_id']]
                detailed.update(external.get('candidate_slugs',[]))
                assert set(external['source_ids'])<=set(sources)
                used_sources.update(external['source_ids'])
                original=case['original']
                prior={k:original[k] for k in ('decision','reason_uk','original_flag','original','name_resolution') if k in original}
                inputs.append({k:case[k] for k in ('case_id','source_kind','person','place_slug','place_name','category','focus_keys')}
                    | {'prior':prior,'research':external,'allowed_external_sources':external['source_ids'],
                       'hebrew_available':any(v['key'].startswith('BHSA:') for v in case['verses']),
                       'hebrew_diagnostics':case.get('context_metadata',{}).get('bhsa',{})})
            packet={'job_id':job_id,'cases':inputs,'verses':list(verses.values()),
                'registered_places':registered,'target_place_details':[definitions[s] for s in sorted(detailed) if s in definitions],
                'external_sources':[sources[s] for s in sorted(used_sources)]}
            path=OUT/'inputs'/(job_id+'.json')
            if path.exists() and read(path)!=packet: raise ValueError('Frozen input changed '+job_id)
            dump(path,packet)
            entries.append({'job_id':job_id,'case_ids':[c['case_id'] for c in chunk],
                'sha256':sha(path),'request_bytes':len(json.dumps(parameters(packet),ensure_ascii=False).encode())})
    manifest={'task':'TSK-1249','version':VERSION,'model':MODEL,'at':now(),'case_count':201,'jobs':entries,
        'script_sha256':sha(Path(__file__)),'client_sha256':sha(MAIN/'scripts/claude_api.py'),
        'system_sha256':object_sha(SYSTEM),'schema_sha256':object_sha(SCHEMA),
        'input_sha256':{n:sha(OUT/n) for n in (CONTEXT_FILE,'web-research.json','catalogue.json')},
        'prior_sha256':{n:sha(REVIEW/n) for n in ('disagreements/decisions.json','places/classifications.json')},
        'incremental_cap_usd':CAP,'cumulative_cap_usd':TOTAL_CAP,'human_accepted':False,
        'owner_instruction':'2026-10-26: BHSA + wider context + internet; close uncertain cases without admitting; allow Sonnet API and increased budget; no corpus gate.'}
    path=OUT/'manifest.json'
    if path.exists():
        old=read(path)
        for key in manifest.keys()-{'at'}:
            if old[key]!=manifest[key]: raise ValueError('Frozen manifest changed '+key)
    else: dump(path,manifest)
    (OUT/'code').mkdir(exist_ok=True)
    shutil.copy2(__file__,OUT/'code'/Path(__file__).name)
    print(json.dumps({'jobs':len(entries),'cases':201,'bytes':sum(e['request_bytes'] for e in entries)}),flush=True)


def frozen():
    manifest=read(OUT/'manifest.json')
    for path,expected in [(Path(__file__),manifest['script_sha256']),(MAIN/'scripts/claude_api.py',manifest['client_sha256'])]:
        if sha(path)!=expected: raise ValueError('Frozen code changed '+str(path))
    for directory,field in ((OUT,'input_sha256'),(REVIEW,'prior_sha256')):
        for name,expected in manifest[field].items():
            if sha(directory/name)!=expected: raise ValueError('Frozen source changed '+name)
    for job in manifest['jobs']:
        if sha(OUT/'inputs'/(job['job_id']+'.json'))!=job['sha256']: raise ValueError('Frozen packet changed')
    return manifest


def budget():
    ledger=MAIN/'.claude-api/usage.jsonl'
    rows=[json.loads(s) for s in ledger.read_text(encoding='utf-8').splitlines() if s.strip()]
    by_id={}
    for row in rows:
        if row.get('label') not in LABELS: continue
        if row.get('usd') is None or not row.get('id'): raise ValueError('Unknown price or request identity')
        key=(row['label'],row['id'])
        if key in by_id and by_id[key]['usd']!=row['usd']: raise ValueError('Conflicting duplicate usage')
        by_id[key]=row
    return (sum(r['usd'] for r in by_id.values() if r['label']==LABEL),sum(r['usd'] for r in by_id.values()))


def reserve(requests,batch):
    estimate=sum((len(json.dumps(p,ensure_ascii=False).encode())*2.5+p['max_tokens']*10)/1e6
        for _,p in requests)/(2 if batch else 1)
    own,total=budget()
    if own+estimate>CAP or total+estimate>TOTAL_CAP: raise ValueError('Budget reservation refused')
    return {'at':now(),'own_spent':own,'cumulative_spent':total,'reserved_usd':estimate}


def record_message(client,message,batch):
    rows=[json.loads(s) for s in client.LEDGER.read_text(encoding='utf-8').splitlines() if s.strip()]
    if not any(r.get('label')==LABEL and r.get('id')==message.id for r in rows):
        client.record(LABEL,message.model,message.usage,batch=batch,request_id=message.id)


def store(client,message,job_id,attempt,batch):
    record_message(client,message,batch)
    dump(OUT/'raw'/f'{job_id}-{attempt}-message.json',message.model_dump(mode='json'))
    errors=[]; answer=None
    if message.model!=MODEL: errors.append('Actual model differs from pinned model')
    if message.stop_reason!='end_turn': errors.append('Stop reason '+str(message.stop_reason))
    try: answer=json.loads(client.text_of(message))
    except (ValueError,TypeError): errors.append('Invalid JSON')
    if answer is not None: errors+=validate(answer,read(OUT/'inputs'/(job_id+'.json')))
    result={'at':now(),'job_id':job_id,'attempt':attempt,'message_id':message.id,'model':message.model,
        'batch':batch,'usage':message.usage.model_dump(mode='json'),'estimated_usd':client.cost(message.model,message.usage,batch),
        'input_sha256':sha(OUT/'inputs'/(job_id+'.json')),'answer':answer,'errors':errors,'human_accepted':False}
    dump(OUT/'attempts'/f'{job_id}-{attempt}.json',result)
    if not errors: dump(OUT/'checked'/(job_id+'.json'),result)
    print(json.dumps({'job':job_id,'attempt':attempt,'valid':not errors,'errors':errors[:8],
        'usd':result['estimated_usd']},ensure_ascii=False),flush=True)
    return result


def pending(manifest):
    return [j['job_id'] for j in manifest['jobs'] if not (OUT/'checked'/(j['job_id']+'.json')).exists()]


def request(job_id):
    previous=sorted((OUT/'attempts').glob(job_id+'-*.json'))
    attempt=len(previous)+1
    if attempt>3: raise ValueError('Attempts exhausted '+job_id)
    old=read(OUT/'attempts'/f'{job_id}-{attempt-1}.json') if previous else None
    params=parameters(read(OUT/'inputs'/(job_id+'.json')),old['errors'] if old else None)
    path=OUT/'raw'/f'{job_id}-{attempt}-request.json'
    if path.exists(): raise ValueError('Unknown prior request outcome; do not resubmit '+str(path))
    return attempt,params,path


def pilot():
    lock=OUT/'controller.lock'
    descriptor=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY)
    os.write(descriptor,str(os.getpid()).encode()); os.close(descriptor)
    try: return _pilot()
    finally: lock.unlink(missing_ok=True)


def _pilot():
    manifest=frozen(); client=api()
    ids={'huldah:0','moses:29'}
    selected=[j['job_id'] for j in manifest['jobs'] if ids.intersection(j['case_ids'])]
    dump(OUT/'pilot-jobs.json',selected)
    for job_id in selected:
        if job_id not in pending(manifest): continue
        attempt,params,path=request(job_id)
        receipt=reserve([(job_id,params)],False)
        dump(path,params); dump(OUT/'pilot-reservation.json',receipt)
        with client.client().messages.stream(**params) as stream:
            message=stream.get_final_message()
        if store(client,message,job_id,attempt,False)['errors']:
            raise ValueError('Pilot failed validation; inspect original before retry')
    report()


def collect(client,batch_id,entries):
    for row in client.client().messages.batches.results(batch_id):
        job_id,attempt=entries[row.custom_id]
        dump(OUT/'raw'/f'{job_id}-{attempt}-batch-result.json',row.model_dump(mode='json'))
        if row.result.type=='succeeded':
            store(client,row.result.message,job_id,attempt,True)
        else:
            dump(OUT/'attempts'/f'{job_id}-{attempt}.json',{'errors':[row.result.type],
                'job_id':job_id,'attempt':attempt,'at':now(),'batch_id':batch_id})


def run():
    manifest=frozen(); client=api()
    gate=read(OUT/'pilot-semantic-review.json')
    if gate.get('generation_gate')!='proceed' or gate.get('human_accepted') is not False:
        raise ValueError('Missing agent semantic pilot review')
    if gate.get('manifest_sha256')!=sha(OUT/'manifest.json'): raise ValueError('Wrong pilot manifest')
    state_path=OUT/'checkpoint.json'
    state=read(state_path) if state_path.exists() else {'batches':[]}
    lock=OUT/'controller.lock'
    descriptor=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY)
    os.write(descriptor,str(os.getpid()).encode()); os.close(descriptor)
    try:
        while True:
            active=next((b for b in state['batches'] if b['status']!='collected'),None)
            if active:
                if not active.get('batch_id'): raise ValueError('Unknown submission outcome; inspect client batch ledger')
                remote=client.client().messages.batches.retrieve(active['batch_id'])
                if remote.processing_status!='ended': time.sleep(45); continue
                collect(client,active['batch_id'],active['entries'])
                active['status']='collected'; active['collected_at']=now(); dump(state_path,state); report(); continue
            eligible=pending(manifest)
            if not eligible: break
            chosen=[]; requests=[]; entries={}; paths=[]
            for job_id in eligible[:80]:
                attempt,params,path=request(job_id)
                candidate=(f'{job_id}-a{attempt}',params)
                try: receipt=reserve(requests+[candidate],True)
                except ValueError:
                    if not requests: raise
                    break
                chosen.append(job_id); requests.append(candidate); paths.append(path)
                entries[candidate[0]]=[job_id,attempt]
            active={'at':now(),'status':'submitting','reservation':receipt,'entries':entries}
            state['batches'].append(active); dump(state_path,state)
            for path,(_,params) in zip(paths,requests): dump(path,params)
            batch_id=client.batch_submit(requests,label=LABEL)
            active.update({'batch_id':batch_id,'status':'processing'}); dump(state_path,state)
            print(json.dumps({'submitted':batch_id,'requests':len(requests),'reservation':receipt}),flush=True)
        report()
    finally: lock.unlink(missing_ok=True)


def report():
    manifest=frozen(); rows=[]; provenance={}
    for job in manifest['jobs']:
        path=OUT/'checked'/(job['job_id']+'.json')
        if not path.exists(): continue
        result=read(path)
        raw=read(OUT/'raw'/f"{job['job_id']}-{result['attempt']}-message.json")
        raw_text=''.join(block['text'] for block in raw['content'] if block['type']=='text')
        assert json.loads(raw_text)==result['answer'] and raw['id']==result['message_id']
        assert raw['model']==MODEL and raw['stop_reason']=='end_turn'
        errors=validate(result['answer'],read(OUT/'inputs'/(job['job_id']+'.json')))
        if errors: raise ValueError(errors)
        assert result['input_sha256']==job['sha256'] and result['model']==MODEL
        assert result['human_accepted'] is False
        for row in result['answer']['decisions']:
            rows.append(row|{'status':'closed','admit_presence':row['decision']=='supported','human_accepted':False,
                'provenance':{'job_id':job['job_id'],'attempt':result['attempt'],'message_id':result['message_id'],
                    'model':result['model'],'input_sha256':result['input_sha256']}})
        provenance[job['job_id']]=sha(path)
    ids=[r['case_id'] for r in rows]
    assert len(ids)==len(set(ids)) and set(ids)<=wanted()
    own,total=budget()
    summary={'task':'TSK-1249','at':now(),'case_count':201,'covered':len(rows),'missing':sorted(wanted()-set(ids)),
        'complete':set(ids)==wanted(),'decisions':dict(collections.Counter(r['decision'] for r in rows)),
        'incremental_usd':round(own,6),'cumulative_usd':round(total,6),'incremental_cap_usd':CAP,
        'cumulative_cap_usd':TOTAL_CAP,'human_accepted':False,'new_corpus_verification':False}
    dump(OUT/'decisions.json',{'summary':summary,'manifest_sha256':sha(OUT/'manifest.json'),
        'checked_sha256':provenance,'decisions':sorted(rows,key=lambda r:r['case_id'])})
    dump(OUT/'status.json',summary)
    print(json.dumps(summary,ensure_ascii=False),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('mode',choices=['prepare','pilot','run','report'])
    getattr(__import__(__name__),parser.parse_args().mode)()
