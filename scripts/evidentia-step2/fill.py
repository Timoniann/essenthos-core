"""Fill rules for unplaced content words, by variant: which pivots, single-target pivot words only, which parts of speech."""
import json,glob,os,sys,itertools,collections
sys.stdout.reconfigure(encoding='utf-8')
ROOT=r'C:\Users\timon\AppData\Local\Temp\essenthos-evidentia'
label,sets=sys.argv[1],sys.argv[2].split(',')
LEX={'KnownRendering','DictionarySense','TargetGloss','MatchingNormalisedForm','SharedStrongNumber'}
def load(s):
    ws=[]
    for f in sorted(glob.glob(os.path.join(ROOT,label,f'{s}-*.words.json'))):
        ws.append(json.load(open(f,encoding='utf-8')))
    return ws
def variant(passages, texts, single, pos, need):
    res=collections.Counter()
    for ws in passages:
        taken={w['TargetWordId'] for w in ws if w['TargetWordId']}
        strong={}
        for w in ws:
            for g in w.get('Gold') or []: strong[g['TargetWordId']]=g['StrongNumber']
            for c in w.get('Candidates') or []: strong.setdefault(c['TargetWordId'],c['TargetStrongNumber'])
        used=set()
        for w in ws:
            piv={k:v for k,v in (w.get('Pivot') or {}).items() if k in texts}
            if w['TargetWordId'] or w.get('State')!='unresolved' or w.get('Grammatical') or not piv: continue
            if pos and w.get('SourcePartOfSpeech') not in pos: continue
            key=set(w.get('SplitKey') or [])
            cands={c['TargetWordId']:c for c in (w.get('Candidates') or [])}
            opts={}
            for t,v in piv.items():
                vv=[x for x in v if not (strong.get(x) or '').startswith('H90')]
                if single and len(vv)!=1: continue
                for x in vv:
                    if x in taken or x in used: continue
                    opts.setdefault(x,set()).add(t)
            def ok(x,by):
                lex = x in cands and bool(set(cands[x]['Evidence'])&LEX)
                if need=='two': return len(by)>=2
                if need=='lex': return lex
                if need=='either': return len(by)>=2 or lex
                if need=='both': return len(by)>=2 and lex
                if need=='one': return True
            ch=[x for x,by in opts.items() if ok(x,by)]
            if len(ch)==1:
                res['n']+=1; res['r']+=ch[0] in key; used.add(ch[0])
                res['cov']+=bool(w['GoldCoversSourceWord'])
    return res['r'],res['n']
passages=[p for s in sets for p in load(s)]
words=sum(len(p) for p in passages)
print(f'{sets}: {words} words')
LIT=['KJV','ULT','YLT','RLT2018']
for texts in (['KJV','ULT','YLT','RLT2018','UST'],LIT,['KJV','ULT'],['ULT','YLT','RLT2018'],['ULT']):
  for single in (False,True):
    for pos in (None,{'NOUN','PROPN','VERB','ADJ'},{'NOUN','PROPN','VERB'}):
      for need in ('one','two','lex','either','both'):
        r,n=variant(passages,texts,single,pos,need)
        if n: print(f"{'+'.join(texts):26} single={single!s:5} pos={'all' if not pos else '+'.join(sorted(pos)):22} need={need:6}: {r}/{n} = {100*r/n:.1f}%  (+{100*r/words:.2f} pts right, {100*(n-r)/words:.2f} wrong)")
