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
rs=[]
for e in idx:
    lp=os.path.join(ROOT,e['level']); sp=os.path.join(ROOT,e['song'])
    if not (os.path.exists(lp) and os.path.exists(sp)): continue
    j=load_adofai(lp)
    if not j or not j.get('angleData'): continue
    d=dur(sp)
    if not d: continue
    try: tl,end=chart_timeline(j)
    except Exception: continue
    if end and end>1: rs.append((end/d,e['level']))
r=sorted(x for x,_ in rs)
print('n=%d median %.3f'%(len(r),statistics.median(r)))
for lo,hi,l in ((0,0.5,'<0.5'),(0.5,0.9,'0.5-0.9'),(0.9,1.1,'0.9-1.1 GOOD'),(1.1,1.5,'1.1-1.5'),(1.5,9e9,'>1.5 wrong')):
    print('  %-14s %d'%(l,sum(1 for x in r if lo<=x<hi)))
print('  playable (<=1.05): %d/%d'%(sum(1 for x in r if x<=1.05),len(r)))
print('  worst:',[(round(x,2),n[:30]) for x,n in sorted(rs,reverse=True)[:5]])
