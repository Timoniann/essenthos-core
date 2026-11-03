# Historical (TSK-1452): the run folders this script names under Resources/Essenthos/generation were archived on
# 2026-11-04 to E:\EssenthosArchive\generation\<run>, with a verified manifest beside it. The script is kept as the record
# of how the run was made and no longer runs as written; to run it again, point it at a copy of the run folder, never
# at the archive itself.
"""Preserve raw model dispositions and apply explicit source-bound agent review separately."""
import importlib.util
import json

from pathlib import Path

MAIN=Path('C:/Users/timon/Projects/Essenthos/essenthos-core')
OUT=MAIN/'Resources/Essenthos/generation/presence-closure-2026-10-26-v2'


def runtime():
    path=OUT/'code/presence_closure.py'
    spec=importlib.util.spec_from_file_location('frozen_presence_closure',path)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    module.frozen()
    return module


def review_packets(reader,manifest,review,original_jobs=None):
    packets={j['job_id']:reader.read(OUT/'inputs'/(j['job_id']+'.json')) for j in manifest['jobs']}
    additions=review.get('additional_witnesses',[])
    if not additions:return packets
    relative='Resources/Essenthos/generation/person-presence-sonnet55-2026-10-26-v3/jobs.jsonl'
    contexts=reader.read(OUT/reader.CONTEXT_FILE)
    expected=contexts['source_hashes'][relative]
    source=MAIN/relative
    assert reader.sha(source)==expected
    if original_jobs is None:
        original_jobs={}
        with source.open(encoding='utf-8') as stream:
            for line in stream:
                job=json.loads(line);original_jobs[job['id']]={v['key']:v for v in job['verses']}
    owner={cid:j['job_id'] for j in manifest['jobs'] for cid in j['case_ids']}
    for addition in additions:
        packet=packets[owner[addition['case_id']]]
        verse=original_jobs[addition['job_id']][addition['verse']]
        witness={k:verse[k] for k in ('key','body','canonical')}
        witness['canonicalAddresses']=verse.get('canonicalAddresses',[verse['canonical']])
        old=next((v for v in packet['verses'] if v['key']==verse['key']),None)
        if old: assert old==witness
        else: packet['verses'].append(witness)
    return packets


def apply_review(reader,raw,review):
    assert raw['summary']['complete'] and raw['summary']['covered']==201
    assert review['human_accepted'] is False
    assert review['source_decisions_sha256']==reader.object_sha(raw['decisions'])
    rows={r['case_id']:dict(r) for r in raw['decisions']}
    seen=set()
    for change in review.get('overrides',[]):
        cid=change['case_id']
        assert cid not in seen and cid in rows
        seen.add(cid)
        assert reader.object_sha(rows[cid])==change['original_sha256']
        assert change['reason_uk'] and set(change['fields'])<=set(reader.FIELDS)-{'case_id','category'}
        rows[cid].update(change['fields'])
        rows[cid]['admit_presence']=rows[cid]['decision']=='supported'
        rows[cid]['agent_override']={'reason_uk':change['reason_uk'],'original_sha256':change['original_sha256']}
    manifest=reader.frozen()
    packets=review_packets(reader,manifest,review)
    for job in manifest['jobs']:
        answer={'job_id':job['job_id'],'decisions':[{k:rows[cid][k] for k in reader.FIELDS} for cid in job['case_ids']]}
        errors=reader.validate(answer,packets[job['job_id']])
        if errors: raise ValueError(errors)
    supported={cid for cid,row in rows.items() if row['admit_presence']}
    assert set(review['reviewed_supported_case_ids'])==supported
    assert len(review['reviewed_supported_case_ids'])==len(supported)
    for addition in review.get('additional_witnesses',[]):
        rows[addition['case_id']].setdefault('agent_additional_witnesses',[]).append(addition)
    return sorted(rows.values(),key=lambda r:r['case_id'])


def main():
    reader=runtime()
    reader.report()
    raw=reader.read(OUT/'decisions.json');review=reader.read(OUT/'agent-semantic-review.json')
    rows=apply_review(reader,raw,review)
    summary=dict(raw['summary'])
    summary['decisions']=dict(reader.collections.Counter(r['decision'] for r in rows))
    summary['agent_overrides']=len(review.get('overrides',[]))
    summary['at']=review['at']
    result={'summary':summary,'manifest_sha256':reader.sha(OUT/'manifest.json'),
        'raw_decisions_sha256':reader.object_sha(raw['decisions']),
        'semantic_review_sha256':reader.sha(OUT/'agent-semantic-review.json'),'decisions':rows}
    target=OUT/'final-decisions.json'
    if target.exists() and reader.read(target)!=result: raise ValueError('Final reviewed layer changed; use a new review version')
    reader.dump(target,result)
    print(json.dumps(summary,ensure_ascii=False))


if __name__=='__main__':main()
