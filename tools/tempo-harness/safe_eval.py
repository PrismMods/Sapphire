"""Timestamp-free check: the curve's median over the 30s after the chart's offset, against
   the level's declared base BPM. No chart walk, so nothing here depends on the offline
   chart-timing reimplementation that turned out to be wrong."""
import sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, cs_curve as C, bins_eval as B, emit_eval as E
SP=os.path.dirname(os.path.abspath(__file__))
META=json.load(open(os.path.join(SP,'tuf150_meta.json')))
ENVS=np.load(os.path.join(SP,'tuf150_env.npy'))

def run(ef,lam,bins,name,win=8.0,step=3.0,band_hi=None):
    k=math.log(6.0)/(bins-1)
    grid=np.array([60.0*math.exp(i*k) for i in range(bins)]); lg=np.log(grid)
    oct_e=[]; band_e=[]
    for i,m in enumerate(META):
        nz=np.nonzero(ENVS[i])[0]
        if len(nz)<4000: continue
        env=ENVS[i][:nz[-1]+1].astype(np.float64); hop=m['hop']
        fl=C.logflux(env,hop)
        W=int(round(win/hop)); S=int(round(step/hop))
        count=max(1,(len(fl)-W)//max(1,S))
        if count<6: continue
        emit=np.array([ef(fl,hop,grid,t*S,min(len(fl),t*S+W)) for t in range(count)])
        mu=emit.mean()
        if mu>1e-12: emit=emit*(0.045/mu)
        p=B.viterbi(emit,lam,lg)
        p=p*C.band_factor(float(np.median(p)))
        a=int(max(0.0,m['offset']/1000.0)/step); b=min(len(p),a+int(30/step))
        if b<=a: continue
        got=float(np.median(p[a:b])); want=m['bpm']
        if not (40<=want<=2000): continue
        oct_e.append(E.octave_err(got,want))
        band_e.append(abs(math.log(C.band_factor(got)*got/(C.band_factor(want)*want))))
    o=np.array(oct_e); bd=np.array(band_e)
    print('%-26s lam=%-4s bins=%-4s n=%3d  oct<1%%=%4.1f%%  band<1%%=%4.1f%%'%(
        name,lam,bins,len(o),100*(o<0.01).mean(),100*(bd<0.01).mean()),flush=True)

def conc_only(fl,hop,grid,a,b): return C.conc_rows(fl,hop,grid,a,b)

if __name__=='__main__':
    run(conc_only,12.0,192,'conc (original)')
    run(B.emit_both,24.0,384,'conc*acf')
    run(B.emit_both,64.0,192,'conc*acf')
    run(B.emit_both,64.0,384,'conc*acf (shipped)')
    run(B.emit_both,100.0,384,'conc*acf')
