# Historical (TSK-1452): the run folders this script names under Resources/Essenthos/generation were archived on
# 2026-11-04 to E:\EssenthosArchive\generation\<run>, with a verified manifest beside it. The script is kept as the record
# of how the run was made and no longer runs as written; to run it again, point it at a copy of the run folder, never
# at the archive itself.
"""One bounded read of locations for all admitted presence places and newly created sites."""
import json
import subprocess
from pathlib import Path
from collections import Counter

MAIN = Path(r'C:\Users\timon\Projects\Essenthos\essenthos-core')
OUT = MAIN / 'Resources/Essenthos/generation/presence-locations-2026-10-26-v1'
OUT.mkdir(parents=True, exist_ok=True)
dataset = json.loads((MAIN / 'Resources/Essenthos/person-presence.json').read_text(encoding='utf-8'))
sites = {s['slug'] for s in dataset['sites']}
targets = sites | {r['place'] for r in dataset['records']}
literal = json.dumps(sorted(targets)).replace("'", "''")
sql = f"""
SET statement_timeout='30s';
SELECT coalesce(jsonb_agg(row_to_json(x)),'[]'::jsonb) FROM (
SELECT e.slug,e.name,e.kind,e.open_bible_id,l.longitude,l.latitude,l.kind location_kind,
coalesce((SELECT jsonb_agg(jsonb_build_object('rank',c.rank,'site',c.site,'modern_id',c.modern_id,
'lon',c.longitude,'lat',c.latitude,'special',c.special,'withheld',c.withheld,'source',c.coordinates_source) ORDER BY c.rank)
FROM place_candidate c WHERE c.entity_id=e.id),'[]'::jsonb) candidates
FROM entity e LEFT JOIN place_location l ON l.entity_id=e.id
WHERE e.slug IN (SELECT jsonb_array_elements_text('{literal}'::jsonb)) ORDER BY e.slug) x;
"""
result = subprocess.run(['docker','exec','-i','essenthos-core-db-1','sh','-c',
                        'psql -X -v ON_ERROR_STOP=1 -q -t -A -U "$POSTGRES_USER" -d essenthos_core'],
                        input=sql, text=True, encoding='utf-8', capture_output=True, check=True)
rows = json.loads(result.stdout)
assert {r['slug'] for r in rows} == targets
for row in rows:
    row['new_record'] = row['slug'] in sites
    if row['latitude'] is not None:
        row['gap'] = 'located'
    elif row['candidates']:
        best = row['candidates'][0]
        row['gap'] = 'withheld' if best['withheld'] else 'non_site_or_unknown' if best['special'] else 'unlocated_candidate'
    elif row['kind'] == 'object':
        row['gap'] = 'structure_without_site'
    else:
        row['gap'] = 'no_catalogue_match'
summary = dict(total=len(rows), geographical=sum(r['kind']=='place' for r in rows),
               created=len(sites), gaps=dict(Counter(r['gap'] for r in rows)))
(OUT / 'inventory.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2)+'\n',encoding='utf-8')
(OUT / 'summary.json').write_text(json.dumps(summary, indent=2)+'\n',encoding='utf-8')
print(json.dumps(summary))
print(json.dumps([{k:r[k] for k in ('slug','name','gap','candidates')} for r in rows if r['gap']!='located'],ensure_ascii=False))
