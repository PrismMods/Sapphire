"""Which local-tempo EMISSION gives the most accurate curve? Ground truth = the level's
   declared base BPM, compared modulo octaves (a chart at 300 and a curve at 150 agree)."""
import sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, cs_curve as C

SP=os.path.dirname(os.path.abspath(__file__))
LO,HI,BINS=60.0,360.0,192
K=math.log(HI/LO)/(BINS-1)
GRID=np.array([LO*math.exp(i*K) for i in range(BINS)])
LG=np.log(GRID)

def emit_conc(fl,hop,a,b):
    return C.conc_rows(fl,hop,GRID,a,b)

def emit_acf(fl,hop,a,b,harm=(1,2,3,4)):
    """Windowed autocorrelation with a harmonic sum: a real beat period also has energy
       at its multiples, which demotes the double-time reading a bare peak picks."""
    x=fl[a:b]-fl[a:b].mean()
    n=len(x)
    if n<16: return np.zeros(BINS)
    F=np.fft.rfft(x,2*n)
    ac=np.fft.irfft(F*np.conj(F))[:n]
    if ac[0]<=0: return np.zeros(BINS)
    ac=ac/ac[0]
    lags=60.0/GRID/hop
    out=np.zeros(BINS)
    for h in harm:
        l=lags*h
        i0=np.floor(l).astype(int); fr=l-i0
        ok=(i0>=1)&(i0+1<n)
        v=np.zeros(BINS)
        v[ok]=ac[i0[ok]]*(1-fr[ok])+ac[i0[ok]+1]*fr[ok]
        out+=v/h
    return np.maximum(out,0.0)

def emit_both(fl,hop,a,b):
    return emit_conc(fl,hop,a,b)*emit_acf(fl,hop,a,b)

def viterbi(emit,lam):
    count=len(emit)
    dp=emit[0].copy(); back=np.zeros((count,BINS),dtype=int)
    for t in range(1,count):
        a=dp+lam*LG
        f=np.maximum.accumulate(a); fi=np.zeros(BINS,dtype=int); bi=0
        for i in range(BINS):
            if a[i]>=a[bi]: bi=i
            fi[i]=bi
        b=dp-lam*LG
        rb=np.maximum.accumulate(b[::-1])[::-1]; ri=np.zeros(BINS,dtype=int); bi=BINS-1
        for i in range(BINS-1,-1,-1):
            if b[i]>=b[bi]: bi=i
            ri[i]=bi
        vf=f-lam*LG; vb=rb+lam*LG
        take=vf>=vb
        dp=np.where(take,vf,vb)+emit[t]
        back[t]=np.where(take,fi,ri)
    j=int(np.argmax(dp)); path=np.zeros(count)
    for t in range(count-1,-1,-1):
        path[t]=GRID[j]; j=back[t][j]
    return path

def curve(env,hop,ef,lam,win=8.0,step=3.0):
    fl=C.logflux(env,hop)
    W=int(round(win/hop)); S=int(round(step/hop))
    count=max(1,(len(fl)-W)//max(1,S))
    emit=np.array([ef(fl,hop,t*S,min(len(fl),t*S+W)) for t in range(count)])
    # scale each variant to a comparable emission range so one lambda means the same thing
    m=emit.max()
    if m>0: emit=emit/m*0.3
    return viterbi(emit,lam),step

def octave_err(got,want):
    if got<=0 or want<=0: return 9.9
    r=math.log(got/want)/math.log(2.0)
    return abs(r-round(r))*math.log(2.0)      # |log ratio| to the nearest octave

def run(ef,lam,name,limit=None):
    meta=json.load(open(os.path.join(SP,'tuf150_meta.json')))
    envs=np.load(os.path.join(SP,'tuf150_env.npy'))
    errs=[]
    for i,m in enumerate(meta[:limit] if limit else meta):
        env=envs[i]; env=env[env>0].shape[0] and envs[i]
        nz=np.nonzero(envs[i])[0]
        if len(nz)<4000: continue
        env=envs[i][:nz[-1]+1].astype(np.float64)
        hop=m['hop']
        try: path,step=curve(env,hop,ef,lam)
        except Exception: continue
        t0=max(0.0,m['offset']/1000.0)
        a=int(t0/step); b=min(len(path),a+int(30/step))
        if b<=a: continue
        got=float(np.median(path[a:b]))
        errs.append(octave_err(got,m['bpm']))
    e=np.array(errs)
    print('%-12s lam=%-5s n=%3d  <1%%=%4.1f%%  <3%%=%4.1f%%  median=%.2f%%'%(
        name,lam,len(e),100*(e<0.01).mean(),100*(e<0.03).mean(),100*np.median(e)))
    return e

if __name__=='__main__':
    lim=int(sys.argv[1]) if len(sys.argv)>1 else None
    for name,ef in (('conc',emit_conc),('acf',emit_acf),('conc*acf',emit_both)):
        for lam in (4.0,12.0):
            run(ef,lam,name,lim)
