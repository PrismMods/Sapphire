"""Pass 1 — per-chart pattern metrics into out/scan.json.

    python3 scan.py [root ...]     # default roots below; files deduplicated by content hash
"""
import os, sys, json, hashlib, collections, glob, re, unicodedata
from walk import *
HERE=os.path.dirname(os.path.abspath(__file__))
OUT=os.path.join(HERE,'out'); os.makedirs(OUT,exist_ok=True)
ROOTS=sys.argv[1:] or ['~/Documents/CustomDL','~/Documents/TUFLevels','~/Library/Mobile Documents/com~apple~CloudDocs/Documents/ADOFAI','~/Library/Mobile Documents/com~apple~CloudDocs/Documents/TUFLevels']
files=[]
for r in ROOTS:
    for p in glob.glob(os.path.join(os.path.expanduser(r),'**','*.adofai'),recursive=True): files.append(p)
seen=set(); out=[]
SKIPK={'floor','eventType','eventTag','active'}
def sig(e): return json.dumps({k:v for k,v in e.items() if k not in SKIPK},sort_keys=True)
for p in files:
    try: raw=open(p,'rb').read()
    except Exception: continue
    h=hashlib.md5(raw).hexdigest()
    if h in seen or len(raw)<200: continue
    seen.add(h)
    d=load_adofai(p)
    if not d or d.get('_partial') or angles(d) is None: continue
    try: rows,base=walk(d)
    except Exception as ex: continue
    acts=d.get('actions',[]); N=len(rows)
    if N<50: continue
    m={'path':p,'name':os.path.basename(os.path.dirname(p)),'mine':'자작맵' in unicodedata.normalize('NFC',p),'tiles':N,'events':len(acts),'decos':len(d.get('decorations',[]) or []),
       'ver':d['settings'].get('version')}
    tc=collections.Counter(e.get('eventType') for e in acts); m['types']=dict(tc)
    # G1 compensation runs: consecutive tiles each with Bpm SetSpeed and identical hit gap (beats of base)
    comp=0; run=0; lastgap=None
    for r in rows:
        has=any(e.get('eventType')=='SetSpeed' for e in r['ev'])
        if r['rel'] is None or not has: 
            if run>=4: comp+=run
            run=0; lastgap=None; continue
        gap=r['rel']/180*base/r['bpm']
        if lastgap is not None and abs(gap-lastgap)<1e-3*max(gap,1e-9): run+=1
        else:
            if run>=4: comp+=run
            run=1
        lastgap=gap
    if run>=4: comp+=run
    m['comp_tiles']=comp; m['setspeed']=tc.get('SetSpeed',0)
    # G3 repeated geometry: 8-tile windows of rel angle repeated elsewhere
    rel=[None if r['rel'] is None else round(r['rel'],1) for r in rows]
    w=8; cnt=collections.Counter(tuple(rel[i:i+w]) for i in range(N-w))
    cover=[False]*N
    for i in range(N-w):
        k=tuple(rel[i:i+w])
        if cnt[k]>1 and len(set(k))>1:
            for j in range(i,i+w): cover[j]=True
    m['rep_geom']=sum(cover)/N
    m['twirls']=tc.get('Twirl',0); m['midspins']=sum(1 for r in rows if r['abs']==999)
    # V2 copy-paste: events whose full param signature (minus floor) occurs >1
    sc=collections.Counter((e.get('eventType'),sig(e)) for e in acts if e.get('eventType') not in('Twirl',))
    m['dup_events']=sum(n for k,n in sc.items() if n>1); m['nontwirl']=sum(sc.values())
    # V1 stagger: same type, params equal except one numeric field stepping linearly, over consecutive floors or same floor with startTile steps
    stag=0
    byt=collections.defaultdict(list)
    for e in acts:
        if e.get('eventType') in('Twirl','SetSpeed','EditorComment'): continue
        byt[e.get('eventType')].append(e)
    for t,lst in byt.items():
        lst=sorted(lst,key=lambda e:e.get('floor',0)); i=0
        while i<len(lst)-2:
            a,b=lst[i],lst[i+1]
            diff=[k for k in set(a)|set(b) if k not in SKIPK and a.get(k)!=b.get(k)]
            df=b.get('floor',0)-a.get('floor',0)
            ok=len(diff)<=1 and df in(0,1) 
            if not ok: i+=1; continue
            j=i+1
            while j+1<len(lst):
                c=lst[j+1]; dd=[k for k in set(lst[j])|set(c) if k not in SKIPK and lst[j].get(k)!=c.get(k)]
                if dd!=diff or c.get('floor',0)-lst[j].get('floor',0)!=df: break
                j+=1
            if j-i+1>=4: stag+=j-i+1
            i=j+1
    m['stagger']=stag
    # V3 hiding
    hide=0
    for e in acts:
        t=e.get('eventType')
        if t=='PositionTrack' and (e.get('opacity')==0 or e.get('scale')==0): hide+=1
        elif t=='ColorTrack' and str(e.get('trackColor','')).lower().endswith('00') and len(str(e.get('trackColor','')))==8: hide+=1
        elif t=='Hide': hide+=1
        elif t=='MoveTrack' and e.get('opacity')==0: hide+=1
    m['hide']=hide
    # V4 multitrack-ish: PositionTrack offsets
    po=[json.dumps(e.get('positionOffset')) for e in acts if e.get('eventType')=='PositionTrack' and e.get('positionOffset') not in(None,[0,0])]
    m['pos_offsets']=len(po); m['pos_distinct']=len(set(po))
    # N1 per-floor crowding
    pf=collections.Counter(e.get('floor',0) for e in acts if e.get('eventType')!='Twirl')
    m['floors_ev']=len(pf); m['max_per_floor']=max(pf.values()) if pf else 0; m['floors_ge10']=sum(1 for v in pf.values() if v>=10)
    m['ev_in_ge10']=sum(v for v in pf.values() if v>=10)
    # N2 tags
    tags=collections.Counter()
    for e in acts:
        for k in('eventTag',):
            v=e.get(k)
            if v and str(v).strip(): tags[str(v)]+=1
    m['tagged']=sum(tags.values()); m['tags_distinct']=len(tags)
    m['comments']=[(e.get('floor'),str(e.get('comment',''))[:60]) for e in acts if e.get('eventType')=='EditorComment'][:40]
    m['decoTags']=len({str(x.get('tag')) for x in (d.get('decorations') or []) if x.get('tag')})
    out.append(m)
json.dump(out,open(os.path.join(OUT,'scan.json'),'w'),ensure_ascii=False)
print(len(files),'files',len(out),'charts')
