"""PUS ground truth, read off Phoenixfisch's plot of Camellia's own MIDI (r/Camellia).
   Breakpoints in (seconds, BPM); the curve is piecewise linear between them."""
GT=[(0,150),(52,150),(56,110),(72,110),(75,140),(79,175),(82,175),(132,175),
    (135,135),(144,135),(150,160),(156,198),(165,200),(172,225),(180,262),
    (185,262),(216,262),(220,100),(225,100),(230,145),(235,150),(260,150),
    (270,160),(278,220),(285,220),(288,222),(360,300),(365,360),(368,435),
    (372,440),(405,440),(410,190),(415,190),(432,190)]

def gt_at(t):
    if t<=GT[0][0]: return GT[0][1]
    for i in range(len(GT)-1):
        a,va=GT[i]; b,vb=GT[i+1]
        if a<=t<=b:
            return va if b==a else va+(vb-va)*(t-a)/(b-a)
    return GT[-1][1]
