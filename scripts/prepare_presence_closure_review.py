"""Record the lead's source-bound semantic decisions without rewriting model responses."""
import copy
import json
import finalize_presence_closure as f


def main():
    p=f.runtime(); raw=p.read(f.OUT/'decisions.json')
    independent=p.read(f.OUT/'semantic-review-independent.json')
    baseline=p.read(f.OUT/'baseline-overlap-review.json')
    assert raw['summary']['complete'] and len(raw['decisions'])==201
    assert independent['source_decisions_sha256'] in (p.sha(f.OUT/'decisions.json'),p.object_sha(raw['decisions']))
    rows={r['case_id']:r for r in raw['decisions']}
    reviewed={r['case_id']:r for r in independent['reviewed']}
    assert set(reviewed)=={cid for cid,r in rows.items() if r['decision']=='supported'}
    overrides=[]
    for cid,judgment in reviewed.items():
        assert judgment['original_case_sha256']==p.object_sha(rows[cid]),cid
        if judgment['assessment']=='agree' or cid=='elijah:4':continue
        overrides.append({'case_id':cid,'original_sha256':p.object_sha(rows[cid]),
            'reason_uk':judgment['reason_uk'],'fields':judgment['proposed_fields']})
    # The recalled sending has an explicitly fulfilled parallel in the held Kings narrative.
    elijah=rows['elijah:4']
    overrides.append({'case_id':'elijah:4','original_sha256':p.object_sha(elijah),
        'reason_uk':'Parent read the actual fulfilled journey in the frozen 1 Kings17 narrative, supplementing the original Luke packet rather than assuming a command was fulfilled.',
        'fields':{'reason_uk':'Лк4:26 згадує послання Іллі до вдови в Сарепті Сидонській. Самого послання недостатньо; додатково прочитано збережений паралельний виклад1Цар17:9–10: наказ називає Сарепту, що належить Сидону, а наступний вірш прямо розповідає, що Ілля встав, пішов туди й прийшов до міської брами, де зустрів удову. Це виконана дія тієї самої оповіді, яку згадує Ісус, а не інша випадкова подорож. Джерельний пакет доповнив агент після модельного читання; не приписуємо ці додаткові вірші початковому запиту Sonnet.',
            'evidence':copy.deepcopy(elijah['evidence'])+[
                {'verse':'KJV:11:17:9','quote':'Zarephath, which belongeth to Zidon','role':'location'},
                {'verse':'KJV:11:17:10','quote':'So he arose and went to Zarephath. And when he came to the gate of the city','role':'event'}]}})
    after={cid:dict(r) for cid,r in rows.items()}
    for change in overrides:after[change['case_id']].update(change['fields'])
    assert baseline['source_dataset_sha256']==p.sha(p.MAIN/'Resources/Essenthos/person-presence.json')
    review={'task':'TSK-1249','at':p.now(),'reviewer':'Codex lead after independent source reading',
        'human_accepted':False,'source_decisions_sha256':p.object_sha(raw['decisions']),
        'independent_review_sha256':p.sha(f.OUT/'semantic-review-independent.json'),
        'baseline_review_sha256':p.sha(f.OUT/'baseline-overlap-review.json'),
        'reviewed_supported_case_ids':sorted(cid for cid,r in after.items() if r['decision']=='supported'),
        'overrides':overrides,'baseline_withdrawals':[r for r in baseline['reviewed'] if r['decision']=='withdraw'],
        'additional_witnesses':[{'case_id':'elijah:4','job_id':'KJV-011-017-00','verse':v}
            for v in ('KJV:11:17:9','KJV:11:17:10')],
        'limits':'All45 original positives independently read.156 closures screened, not independently re-read exhaustively.16 overlapping baseline pairs fully audited;10 withdrawn, other independently supported events retained. No population precision estimate or human acceptance.'}
    path=f.OUT/'agent-semantic-review.json'
    if path.exists():raise ValueError('Agent review already exists; preserve its version')
    p.dump(path,review)
    print(json.dumps({'supported':len(review['reviewed_supported_case_ids']),'overrides':len(overrides),
        'withdrawals':len(review['baseline_withdrawals'])}))


if __name__=='__main__':main()
