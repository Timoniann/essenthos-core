"""Acquire named source evidence once and preserve exact bytes/hashes; never overwrite held evidence."""
import hashlib
import json
import urllib.request
from pathlib import Path
from datetime import datetime, timezone

ROOT = Path(r'C:\Users\timon\Projects\Essenthos\essenthos-core\Resources\PlaceLocationSources\2026-10-26')
ROOT.mkdir(parents=True, exist_ok=True)
sources = {
    'pcgn-jordan-2025.pdf': 'https://assets.publishing.service.gov.uk/media/694152af5431f4f94d7f0aca/Jordan_Toponymic_Factfile.pdf',
    'govuk-reuse.html': 'https://www.gov.uk/help/reuse-govuk-content',
    'drews-han-2010.html': 'https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0012481',
    'drews-han-2010.xml': 'https://journals.plos.org/plosone/article/file?id=10.1371/journal.pone.0012481&type=manuscript',
}
receipt_path = ROOT/'manifest.json'
PINNED = {
    'pcgn-jordan-2025.pdf': 'b114fe09f4d593b6bd9bdd5cb3443b63f6564232a9109887d4899bcad2b86d16',
    'govuk-reuse.html': 'cf262480ba879fed594c3c938c273a0f34187d9dd4af11317c6a509926a9a0fa',
    'drews-han-2010.html': 'd299df426bdee7086ae3281a79cec8edd057222d0039293bd9f168bf39a87dad',
    'drews-han-2010.xml': 'e511bc3c26268fac5f118fd377c7b808ccc3486a53e1e2ca38d491822d8d1f02',
}
held = json.loads(receipt_path.read_text(encoding='utf-8')) if receipt_path.exists() else {}
for name,url in sources.items():
    path = ROOT/name
    if path.exists():
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        assert held[name]['sha256'] == digest == PINNED[name] and held[name]['url'] == url, name
        continue
    request = urllib.request.Request(url, headers={'User-Agent':'Essenthos source archiver/1.0'})
    with urllib.request.urlopen(request, timeout=60) as response:
        body = response.read()
    assert len(body) > 1000
    assert hashlib.sha256(body).hexdigest() == PINNED[name], 'Source bytes changed: '+name
    path.write_bytes(body)
    held[name] = dict(url=url, sha256=hashlib.sha256(body).hexdigest(), bytes=len(body), retrieved=datetime.now(timezone.utc).isoformat())
    receipt_path.write_text(json.dumps(held,indent=2)+'\n',encoding='utf-8')
print(json.dumps(held))
