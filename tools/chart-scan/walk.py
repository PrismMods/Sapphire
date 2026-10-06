"""Chart walker: per-tile charter angle, spin and running bpm. Shared by every scan here.

Lighter than tempo-harness/charttime3.py (which follows the game's own timing code): no pauses,
holds or free-roam. Good for pattern statistics, not for exact song time."""
import os, sys, collections
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'tempo-harness'))
from adofai import load_adofai
PATH={'R':0,'p':15,'J':30,'E':45,'T':60,'o':75,'U':90,'q':105,'G':120,'Q':135,'H':150,'W':165,'L':180,'x':195,'N':210,'Z':225,'F':240,'V':255,'D':270,'Y':285,'B':300,'C':315,'M':330,'A':345,'!':999,
      '5':555,'6':666,'7':777,'8':888}
def angles(d):
    if d.get('angleData') is not None: return [float(a) for a in d['angleData']]
    pd=d.get('pathData')
    if pd is None: return None
    out=[]
    for ch in pd:
        out.append(float(PATH.get(ch,0)))
    return out
def walk(d):
    """returns list of dicts per floor 1..N: rel (charter degrees, 0..360], bpm, t (seconds of hit)"""
    a=angles(d); s=d['settings']; base=float(s.get('bpm',100) or 100)
    ev=collections.defaultdict(list)
    for e in d.get('actions',[]): ev[e.get('floor',0)].append(e)
    bpm=base; ccw=False; t=0.0; rows=[]
    prev=a[0] if a else 0
    # floor 0 events
    for f in range(0,len(a)+1):
        for e in ev.get(f,[]):
            et=e.get('eventType')
            if et=='Twirl': ccw=not ccw
            elif et=='SetSpeed':
                if e.get('speedType','Bpm')=='Multiplier': bpm*=float(e.get('bpmMultiplier',1) or 1)
                else: bpm=float(e.get('beatsPerMinute',bpm) or bpm)
        if f==len(a): break
        cur=a[f]
        if cur==999: rel=None
        else:
            inc=(prev+180) if f>0 else 0  # entry heading reversed
            if f==0: rel=180.0
            else:
                p=prev if prev!=999 else None
                if p is None: rel=None
                else:
                    r=((p+180-cur) % 360) if not ccw else ((cur-(p+180)) % 360)
                    rel = r if r>1e-6 else 360.0
        rows.append({'f':f,'abs':cur,'rel':rel,'bpm':bpm,'ccw':ccw,'ev':ev.get(f,[])})
        if cur!=999: prev=cur
        else: ccw = not ccw; prev=(prev+180)%360 if prev is not None else 0
    return rows, base
