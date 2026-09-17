import sys, os, json, glob, collections
SP=os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0,SP)
from osz import ingest, duration, sustained
ROOTS=[os.path.expanduser('~/Downloads')]
AUD=os.path.join(SP,'osu_audio'); os.makedirs(AUD,exist_ok=True)
oszs=[]
for r in ROOTS:
    oszs+=glob.glob(os.path.join(r,'**','*.osz'),recursive=True)
print('archives:',len(oszs),flush=True)
songs=[]
for p in oszs:
    songs+=ingest(p,AUD)
print('song records:',len(songs),flush=True)
# one record per distinct audio across all archives
by={}
for s in songs: by.setdefault(s['song_id'],s)
songs=list(by.values())
for s in songs: s['dur']=duration(s['audio'])
songs=[s for s in songs if s['dur']]
for s in songs:
    s['tps_raw']=s['tps']
    s['tps']=sustained(s['tps'], s['dur'])
    s['distinct_bpm']=len({round(b,3) for _,b in s['tps']})
    s['key']=(s['artist'].lower()+'|'+s['title'].lower())
songs=[s for s in songs if s['dur'] and s['dur']>30]
print('distinct songs with audio >30s:',len(songs))
ch=[s for s in songs if s['distinct_bpm']>1]
print('  with >1 distinct BPM: %d'%len(ch))
print('  timing disagreed between difficulties: %d'%sum(1 for s in songs if not s['timing_agrees']))
print('  modes:',collections.Counter(s['mode'] for s in songs).most_common())
keys=collections.Counter(s['key'] for s in songs)
print('  DISTINCT WORKS (artist+title): %d  -- rate variants collapse here'%len(keys))
print('  works with >1 sustained BPM: %d'%len({s['key'] for s in songs if s['distinct_bpm']>1}))
print('  difficulties per song: mean %.1f'%(sum(s['difficulties'] for s in songs)/max(1,len(songs))))
json.dump(songs,open(os.path.join(SP,'osu_songs.json'),'w'))
print()
print('%-42s %5s %5s %6s %s'%('title','mode','diffs','BPMs','range'))
for s in sorted(songs,key=lambda x:-x['distinct_bpm'])[:18]:
    bs=[b for _,b in s['tps']]
    print('%-42s %5d %5d %6d  %.1f-%.1f'%(s['title'][:42],s['mode'],s['difficulties'],
          s['distinct_bpm'],min(bs),max(bs)))
