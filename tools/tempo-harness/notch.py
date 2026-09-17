"""Transition cost with a HARMONIC NOTCH instead of a distance knee.

   A knee taxes every big move, but a real tempo change can be big — PUS drops 262 to 100 —
   while the failure mode is specific: the path landing on a HARMONIC of where it was, because
   autocorrelation scores a beat and its double almost equally. So penalise proximity to a
   simple ratio, not distance:

       cost(d) = lam*d + oct * exp( -(minh |log(r/h)| / sigma)^2 / 2 ),  r = e^d, h in {2,3,4}

   The cost depends only on (j-i) on a log-uniform grid, so it is one kernel computed once and
   the decode is a lookup — O(bins^2) but with a trivial inner loop."""
import math
import numpy as np

def kernel(lg, lam, oct_, sigma=0.06, harmonics=(2.0, 3.0, 4.0)):
    B = len(lg); step = lg[1] - lg[0]
    d = np.abs(np.arange(-(B - 1), B) * step)
    base = lam * d
    hp = np.full(len(d), 1e9)
    for h in harmonics:
        hp = np.minimum(hp, np.abs(d - math.log(h)))
    notch = oct_ * np.exp(-(hp / sigma) ** 2 / 2.0)
    return base + notch      # index: (j-i) + (B-1)

def viterbi_n(emit, lg, lam, oct_, sigma=0.06):
    B = len(lg); count = len(emit)
    ker = kernel(lg, lam, oct_, sigma)
    off = B - 1
    dp = emit[0].copy()
    back = np.zeros((count, B), dtype=np.int32)
    # cost matrix rows: cost[j, i] = ker[j - i + off]
    idx = np.arange(B)
    M = ker[(idx[:, None] - idx[None, :]) + off]      # B x B, built once
    for t in range(1, count):
        v = dp[None, :] - M
        bi = np.argmax(v, axis=1)
        dp = v[idx, bi] + emit[t]
        back[t] = bi
    j = int(np.argmax(dp)); path = np.zeros(count)
    for t in range(count - 1, -1, -1):
        path[t] = math.exp(lg[j]); j = back[t][j]
    return path
