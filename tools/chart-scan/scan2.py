"""Pass 2 — reads out/scan.json, drops near-duplicate charts, measures periodic trains,
cascades and geometry+event copy-paste into out/scan2.json."""
import os, json, collections, sys
from walk import *
OUT=os.path.join(os.path.dirname(os.path.abspath(__file__)),'out')
M=json.load(open(os.path.join(OUT,'scan.json')))
from scan2_dedup import unique
keep=unique(M)
print('unique charts',len(keep))
SKIPK={'floor','eventType','eventTag','active'}
def sig(e): return json.dumps({k:v for k,v in e.items() if k not in SKIPK},sort_keys=True)
res=[]
for m in keep:
    d=load_adofai(m['path']); rows,base=walk(d)
    # hit time in beats-of-base for each floor
    tb=[0.0]*(len(rows)+1); acc=0.0
    for i,r in enumerate(rows):
        tb[i]=acc
        if r['rel'] is not None: acc+= r['rel']/180*base/r['bpm']
    acts=[e for e in d.get('actions',[]) if e.get('eventType')!='Twirl']
    groups=collections.defaultdict(list)
    for e in acts:
        f=e.get('floor',0)
        if f>=len(rows): continue
        t=tb[f]+float(e.get('angleOffset',0) or 0)/180*base/rows[f]['bpm']
        groups[(e.get('eventType'),sig(e))].append(t)
    # periodic trains: within a signature, maximal runs >=4 with constant spacing (tol 2%)
    train=0; trainlens=[]
    for k,ts in groups.items():
        ts=sorted(ts); i=0
        while i<len(ts)-3:
            dt=ts[i+1]-ts[i]
            if dt<=1e-6: i+=1; continue
            j=i+1
            while j+1<len(ts) and abs((ts[j+1]-ts[j])-dt)<=0.02*dt: j+=1
            if j-i+1>=4: train+=j-i+1; trainlens.append(j-i+1)
            i=j if j>i+1 else i+1
    # cascades: same type, same floor or consecutive, params differ in <=2 fields that change by constant numeric/relative-tile step
    def num(v):
        if isinstance(v,(int,float)): return float(v)
        if isinstance(v,list) and v and isinstance(v[0],(int,float)): return float(v[0])
        return None
    casc=0
    byt=collections.defaultdict(list)
    for e in acts: byt[e.get('eventType')].append(e)
    for t,lst in byt.items():
        lst.sort(key=lambda e:(e.get('floor',0)))
        i=0
        while i<len(lst)-3:
            a,b=lst[i],lst[i+1]
            diff=sorted(k for k in set(a)|set(b) if k not in SKIPK|{'floor'} and a.get(k)!=b.get(k))
            df=b.get('floor',0)-a.get('floor',0)
            if not (1<=len(diff)+(df!=0)<=3) or df not in(0,1) or any(num(a.get(k)) is None or num(b.get(k)) is None for k in diff):
                i+=1; continue
            steps=[num(b.get(k))-num(a.get(k)) for k in diff]
            j=i+1
            while j+1<len(lst):
                c,p=lst[j+1],lst[j]
                dd=sorted(k for k in set(p)|set(c) if k not in SKIPK|{'floor'} and p.get(k)!=c.get(k))
                if dd!=diff or c.get('floor',0)-p.get('floor',0)!=df: break
                if any(num(c.get(k)) is None or abs((num(c.get(k))-num(p.get(k)))-s)>1e-6 for k,s in zip(diff,steps)): break
                j+=1
            if j-i+1>=4: casc+=j-i+1
            i=j+1
    # geometry+events copy: repeated 8-tile rel windows whose event signature sequence also repeats
    rel=[None if r['rel'] is None else round(r['rel'],1) for r in rows]
    evs=[tuple(sorted((e.get('eventType'),sig(e)) for e in r['ev'] if e.get('eventType')!='Twirl')) for r in rows]
    w=8; first={}; both=0; geo=0
    for i in range(len(rows)-w):
        k=tuple(rel[i:i+w]); 
        if len(set(k))<2: continue
        ek=tuple(evs[i:i+w])
        if k in first:
            geo+=1
            if any(ek==x for x in first[k]) and any(ek): both+=1
            first[k].append(ek)
        else: first[k]=[ek]
    res.append({'name':m['name'],'nontwirl':len(acts),'train':train,'casc':casc,'geo_rep_windows':geo,'geo_ev_rep_windows':both,'tiles':m['tiles']})
    sys.stdout.write('.'); sys.stdout.flush()
json.dump(res,open(os.path.join(OUT,'scan2.json'),'w'),ensure_ascii=False)
A=sum(r['nontwirl'] for r in res)
print('\nperiodic-train share',round(sum(r['train'] for r in res)/A,3),'cascade share',round(sum(r['casc'] for r in res)/A,3))
print('repeated geometry windows that carry identical event blocks', round(sum(r['geo_ev_rep_windows'] for r in res)/max(1,sum(r['geo_rep_windows'] for r in res)),3))
o=[r for r in res if 'Once Forgotten' in r['name']]; print(o)
