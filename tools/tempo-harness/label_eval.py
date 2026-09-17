"""Our shipped curve against the 39 labelled multi-tempo charts — the first time this has been
   measurable on more than one shifting song."""
import sys, os, json, math, subprocess
SP=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,SP)
import numpy as np, cs_curve as C, fast, twoslope
from charttime3 import tempo_at
ROOT='/Users/preluminance/Documents/TUFLevels'
idx={e['level']:e for e in json.load(open(SP+'/tuf_index.json'))}
labels=json.load(open(SP+'/tuf_labels.json'))
BINS=448; K=math.log(8.0)/(BINS-1)
GRID=np.array([60.0*math.exp(i*K) for i in range(BINS)]); LG=np.log(GRID)
WIN,STEP=8.0,3.0
def curve(env,hop):
    fl=C.logflux(env,hop)
    W=int(round(WIN/hop)); S=int(round(STEP/hop))
    n=max(1,(len(fl)-W)//max(1,S))
    if n<6: return None
    e=np.empty((n,BINS))
    for i in range(n):
        a=i*S; b=min(len(fl),a+W); seg=fl[a:b]; t=np.arange(a,b)*hop
        e[i]=fast.conc_all(seg,t,GRID)*fast.acf_all(seg,hop,GRID)
    mu=e.mean()
    if mu>1e-12: e*=(0.045/mu)
    p=twoslope.viterbi2(e,12.0,LG,extra=36.0,knee=math.log(1.35))
    return p*C.band_factor(float(np.median(p)))
oct_h=oct_n=0; band_h=band_n=0; per=[]
done=0
for L in labels:
    tl=[(t,b) for t,b in L['tl']]
    if len({round(b,3) for _,b in tl})<2: continue
    e=idx.get(L['lvl'])
    if not e: continue
    song=os.path.join(ROOT,e['song'])
    if not os.path.exists(song): continue
    try:
        env,hop=C.envelope(song,maxsec=200)
        p=curve(env,hop)
    except Exception: continue
    if p is None: continue
    h=n=0
    t=max(0.0,e['offset']/1000.0)+WIN*0.5
    while t<min(L['dur'],200.0)-WIN:
        w=tempo_at(tl,t)
        if 40<=w<=2000:
            g=fast.sample(p,WIN,STEP,t)
            r=math.log(g/w)/math.log(2.0)
            if abs(r-round(r))*math.log(2.0)<0.03: oct_h+=1; h+=1
            oct_n+=1; n+=1
            if abs(math.log(C.band_factor(g)*g/(C.band_factor(w)*w)))<0.03: band_h+=1
            band_n+=1
        t+=3.0
    if n: per.append((h/n, L['lvl'], e['song'], n))
    done+=1
    if done%8==0: print(' %d songs'%done,flush=True)
print('OUR CURVE vs %d labelled multi-tempo charts'%done)
print('  samples within 3%%: octave-invariant %.1f%%   band (displayed) %.1f%%'%(
    100*oct_h/max(1,oct_n), 100*band_h/max(1,band_n)))
vals=np.array([x[0] for x in per])
print('  per-song mean %.1f%%   songs tracked >70%% of the time: %d/%d'%(
    100*vals.mean(), int((vals>0.7).sum()), len(per)))
per.sort()
print()
print('WORST TRACKED:')
for frac,lvl,song,n in per[:14]:
    print('  %5.1f%%  %-46s  song: %s'%(100*frac, lvl, song))
import json as _j
_j.dump([{'tracked':x[0],'level':x[1],'song':x[2],'samples':x[3]} for x in per],
        open('/private/tmp/claude-501/-Users-preluminance-Coding-Sapphire/1431b25b-72c0-4ad6-81d7-16d60871c10a/scratchpad/tuf_tracking.json','w'), indent=1)
