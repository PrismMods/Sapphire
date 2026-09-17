"""Numpy mirror of AudioAnalysis.CurveJob (C#) — for measuring changes offline."""
import sys, os, math, subprocess, array
import numpy as np

SR=8000; WIN=80   # C#: Win = frequency/100 -> 10ms hop

def envelope(path, maxsec=600):
    r=subprocess.run(['ffmpeg','-v','quiet','-i',path,'-t',str(maxsec),'-ac','1',
                      '-ar',str(SR),'-f','s16le','-'],capture_output=True,timeout=900).stdout
    a=np.frombuffer(r[:len(r)//2*2],dtype='<i2').astype(np.float64)/32768.0
    n=len(a)//WIN
    a=a[:n*WIN].reshape(n,WIN)
    return np.sqrt((a*a).mean(axis=1)), WIN/float(SR)

def logflux(env,hop,lag=0.012):
    k=max(1,int(round(lag/hop))); eps=1e-5
    l=np.log(env+eps); fl=np.zeros_like(env)
    fl[k:]=np.maximum(l[k:]-l[:-k],0.0)
    return fl

def conc_rows(fl,hop,grid,a,b,bins=64):
    """Concentration of every tempo in `grid` over fl[a:b]."""
    seg=fl[max(1,a):b]
    if len(seg)==0: return np.zeros(len(grid))
    t=(np.arange(max(1,a),b)*hop)
    out=np.empty(len(grid))
    for gi,bpm in enumerate(grid):
        cr=60.0/bpm
        if cr<=hop: out[gi]=0.0; continue
        ph=np.mod(t,cr)/cr*2*math.pi
        tot=seg.sum()
        if tot<=0: out[gi]=0.0; continue
        # C# histograms into 64 bins first; the bin quantisation is a negligible
        # smoothing of the same resultant, so compute the resultant directly.
        out[gi]=math.hypot((seg*np.cos(ph)).sum(),(seg*np.sin(ph)).sum())/tot
    return out

def fold_octave(b, ref):
    if b<=0 or ref<=0: return b
    while b < ref/1.4142: b*=2.0
    while b > ref*1.4142: b/=2.0
    return b

BAND_LO, BAND_HI = 80.0, 300.0*1.05
def band_factor(bpm):
    if bpm<=0: return 1.0
    f=1.0
    while bpm*f < BAND_LO: f*=2.0
    while bpm*f > BAND_HI: f*=0.5
    return f

def global_tempo(fl,hop,limit_s=40.0):
    """Mirror of C# Tempo(): ACF + harmonic sum over the first `limit_s`, then a
       phase-concentration sweep around the winner."""
    n=min(len(fl),int(round(limit_s/hop)))
    if n<64: return None
    x=fl[:n]-fl[:n].mean()
    loLag=max(2,int(round(60.0/240.0/hop))); hiLag=min(n//2,int(round(60.0/60.0/hop)))
    if hiLag<=loLag: return None
    ac=np.zeros(hiLag+1)
    for lag in range(loLag,hiLag+1):
        ac[lag]=(x[lag:]*x[:-lag]).sum()/(n-lag)
    best=(-1e18,loLag)
    for lag in range(loLag,hiLag+1):
        s=ac[lag]
        for k in (2,3,4):
            if lag*k<=hiLag: s+=ac[lag*k]/k
        if s>best[0]: best=(s,lag)
    c=60.0/(best[1]*hop)
    cands=[]
    for base in (c,c*2,c*0.5):
        if 60<=base<=360:
            x0=base*0.94
            while x0<=base*1.06:
                cands.append(x0); x0*=1.0007
    if not cands: return None
    sc=conc_rows(fl,hop,cands,0,n)
    i=int(np.argmax(sc))
    return cands[i], float(sc[i])

def curve(env,hop,win_s=8.0,step_s=3.0,bins=192,lam=12.0,lo=60.0,hi=360.0,
          per_point_fold=True, band=True, gref=None):
    fl=logflux(env,hop)
    W=int(round(win_s/hop)); S=int(round(step_s/hop))
    count=max(1,(len(fl)-W)//max(1,S))
    k=math.log(hi/lo)/(bins-1)
    grid=np.array([lo*math.exp(i*k) for i in range(bins)])
    lg=np.log(grid)
    emit=np.array([conc_rows(fl,hop,grid,t*S,min(len(fl),t*S+W)) for t in range(count)])
    dp=emit[0].copy(); back=np.zeros((count,bins),dtype=int)
    for t in range(1,count):
        # linear cost is monotone in log-distance -> running max in each direction
        a=dp+lam*lg
        f=np.maximum.accumulate(a); fi=np.zeros(bins,dtype=int)
        bi=0
        for i in range(bins):
            if a[i]>=a[bi]: bi=i
            fi[i]=bi
        b=dp-lam*lg
        rb=np.maximum.accumulate(b[::-1])[::-1]; ri=np.zeros(bins,dtype=int)
        bi=bins-1
        for i in range(bins-1,-1,-1):
            if b[i]>=b[bi]: bi=i
            ri[i]=bi
        vf=f-lam*lg; vb=rb+lam*lg
        take=vf>=vb
        dp=np.where(take,vf,vb)+emit[t]
        back[t]=np.where(take,fi,ri)
    j=int(np.argmax(dp))
    path=np.zeros(count)
    for t in range(count-1,-1,-1):
        path[t]=grid[j]; j=back[t][j]
    if per_point_fold and gref: path=np.array([fold_octave(p,gref) for p in path])
    if band:
        bf=band_factor(float(np.median(path)))
        if bf!=1.0: path=path*bf
    return path
