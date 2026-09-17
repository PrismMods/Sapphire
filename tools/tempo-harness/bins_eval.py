"""Does a finer tempo grid actually make the curve more accurate?
   Same corpus + metric as full_eval4, with a tighter tolerance so quantisation shows."""
import sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, cs_curve as C
from gt import tempo_at
SP=os.path.dirname(os.path.abspath(__file__))
META=json.load(open(os.path.join(SP,'tuf150_meta.json')))
ENVS=np.load(os.path.join(SP,'tuf150_env.npy'))
GT=json.load(open(os.path.join(SP,'tuf150_gt.json')))
MEAN=0.045; LO,HI=60.0,360.0

def emit_both(fl,hop,grid,a,b):
    return C.conc_rows(fl,hop,grid,a,b)*acf(fl,hop,grid,a,b)

def acf(fl,hop,grid,a,b,harm=(1,2,3,4)):
    x=fl[a:b]-fl[a:b].mean(); n=len(x)
    if n<16: return np.zeros(len(grid))
    F=np.fft.rfft(x,2*n); ac=np.fft.irfft(F*np.conj(F))[:n]
    if ac[0]<=0: return np.zeros(len(grid))
    ac=ac/ac[0]
    lags=60.0/grid/hop; out=np.zeros(len(grid))
    for h in harm:
        l=lags*h; i0=np.floor(l).astype(int); fr=l-i0
        ok=(i0>=1)&(i0+1<n); v=np.zeros(len(grid))
        v[ok]=ac[i0[ok]]*(1-fr[ok])+ac[i0[ok]+1]*fr[ok]
        out+=v/h
    return np.maximum(out,0.0)

def viterbi(emit,lam,lg):
    B=len(lg); count=len(emit)
    dp=emit[0].copy(); back=np.zeros((count,B),dtype=int)
    for t in range(1,count):
        a=dp+lam*lg
        f=np.maximum.accumulate(a); fi=np.zeros(B,dtype=int); bi=0
        for i in range(B):
            if a[i]>=a[bi]: bi=i
            fi[i]=bi
        b=dp-lam*lg
        rb=np.maximum.accumulate(b[::-1])[::-1]; ri=np.zeros(B,dtype=int); bi=B-1
        for i in range(B-1,-1,-1):
            if b[i]>=b[bi]: bi=i
            ri[i]=bi
        vf=f-lam*lg; vb=rb+lam*lg; take=vf>=vb
        dp=np.where(take,vf,vb)+emit[t]; back[t]=np.where(take,fi,ri)
    j=int(np.argmax(dp)); path=np.zeros(count)
    for t in range(count-1,-1,-1): path[t]=np.exp(lg[j]); j=back[t][j]
    return path

def run(bins,lam=64.0,win=8.0,step=3.0):
    k=math.log(HI/LO)/(bins-1)
    grid=np.array([LO*math.exp(i*k) for i in range(bins)]); lg=np.log(grid)
    acc={t:[0,0] for t in (0.002,0.005,0.01)}
    for i,m in enumerate(META):
        tl=GT[i]
        if not tl: continue
        nz=np.nonzero(ENVS[i])[0]
        if len(nz)<4000: continue
        env=ENVS[i][:nz[-1]+1].astype(np.float64); hop=m['hop']
        fl=C.logflux(env,hop)
        W=int(round(win/hop)); S=int(round(step/hop))
        count=max(1,(len(fl)-W)//max(1,S))
        if count<6: continue
        emit=np.array([emit_both(fl,hop,grid,t*S,min(len(fl),t*S+W)) for t in range(count)])
        mu=emit.mean()
        if mu>1e-12: emit=emit*(MEAN/mu)
        path=viterbi(emit,lam,lg)
        path=path*C.band_factor(float(np.median(path)))
        for kk in range(int(max(0.0,m['offset']/1000.0)/step),count):
            w=tempo_at(tl,kk*step+win*0.5)
            if not (40<=w<=2000): continue
            g=float(path[kk])
            e=abs(math.log(g*C.band_factor(g)/(w*C.band_factor(w))))
            for tol in acc:
                acc[tol][0]+= 1 if e<tol else 0; acc[tol][1]+=1
    print('bins=%-4d  step=%.2f%%   <0.2%%=%4.1f%%  <0.5%%=%4.1f%%  <1%%=%4.1f%%'%(
        bins,100*(math.exp(k)-1),*[100*acc[t][0]/max(1,acc[t][1]) for t in (0.002,0.005,0.01)]),flush=True)

if __name__=='__main__':
    for b in (96,192,384,768):
        run(b)
