import sys, os, json, subprocess, math
SP=os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0,SP)
from adofai import load_adofai
from charttime4 import base_timeline
ROOT='/Users/preluminance/Documents/TUFLevels'
def dur(p):
    r=subprocess.run(['ffprobe','-v','quiet','-show_entries','format=duration','-of','csv=p=0',p],capture_output=True)
    try: return float(r.stdout.strip())
    except: return None
idx=json.load(open(SP+'/tuf_index.json'))
out=[]
for e in idx:
    lp=os.path.join(ROOT,e['level']); sp=os.path.join(ROOT,e['song'])
    if not (os.path.exists(lp) and os.path.exists(sp)): continue
    j=load_adofai(lp)
    if not j or not j.get('angleData'): continue
    d=dur(sp)
    if not d: continue
    try: r=base_timeline(j)
    except Exception: continue
    if not r: continue
    tl,end=r
    if not end or end<=1: continue
    ratio=end/d
    if not (0.9<=ratio<=1.1): continue
    out.append({'lvl':e['level'],'song':e['song'],'ratio':ratio,'tl':tl,'dur':d,
                'changes':len({round(b,3) for _,b in tl})-1})
json.dump(out,open(SP+'/tuf_labels2.json','w'))
print('levels passing duration check: %d'%len(out))
print('  with >=1 base change: %d'%sum(1 for x in out if x['changes']>0))
print('  constant base:        %d'%sum(1 for x in out if x['changes']==0))
