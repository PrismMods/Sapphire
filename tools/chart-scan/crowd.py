"""Pass 3 — what piles up on crowded tiles (>=10 non-twirl events) and the shapes of MoveCamera
use (which properties, set vs tween, relativeTo). Reads out/scan.json."""
import os, json, collections
from walk import load_adofai
from scan2_dedup import unique

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
crowd = collections.Counter(); crowdN = 0
cam = collections.Counter(); camN = 0; pair = 0
for m in unique(json.load(open(os.path.join(OUT, 'scan.json')))):
    d = load_adofai(m['path'])
    pf = collections.defaultdict(list)
    for e in d.get('actions', []):
        if e.get('eventType') != 'Twirl': pf[e.get('floor', 0)].append(e)
    for l in pf.values():
        if len(l) >= 10:
            crowd.update(e.get('eventType') for e in l); crowdN += len(l)
        mc = [e for e in l if e.get('eventType') == 'MoveCamera']
        for e in mc:
            camN += 1
            props = tuple(k for k in ('position', 'rotation', 'zoom') if e.get(k) not in (None, [None, None]))
            cam[(props, 'set' if float(e.get('duration', 1) or 0) == 0 else 'tween', e.get('relativeTo'))] += 1
        if any(float(e.get('duration', 1) or 0) == 0 for e in mc) and any(float(e.get('duration', 1) or 0) > 0 for e in mc):
            pair += 1
print('crowded-tile mix', [(k, round(100 * v / crowdN, 1)) for k, v in crowd.most_common(8)])
print('MoveCamera', camN, [(k, round(100 * v / camN, 1)) for k, v in cam.most_common(8)])
print('tiles with a set+tween camera pair', pair)
