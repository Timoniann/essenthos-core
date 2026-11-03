# Historical (TSK-1452): the run folders this script names under Resources/Essenthos/generation were archived on
# 2026-11-04 to E:\EssenthosArchive\generation\<run>, with a verified manifest beside it. The script is kept as the record
# of how the run was made and no longer runs as written; to run it again, point it at a copy of the run folder, never
# at the archive itself.
"""Propose exact source name matches; this never creates identity or coordinate claims."""
import json
import re
from pathlib import Path

MAIN = Path(r'C:\Users\timon\Projects\Essenthos\essenthos-core')
OUT = MAIN / 'Resources/Essenthos/generation/presence-locations-2026-10-26-v1'
rows = json.loads((OUT/'inventory.json').read_text(encoding='utf-8'))
ancient = [json.loads(line) for line in (MAIN/'Resources/OpenBible/ancient.jsonl').read_text(encoding='utf-8').splitlines() if line]
def fold(name):
    return re.sub(r'[^a-z0-9]', '', name.casefold().replace(' of ', ' '))
lookup = {}
for row in ancient:
    for name in [row['friendly_id'], *row.get('translation_name_counts', {})]:
        lookup.setdefault(fold(name), {})[row['id']] = row
aliases = {'Abel of Beth-maachah':'Abel-beth-maacah','Judaea':'Judea','Melita':'Malta','Pergamos':'Pergamum','Shushan':'Susa',
           'Vale of Siddim':'Valley of Siddim','Kadeshbarnea':'Kadesh-barnea','Harosheth':'Harosheth-hagoyim',
           'Wilderness of Judah':'wilderness of Judea','Territory of Issachar':'Issachar','Land of Israel':'Israel'}
report = []
for row in rows:
    if row['gap'] != 'no_catalogue_match': continue
    candidates = lookup.get(fold(row['name']), {}) | lookup.get(fold(aliases.get(row['name'], '')), {})
    report.append(dict(slug=row['slug'],name=row['name'],candidates=[dict(
        id=a['id'],name=a['friendly_id'],types=a['types'],verses=[v['usx'] for v in a.get('verses',[])],
        modern=a.get('modern_associations',{})) for a in candidates.values()]))
(OUT/'name-matches.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps([dict(slug=r['slug'],candidates=[dict(id=a['id'],name=a['name'],types=a['types'],verses=a['verses'][:8]) for a in r['candidates']]) for r in report],ensure_ascii=False))
