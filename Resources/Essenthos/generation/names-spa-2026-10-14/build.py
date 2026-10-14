# Run once, on a checkout without the batch: it appends "spa" to the object files.
# missing.json is query.sql's output parsed into one JSON list (the rows span lines).
import json, re, pathlib
HERE=pathlib.Path(__file__).resolve().parent
W=str(HERE.parents[3])
rows=json.load(open(HERE/'missing.json',encoding='utf-8'))
def tsv(p):
    d={}
    for l in open(p,encoding='utf-8'):
        l=l.rstrip('\n')
        if l:
            s,n=l.split('\t'); assert s not in d, s; d[s]=n
    return d
unnamed=tsv(HERE/'unnamed.tsv'); corr=tsv(HERE/'corrections.tsv')
things={'jar-of-manna':'Urna del maná','hezekiahs-tunnel':'Conducto de Ezechîas',
 'tent-of-meeting-outside-the-camp':'Tabernáculo del Testimonio fuera del campo',
 'davids-tent-for-the-ark':'Tienda de David para el arca','jacobs-pillar':'Título de Jacob',
 'sauls-spear':'Lanza de Saúl','moses-veil':'Velo de Moisés','firstfruits':'Gavilla de las primicias',
 'eighth-day-assembly':'Día octavo de la solemnidad','fasts-of-the-exile':'Ayuno del cuarto, quinto, séptimo y décimo mes',
 'second-passover':'Pascua del segundo mes','jeroboams-feast':'Solemnidad de Jeroboam'}
bykind={r['slug']:r for r in rows}
assert all(bykind[s]['kind'] in ('object','observance') for s in things)
assert {s for s,r in bykind.items() if r['kind'] in ('object','observance')}==set(things)
for s in corr: assert s in bykind and bykind[s]['printed'], s
SUP={"en","a","de","del","al","el","la","los","las"}
out=[]; stats={'unnamed':0,'printed':0,'corrected':0}
for r in sorted(rows,key=lambda r:r['slug']):
    s=r['slug']
    if s in things: continue
    if not r['printed']: n=unnamed[s]; stats['unnamed']+=1
    elif s in corr: n=corr[s]; stats['corrected']+=1
    else: n=r['printed']; stats['printed']+=1
    assert n==n.strip() and n, s
    assert not (' ' in n and n.split(' ')[0].lower() in SUP), (s,n)
    assert not re.search(r'[\u0400-\u04FF]',n)
    out.append(json.dumps({"entity":s,"names":{"spa":{"nominative":n}},"model":"claude-opus-5-5","askedAt":"2026-10-14"},ensure_ascii=False))
open(W+'/Resources/Essenthos/name-forms/zz-complete-spa-2026-10-14.jsonl','w',encoding='utf-8',newline='\n').write('\n'.join(out)+'\n')
print(stats, len(out))
for f in ['ObjectRecords.json','ObservanceRecords.json']:
    p=W+'/Essenthos.Forge/Loading/Encyclopedia/'+f
    t=open(p,encoding='utf-8').read()
    for s,n in things.items():
        i=t.find('"slug": "%s"'%s)
        if i<0: continue
        j=t.index('"names": {',i); k=t.index('}',j)
        block=t[j:k]
        last=block.rstrip().rsplit('\n',1)[1]
        indent=re.match(r'\s*',last).group(0)
        newblock=block.rstrip()+',\n'+indent+'"spa": '+json.dumps(n,ensure_ascii=False)+block[len(block.rstrip()):]
        t=t[:j]+newblock+t[k:]
    open(p,'w',encoding='utf-8',newline='').write(t)
    d=json.loads(t)
    print(f, sum(1 for r in d['records'] if 'spa' not in r['names']))
