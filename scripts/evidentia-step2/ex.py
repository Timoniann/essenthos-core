import json,glob,os,sys,itertools
sys.stdout.reconfigure(encoding='utf-8')
ROOT=r'C:\Users\timon\AppData\Local\Temp\essenthos-evidentia'
label,s=sys.argv[1],sys.argv[2]
LEX={'KnownRendering','DictionarySense','TargetGloss','MatchingNormalisedForm','SharedStrongNumber'}
for f in sorted(glob.glob(os.path.join(ROOT,label,f'{s}-*.words.json'))):
    ws=json.load(open(f,encoding='utf-8'))
    taken={w['TargetWordId'] for w in ws if w['TargetWordId']}
    surf={}
    for w in ws:
        for g in w.get('Gold') or []: surf[g['TargetWordId']]=(g['Surface'],g['StrongNumber'],g.get('Gloss'))
        for c in w.get('Candidates') or []: surf.setdefault(c['TargetWordId'],(c['TargetSurface'],c['TargetStrongNumber'],None))
    for w in ws:
        piv=w.get('Pivot') or {}
        if w['TargetWordId'] or w.get('State')!='unresolved' or w.get('Grammatical') or not piv: continue
        key=set(w.get('SplitKey') or []); gold={g['TargetWordId'] for g in w.get('Gold') or []}
        cands={c['TargetWordId']:c for c in (w.get('Candidates') or [])}
        opts={}
        for t,v in piv.items():
            for x in v:
                if x in taken or (surf.get(x,('',None))[1] or '').startswith('H90'): continue
                opts.setdefault(x,set()).add(t)
        ch=[x for x,by in opts.items() if len(by)>=2 or (x in cands and set(cands[x]['Evidence'])&LEX)]
        if len(ch)==1 and ch[0] not in key:
            x=ch[0]
            print(f"{w['CanonicalBook']}:{w['CanonicalChapter']}:{w['CanonicalVerse']} '{w['SourceSurface']}' -> {surf.get(x)} by {sorted(opts[x])}; key={[surf.get(k) for k in key]}; ingold={x in gold}")
