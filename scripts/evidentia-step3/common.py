"""Shared readers for the step 3 scripts: a benchmark label's word records and edges, and the combiner's weights."""
import glob
import json
import math
import os
import sys

sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.join(os.environ.get('TEMP', r'C:\Users\timon\AppData\Local\Temp'), 'essenthos-evidentia')
HERE = os.path.dirname(os.path.abspath(__file__))
WEIGHTS = os.path.join(HERE, '..', '..', 'Essenthos.Forge', 'Loading', 'Links', 'Evidentia', 'EvidentiaCombiner.json')
PLACED = ('linked', 'attached', 'phrase member')


def passages(label, sets):
    out = []
    for s in sets:
        out += sorted(glob.glob(os.path.join(ROOT, label, f'{s}-*.words.json')))
    if not out:
        raise SystemExit(f'no word records under {label} for {sets}')
    return out


def words(label, sets):
    """Every word record, with its passage, in passage order."""
    rows = []
    for f in passages(label, sets):
        passage = os.path.basename(f).split('.')[0]
        for r in json.load(open(f, encoding='utf-8')):
            r['Passage'] = passage
            rows.append(r)
    return rows


def edges(label, sets):
    names, rows = None, []
    for s in sets:
        for f in sorted(glob.glob(os.path.join(ROOT, label, f'{s}-*.edges.json'))):
            data = json.load(open(f, encoding='utf-8'))
            names = names or data['Features']
            passage = os.path.basename(f).split('.')[0]
            for r in data['Rows']:
                r['Passage'] = passage
                rows.append(r)
    return names, rows


def explained_right(r):
    if r['State'] in PLACED:
        return r['CorrectOnSplitKey'] is True
    if r['State'] == 'supplied':
        return r['AbsenceCorrect'] is True
    return False


def judged_right(r):
    return r['CorrectOnJudgedKey'] is True


class Combiner:
    def __init__(self, path=WEIGHTS):
        self.document = json.load(open(path, encoding='utf-8'))
        self.features = self.document['features']

    def model(self, group):
        models = self.document['models']
        return models.get(group) or models['all']

    def raw(self, row):
        m = self.model(row['Group'])
        total = m['intercept']
        for w, v, mean, scale in zip(m['weights'], row['Features'], m['mean'], m['scale']):
            total += w * (v - mean) / scale
        return 1 / (1 + math.exp(-total))

    def calibrated(self, row):
        p = self.raw(row)
        for hi, rate in self.model(row['Group'])['calibration']:
            if p <= hi:
                return rate
        return self.model(row['Group'])['calibration'][-1][1]


def share(part, whole):
    return f'{part:,}/{whole:,} ({100 * part / whole if whole else 0:.2f}%)'
