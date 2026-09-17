"""Which levels can we TRUST as labels? Not all of them — just the ones whose reconstructed
   chart duration lands on the song. Filter by chart features and see which subset is clean."""
import sys, os, json, subprocess, statistics
SP=os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,SP)
from adofai import load_adofai
from charttime3 import chart_timeline
ROOT='/Users/preluminance/Documents/TUFLevels'
def dur(p):
    r=subprocess.run(['ffprobe','-v','quiet','-show_entries','format=duration','-of','csv=p=0',p],capture_output=True)
    try: return float(r.stdout.strip())
    except: return None
idx=json.load(open(SP+'/tuf_index.json'))
rows=[]
for e in idx:
    lp=os.path.join(ROOT,e['level']); sp=os.path.join(ROOT,e['song'])
    if not (os.path.exists(lp) and os.path.exists(sp)): continue
    j=load_adofai(lp)
    if not j or not j.get('angleData'): continue
    d=dur(sp)
    if not d: continue
    try: tl,end=chart_timeline(j)
    except Exception: continue
    if not end or end<=1: continue
    ang=j['angleData']; acts=[a for a in (j.get('actions') or []) if isinstance(a,dict)]
    nm=sum(1 for a in ang if a==999)
    npause=sum(1 for a in acts if a.get('eventType') in ('Pause','Hold'))
    rows.append(dict(r=end/d,nm=nm,np=npause,lvl=e['level'],tl=tl,end=end,dur=d,
                     bpm=j['settings'].get('bpm')))
def rep(name,pool):
    if not pool: print('%-28s none'%name); return
    v=sorted(x['r'] for x in pool)
    print('%-28s n=%3d median %.3f   in 0.9-1.1: %3d (%.0f%%)'%(
        name,len(v),statistics.median(v),sum(1 for x in v if 0.9<=x<=1.1),
        100*sum(1 for x in v if 0.9<=x<=1.1)/len(v)))
rep('all',rows)
rep('no midspins',[x for x in rows if x['nm']==0])
rep('no pauses/holds',[x for x in rows if x['np']==0])
rep('neither',[x for x in rows if x['nm']==0 and x['np']==0])
clean=[x for x in rows if 0.9<=x['r']<=1.1]
print()
print('TRUSTWORTHY SUBSET (ratio within 10%%): %d levels'%len(clean))
multi=[x for x in clean if len({round(b,3) for _,b in x['tl']})>1]
print('  of which have >1 distinct declared tempo: %d'%len(multi))
json.dump([{'lvl':x['lvl'],'ratio':x['r'],'tl':x['tl'],'dur':x['dur'],'bpm':x['bpm']} for x in clean],
          open(SP+'/tuf_labels.json','w'))
print('  written to tuf_labels.json')
