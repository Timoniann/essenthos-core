import json,glob,os,sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT=r'C:\Users\timon\AppData\Local\Temp\essenthos-evidentia'
label,sets=sys.argv[1],sys.argv[2].split(',')
for s in sets:
  for f in sorted(glob.glob(os.path.join(ROOT,label,f'{s}-*.words.json'))):
    ws=json.load(open(f,encoding='utf-8'))
    surf={}
    for w in ws:
        for g in w.get('Gold') or []: surf[g['TargetWordId']]=(g['Surface'],g['StrongNumber'])
    for w in ws:
        if w.get('ProposalKind')!='SameLanguagePivot' or w.get('CorrectOnSplitKey'): continue
        print(f"{os.path.basename(f)[:9]} {w['CanonicalChapter']}:{w['CanonicalVerse']} '{w['SourceSurface']}' -> {w['TargetSurface']} {w['TargetStrongNumber']} judged={w.get('CorrectOnJudgedKey')} | {w.get('Rationale','')[:60]} | key={[surf.get(k) for k in (w.get('SplitKey') or [])]} piv={ {k:len(v) for k,v in (w.get('Pivot') or {}).items()} }")
