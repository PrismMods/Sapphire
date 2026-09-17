"""Two-slope transition cost.

   One linear slope cannot serve both jobs. Low lambda follows a real shift promptly but also
   lets the path flip octave whenever the emission's harmonic twin is momentarily stronger;
   high lambda stops the flipping and arrives eleven seconds late. They are different kinds of
   move: a tempo change is a modest ratio, an octave flip is a factor near two. So charge them
   differently — lambda per unit log ratio out to a knee, and lambda+extra beyond it.

   cost(d) = lam*d + extra*max(0, d - knee)

   Still O(bins): beyond the knee the slope is constant, so prefix/suffix running maxima
   answer it; inside the knee it is a bounded window, which a monotonic deque answers."""
import math
import numpy as np
from collections import deque

def viterbi2(emit, lam, lg, extra=0.0, knee=math.log(1.35)):
    B = len(lg); count = len(emit)
    step = lg[1] - lg[0]
    K = max(1, int(round(knee / step)))
    far = lam + extra
    off = extra * knee
    dp = emit[0].copy()
    back = np.zeros((count, B), dtype=np.int32)
    ar = np.arange(B)
    for t in range(1, count):
        best = np.full(B, -1e30); bidx = np.zeros(B, dtype=np.int32)
        # --- far field, i <= j-K : value = (dp[i] + far*lg[i]) - far*lg[j] + off
        a = dp + far * lg
        pm = np.maximum.accumulate(a); pi = np.maximum.accumulate(np.where(a >= pm - 1e-12, ar, 0))
        j = np.arange(K, B)
        if len(j):
            v = pm[j - K] - far * lg[j] + off
            m = v > best[j]
            best[j[m]] = v[m]; bidx[j[m]] = pi[j - K][m]
        # --- far field, i >= j+K
        b = dp - far * lg
        sm = np.maximum.accumulate(b[::-1])[::-1]
        si = np.minimum.accumulate(np.where(b >= sm - 1e-12, ar, B - 1)[::-1])[::-1]
        j = np.arange(0, B - K)
        if len(j):
            v = sm[j + K] + far * lg[j] + off
            m = v > best[j]
            best[j[m]] = v[m]; bidx[j[m]] = si[j + K][m]
        # --- near field, |i-j| < K : slope lam, bounded window -> monotonic deque
        # forward half (i <= j): maximise dp[i] + lam*lg[i] over i in [j-K+1, j]
        f = dp + lam * lg
        dq = deque()
        for jj in range(B):
            while dq and dq[0] < jj - K + 1: dq.popleft()
            while dq and f[dq[-1]] <= f[jj]: dq.pop()
            dq.append(jj)
            v = f[dq[0]] - lam * lg[jj]
            if v > best[jj]: best[jj] = v; bidx[jj] = dq[0]
        # backward half (i >= j): maximise dp[i] - lam*lg[i] over i in [j, j+K-1]
        g = dp - lam * lg
        dq = deque()
        for jj in range(B - 1, -1, -1):
            while dq and dq[0] > jj + K - 1: dq.popleft()
            while dq and g[dq[-1]] <= g[jj]: dq.pop()
            dq.append(jj)
            v = g[dq[0]] + lam * lg[jj]
            if v > best[jj]: best[jj] = v; bidx[jj] = dq[0]
        dp = best + emit[t]
        back[t] = bidx
    j = int(np.argmax(dp)); path = np.zeros(count)
    for t in range(count - 1, -1, -1):
        path[t] = math.exp(lg[j]); j = back[t][j]
    return path
