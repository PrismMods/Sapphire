"""Bin count at TIGHT tolerance, timestamp-free: the curve's reading shortly after the
   chart's offset against the level's declared base BPM. Quantisation is what is under test,
   so the tolerances are finer than the 1% used for the octave question."""
import sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, cs_curve as C, bins_eval as B, emit_eval as E
SP=os.path.dirname(os.path.abspath(__file__))
META=json.load(open(os.path.join(SP,'tuf150_meta.json')))
ENVS=np.load(os.path.join(SP,'tuf150_env.npy'))

def run(bins,lam=64.0,win=8.0,step=3.0):
    k=math.log(6.0)/(bins-1)
    grid=np.array([60.0*math.exp(i*k) for i in range(bins)]); lg=np.log(grid)
    errs=[]
    for i,m in enumerate(META):
        nz=np.nonzero(ENVS[i])[0]
        if len(nz)<4000: continue
        env=ENVS[i][:nz[-1]+1].astype(np.float64); hop=m['hop']
        fl=C.logflux(env,hop)
        W=int(round(win/hop)); S=int(round(step/hop))
        count=max(1,(len(fl)-W)//max(1,S))
        if count<6: continue
        emit=np.array([B.emit_both(fl,hop,grid,t*S,min(len(fl),t*S+W)) for t in range(count)])
        mu=emit.mean()
        if mu>1e-12: emit=emit*(0.045/mu)
        p=B.viterbi(emit,lam,lg)
        p=p*C.band_factor(float(np.median(p)))
        a=int(max(0.0,m['offset']/1000.0)/step); b=min(len(p),a+int(30/step))
        if b<=a: continue
        got=float(np.median(p[a:b])); want=m['bpm']
        if not (40<=want<=2000): continue
        errs.append(E.octave_err(got,want))
    e=np.array(errs)
    print('bins=%-4d step=%.2f%%  n=%3d  <0.2%%=%4.1f%%  <0.5%%=%4.1f%%  <1%%=%4.1f%%'%(
        bins,100*(math.exp(k)-1),len(e),
        100*(e<0.002).mean(),100*(e<0.005).mean(),100*(e<0.01).mean()),flush=True)

if __name__=='__main__':
    for b in (192,384,768):
        run(b)
