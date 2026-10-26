"""Admit only explicitly reviewed supported cases; preserve existing pairs and source editions."""
import importlib.util
import hashlib
import json
from pathlib import Path
import unicodedata

import finalize_presence_closure as final


def object_sha(value):
    return hashlib.sha256(json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':')).encode()).hexdigest()


def extend(base, rows, packets, cases, withdrawals=()):
    pairs={(r['person'],r['place'],r['category']):dict(r,addresses=list(r['addresses']),evidence=list(r['evidence']))
        for r in base['records']}
    before=set(pairs)
    withdrawn=set()
    for decision in withdrawals:
        key=(decision['person'],decision['place'],decision['category'])
        assert key in pairs and key not in withdrawn and decision['decision']=='withdraw'
        assert object_sha(pairs[key])==decision['original_record_sha256']
        assert decision['reason_uk'] and decision['evidence']
        withdrawn.add(key)
        del pairs[key]
    added=[]
    for row in rows:
        assert row['status']=='closed' and row['human_accepted'] is False
        assert row['admit_presence']==(row['decision']=='supported')
        if not row['admit_presence']: continue
        case=cases[row['case_id']]
        packet=packets[row['provenance']['job_id']]
        witnesses={v['key']:v for v in packet['verses']}
        key=(case['person'],row['target_place'],row['category'])
        record=pairs.setdefault(key,{'person':key[0],'place':key[1],'category':key[2],'addresses':[],'evidence':[]})
        addresses=set(record['addresses'])
        for quote in row['evidence']:
            witness=witnesses[quote['verse']]
            assert unicodedata.normalize('NFC',quote['quote']) in unicodedata.normalize('NFC',witness['body'])
            for book,chapter,verse in witness['canonicalAddresses']:
                assert 1<=book<=93 and chapter>=1 and verse>=0
                addresses.add(book*1000000+chapter*1000+verse)
        record['addresses']=sorted(addresses)
        record['evidence'].append({'stage':'BHSA/wider-context/external-source closure',
            'task':'TSK-1249','case_id':row['case_id'],'source_kind':case['source_kind'],
            'source_pointer':case['original']['source_pointer'],'closure_reading':row,
            'evidence':row['evidence'],'external_source_ids':row['external_source_ids']})
        added.append(row['case_id'])
    assert before-withdrawn<=pairs.keys()
    return [pairs[k] for k in sorted(pairs)],{'supported_contexts':len(added),'added_pairs':len(pairs.keys()-before),
        'withdrawn_pairs':len(withdrawn-pairs.keys()),'net_pairs':len(pairs)-len(before),'case_ids':added,
        'withdrawn_keys':[list(k) for k in sorted(withdrawn-pairs.keys())]}


def apply(base, config, main, original_jobs):
    directory=(main/config['directory']).resolve()
    assert directory.is_relative_to((main/'Resources/Essenthos/generation').resolve())
    spec=importlib.util.spec_from_file_location('closure_frozen_runtime',directory/'code/presence_closure.py')
    reader=importlib.util.module_from_spec(spec);spec.loader.exec_module(reader)
    manifest=reader.frozen()
    assert config['version']==1 and config['task']=='TSK-1249'
    for name,expected in config['sha256'].items():
        assert name in ('manifest.json','final-decisions.json','agent-semantic-review.json')
        assert reader.sha(directory/name)==expected,name
    assert set(config['sha256'])=={'manifest.json','final-decisions.json','agent-semantic-review.json'}
    reader.report()
    raw=reader.read(directory/'decisions.json');review=reader.read(directory/'agent-semantic-review.json')
    # Use the matching finalizer contract while binding every quote back to frozen model inputs.
    assert final.OUT==directory
    reviewed=final.apply_review(reader,raw,review)
    published=reader.read(directory/'final-decisions.json')
    assert published['decisions']==reviewed and published['summary']['complete']
    contexts=reader.read(directory/reader.CONTEXT_FILE)
    cases={c['case_id']:c for c in contexts['cases']}
    packets=final.review_packets(reader,manifest,review,original_jobs)
    withdrawals=review.get('baseline_withdrawals',[])
    for decision in withdrawals:
        for quote in decision['evidence']:
            witness=original_jobs[quote['job_id']][quote['verse']]
            assert reader.quoted(quote['quote'],witness['body'])
    records,delta=extend(base,reviewed,packets,cases,withdrawals)
    base['records']=records
    base['admission']['closure']={'task':config['task'],'source':config['directory'],'sha256':config['sha256'],
        'human_accepted':False,'delta':delta,'excluded_closed_cases':201-delta['supported_contexts']}
    base['source']+=' Additional source-bound BHSA, parallel-witness and external-identity review, TSK-1249; uncertain cases closed without admission.'
    return base
