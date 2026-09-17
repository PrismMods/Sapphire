import sys, os, math
SP=os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0,SP)
from adofai import load_adofai
from charttime4 import base_timeline
ROOT='/Users/preluminance/Documents/TUFLevels'
CASES=[('He He He (constant)','1881734557/main.adofai'),
       ("Heaven's Gateway (decel at end)",'353752589/main.adofai'),
       ('little witch (up, sharp down, up)','938698027/little witch in permafrost garden.adofai'),
       ('Hello BPM 2025 (accel/decel)','-1629159312/level.adofai'),
       ('PROFESS1ON (irregular)','2115474585/profess1on.adofai')]
for name,rel in CASES:
    j=load_adofai(os.path.join(ROOT,rel))
    if not j: print(name,'-- load failed'); continue
    r=base_timeline(j)
    if not r: print(name,'-- no timeline'); continue
    tl,end=r
    pts=[]; prev=None
    for t,b in tl:
        if prev is None or abs(math.log(b/prev))>0.005:
            pts.append((t,b)); prev=b
    print('%-36s base=%-8.2f end=%6.1fs  base changes=%d'%(name,tl[0][1],end,len(pts)-1))
    for t,b in pts[:12]: print('      %7.1fs  %9.2f'%(t,b))
    if len(pts)>12: print('      ... %d more'%(len(pts)-12))
