"""Chart BASE tempo over time — what the music does, not what the tiles do.

   charttime3 reports the effective tile rate, which is not the tempo: He He He holds 126.5 BPM
   throughout while its tile rate walks 126.5 -> 253 -> 506 -> 1012 -> 2024. Every one of those
   is a power of two, i.e. a subdivision. Labels built from the tile rate punish a correct curve
   for staying still.

   So each change is classified the way EditorChartAnalysis does it, measured over 31,220
   multipliers in 429 published charts:
     - a power of two is a subdivision            -> the base did not move
     - a simple 180/angle ratio is a magic shape  -> the base did not move
     - anything else                              -> the base moved by that ratio
"""
import math
from charttime3 import chart_timeline

MAGIC=[3.0,1.5,4.0/3.0,6.0/5.0,9.0/8.0,5.0/4.0,5.0/3.0,7.0/4.0,12.0/5.0,8.0/5.0]

def is_subdivision(r):
    if r<=0: return True
    l=math.log(r,2)
    return abs(l-round(l))<0.004

def is_magic(r):
    if r<=0: return True
    l=math.log(r,2)
    folded=r/(2.0**round(l))
    return any(abs(folded-m)<0.01 or abs(folded-1.0/m)<0.01 for m in MAGIC)

def base_timeline(j):
    out=chart_timeline(j)
    if not out: return None
    tl,end=out
    if not tl: return None
    base=tl[0][1]; prev_eff=tl[0][1]
    res=[(tl[0][0], base)]
    for t,eff in tl[1:]:
        if eff<=0 or prev_eff<=0: continue
        r=eff/prev_eff
        if not is_subdivision(r) and not is_magic(r):
            base*=r
            res.append((t,base))
        prev_eff=eff
    return res, end
