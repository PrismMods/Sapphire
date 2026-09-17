"""Beat tracking by dynamic programming, with a VARIABLE period.

   The windowed tempo estimator cannot resolve a change shorter than its window — eight seconds
   on the defaults — which is structurally why PUS's nine-second dips are missed however the
   scoring is tuned. madmom does not have that limit because it does not estimate tempo in
   windows at all: it locates individual BEATS and reads the tempo off the intervals between
   them. That idea costs nothing and needs no network.

   Ellis (2007) does this for a FIXED period. Generalised to a varying one, the state is
   (frame, period) and the recurrence is

       D[t,p] = onset[t] + max over p' of ( D[t-p, p'] - lam * |log(p/p')| )

   i.e. a beat at t with period p follows a beat one period earlier at some period p', paying
   for any change in period. Backtracking gives the beat times; the tempo is 60/period along
   the path, which is continuous rather than quantised to a window.

   Cost is frames x periods x neighbours, and the neighbour span can be small because the
   period changes slowly — a few million operations for a seven-minute song."""
import math
import numpy as np

def periods_grid(lo_bpm=55.0, hi_bpm=500.0, n=96, hop=0.01):
    bpm = np.array([lo_bpm*math.exp(i*math.log(hi_bpm/lo_bpm)/(n-1)) for i in range(n)])
    return np.maximum(2, np.round(60.0/bpm/hop).astype(int)), bpm

def track(onset, hop=0.01, lo_bpm=55.0, hi_bpm=500.0, nper=96, lam=40.0, span=6):
    """-> (beat_frames, period_frames_at_each_beat)"""
    o = np.asarray(onset, dtype=np.float64)
    o = o / max(1e-9, o.max())
    P, BPM = periods_grid(lo_bpm, hi_bpm, nper, hop)
    T = len(o); K = len(P)
    lgP = np.log(P.astype(np.float64))
    NEG = -1e18
    D = np.full((T, K), NEG)
    B = np.zeros((T, K), dtype=np.int32)
    # seed: the first period-length of frames can start a track freely
    first = int(P.max())
    D[:first, :] = o[:first, None]
    for t in range(first, T):
        prev = t - P                      # frame of the preceding beat, per period
        col = D[prev, :]                  # K x K would be huge; restrict to a neighbourhood
        best = np.full(K, NEG); arg = np.zeros(K, dtype=np.int32)
        for k in range(K):
            a = max(0, k-span); b = min(K, k+span+1)
            cand = D[prev[k], a:b] - lam*np.abs(lgP[k]-lgP[a:b])
            j = int(np.argmax(cand))
            best[k] = cand[j]; arg[k] = a+j
        D[t] = o[t] + best
        B[t] = arg
    # backtrack from the best final state
    t = int(np.argmax(D[:, :].max(axis=1)))
    k = int(np.argmax(D[t]))
    beats=[]; pers=[]
    while t >= first:
        beats.append(t); pers.append(P[k])
        nk = B[t, k]; t = t - P[k]; k = nk
    beats.reverse(); pers.reverse()
    return np.array(beats), np.array(pers)

def tempo_curve(beats, pers, hop=0.01, smooth=5):
    """BPM at each beat, median-smoothed over neighbouring beats."""
    if len(beats) < 2: return np.array([]), np.array([])
    t = beats*hop
    bpm = 60.0/(pers*hop)
    out = np.copy(bpm)
    for i in range(len(bpm)):
        a=max(0,i-smooth//2); b=min(len(bpm),i+smooth//2+1)
        out[i]=np.median(bpm[a:b])
    return t, out

def sample(t, bpm, at):
    if len(t)==0: return -1.0
    i=int(np.searchsorted(t, at))
    return float(bpm[min(max(i,0),len(bpm)-1)])
