"""Vectorised emission + the real-PUS metric, absolute (not octave-forgiving)."""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, cs_curve as C
from pus_gt import gt_at

def conc_all(seg, t, grid):
    """|resultant| of the flux folded on each candidate period, all bins at once."""
    tot=seg.sum()
    if tot<=0: return np.zeros(len(grid))
    ph=2*math.pi*np.outer(t,1.0/(60.0/grid))
    cs=(seg[:,None]*np.cos(ph)).sum(axis=0)
    sn=(seg[:,None]*np.sin(ph)).sum(axis=0)
    return np.hypot(cs,sn)/tot

def acf_all(seg, hop, grid, harm=(1,2,3,4)):
    x=seg-seg.mean(); n=len(x)
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
    dp=emit[0].copy(); back=np.zeros((count,B),dtype=np.int32)
    ar=np.arange(B)
    for t in range(1,count):
        a=dp+lam*lg
        f=np.maximum.accumulate(a)
        fi=np.maximum.accumulate(np.where(a>=f-1e-12,ar,0))
        b=dp-lam*lg
        rb=np.maximum.accumulate(b[::-1])[::-1]
        ri=np.minimum.accumulate(np.where(b>=rb-1e-12,ar,B-1)[::-1])[::-1]
        vf=f-lam*lg; vb=rb+lam*lg; take=vf>=vb
        dp=np.where(take,vf,vb)+emit[t]
        back[t]=np.where(take,fi,ri)
    j=int(np.argmax(dp)); path=np.zeros(count)
    for t in range(count-1,-1,-1): path[t]=math.exp(lg[j]); j=back[t][j]
    return path

def curve(env,hop,win,step,bins,lam,lo=60.0,hi=360.0):
    fl=C.logflux(env,hop)
    k=math.log(hi/lo)/(bins-1)
    grid=np.array([lo*math.exp(i*k) for i in range(bins)]); lg=np.log(grid)
    W=int(round(win/hop)); S=int(round(step/hop))
    count=max(1,(len(fl)-W)//max(1,S))
    emit=np.empty((count,bins))
    for i in range(count):
        a=i*S; b=min(len(fl),a+W)
        seg=fl[a:b]; t=np.arange(a,b)*hop
        emit[i]=conc_all(seg,t,grid)*acf_all(seg,hop,grid)
    mu=emit.mean()
    if mu>1e-12: emit*= (0.045/mu)
    p=viterbi(emit,lam,lg)
    return p*C.band_factor(float(np.median(p))), win, step

def sample(p,win,step,t):
    x=(t-win*0.5)/step
    if x<=0: return p[0]
    i=int(x)
    if i>=len(p)-1: return p[-1]
    return p[i]+(p[i+1]-p[i])*(x-i)

def score(p,win,step):
    ts=np.arange(10,425,2.0)
    abs_e=[];oct_e=[]
    for t in ts:
        g=sample(p,win,step,t); w=gt_at(t)
        abs_e.append(abs(math.log(g/w)))
        r=math.log(g/w)/math.log(2.0)
        oct_e.append(abs(r-round(r))*math.log(2.0))
    return np.array(abs_e), np.array(oct_e)
