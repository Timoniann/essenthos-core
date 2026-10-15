import json,glob,os,sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT=r'C:\Users\timon\AppData\Local\Temp\essenthos-evidentia'
label,sets,texts=sys.argv[1],sys.argv[2].split(','),sys.argv[3].split(',')
POS={'NOUN','PROPN','VERB'}
LEX={'KnownRendering','DictionarySense','TargetGloss','MatchingNormalisedForm','SharedStrongNumber'}
good=bad=0
for s in sets:
  for f in sorted(glob.glob(os.path.join(ROOT,label,f'{s}-*.words.json'))):
    ws=json.load(open(f,encoding='utf-8'))
    taken={w['TargetWordId'] for w in ws if w['TargetWordId']}
    surf={}
    for w in ws:
        for g in w.get('Gold') or []: surf[g['TargetWordId']]=(g['Surface'],g['StrongNumber'],g.get('Gloss'))
        for c in w.get('Candidates') or []: surf.setdefault(c['TargetWordId'],(c['TargetSurface'],c['TargetStrongNumber'],None))
    used=set()
    for w in ws:
        piv={k:v for k,v in (w.get('Pivot') or {}).items() if k in texts}
        if w['TargetWordId'] or w.get('State')!='unresolved' or w.get('Grammatical') or not piv or w.get('SourcePartOfSpeech') not in POS: continue
        key=set(w.get('SplitKey') or [])
        cands={c['TargetWordId']:c for c in (w.get('Candidates') or [])}
        opts={}
        for t,v in piv.items():
            vv=[x for x in v if not ((surf.get(x) or ('',None))[1] or '').startswith('H90')]
            if len(vv)!=1: continue
            for x in vv:
                if x in taken or x in used: continue
                opts.setdefault(x,set()).add(t)
        ch=[x for x,by in opts.items() if len(by)>=2 or (x in cands and set(cands[x]['Evidence'])&LEX)]
        if len(ch)!=1: continue
        x=ch[0]; used.add(x)
        if x in key: good+=1; continue
        bad+=1
        print(f"{w['CanonicalBook']}:{w['CanonicalChapter']}:{w['CanonicalVerse']} '{w['SourceSurface']}'/{w['SourcePartOfSpeech']} -> {surf.get(x)} by {sorted(opts[x])} lex={x in cands}; key={[surf.get(k) for k in key]}")
print(good,bad)
