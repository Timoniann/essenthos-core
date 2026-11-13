"""Places of one name a reader cannot tell apart, as the map list shows them: per language, every group of places
with the same shown name whose words under the name (the distinguisher) are the same, and every distinguisher that
prints a book code as it is (1CH 27:27). Reads what a running API answers, so it measures the API's own rules.

usage: places-told-apart.py <api base url> [<languages, default eng,ukr,deu,spa>]
Exit 1 when any group reads alike.
"""
import json
import re
import sys
import urllib.request

sys.stdout.reconfigure(encoding='utf-8')
base = sys.argv[1].rstrip('/')
languages = (sys.argv[2] if len(sys.argv) > 2 else 'eng,ukr,deu,spa').split(',')
coded = re.compile(r'\b[1-3]?[A-Z]{2,3} \d+:\d+')

alike_total = 0
for language in languages:
    with urllib.request.urlopen(f'{base}/v1/places/map?language={language}', timeout=120) as response:
        items = json.load(response)['items']
    groups = {}
    for place in items:
        groups.setdefault((place.get('localName') or place['name']).lower(), []).append(place)
    shared = [group for group in groups.values() if len(group) > 1]
    alike = []
    for group in shared:
        by_line = {}
        for place in group:
            by_line.setdefault(place.get('distinguisher'), []).append(place['slug'])
        alike += [(line, slugs) for line, slugs in by_line.items() if len(slugs) > 1]
    codes = [place['slug'] for place in items if place.get('distinguisher') and coded.search(place['distinguisher'])]
    alike_total += len(alike)
    print(f'{language}: {len(shared)} names shared, {len(alike)} groups alike, {len(codes)} lines with a book code')
    for line, slugs in alike:
        print(f'  alike: {", ".join(slugs)} | {line}')
    if codes:
        print(f'  book codes: {", ".join(codes)}')

sys.exit(1 if alike_total else 0)
