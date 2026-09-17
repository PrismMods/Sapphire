import sys, os, json, math
SP=os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0,SP)
import numpy as np, cs_curve as C, fast, twoslope
from charttime3 import tempo_at
ROOT='/Users/preluminance/Documents/TUFLevels'
labels=json.load(open(SP+'/tuf_labels2.json'))
idx={e['level']:e for e in json.load(open(SP+'/tuf_index.json'))}
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
oh=on=bh=bn=0; per=[]
for L in labels:
    e=idx.get(L['lvl'])
    if not e: continue
    song=os.path.join(ROOT,L['song'])
    if not os.path.exists(song): continue
    try:
        env,hop=C.envelope(song,maxsec=200); p=curve(env,hop)
    except Exception: continue
    if p is None: continue
    tl=[(t,b) for t,b in L['tl']]
    h=n=0
    t=max(0.0,e['offset']/1000.0)+WIN*0.5
    while t<min(L['dur'],200.0)-WIN:
        w=tempo_at(tl,t)
        if 40<=w<=2000:
            g=fast.sample(p,WIN,STEP,t)
            r=math.log(g/w)/math.log(2.0)
            ok=abs(r-round(r))*math.log(2.0)<0.03
            oh+=ok; on+=1; h+=ok; n+=1
            bh+=abs(math.log(C.band_factor(g)*g/(C.band_factor(w)*w)))<0.03; bn+=1
        t+=3.0
    if n: per.append((h/n,L['lvl'],L['changes']))
print('OUR CURVE vs %d base-tempo labelled charts'%len(per))
print('  octave-invariant %.1f%%   band %.1f%%'%(100*oh/max(1,on),100*bh/max(1,bn)))
v=np.array([x[0] for x in per])
print('  per-song mean %.1f%%   >70%%: %d/%d'%(100*v.mean(),int((v>0.7).sum()),len(v)))
mv=[x for x in per if x[2]>0]; cs=[x for x in per if x[2]==0]
if mv: print('  shifting songs (%d): mean %.1f%%'%(len(mv),100*np.mean([x[0] for x in mv])))
if cs: print('  constant songs (%d): mean %.1f%%'%(len(cs),100*np.mean([x[0] for x in cs])))
per.sort()
print('\nWORST:')
for f,l,c in per[:10]: print('  %5.1f%%  changes=%-4d %s'%(100*f,c,l))
json.dump([{'tracked':x[0],'level':x[1],'changes':x[2]} for x in sorted(per)],
          open(SP+'/tuf_tracking2.json','w'),indent=1)
