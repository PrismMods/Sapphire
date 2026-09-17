"""Ingest .osz beatmap archives into (audio, notated tempo curve) pairs.

   An .osz is a zip holding the .osu difficulties and their audio side by side, so unlike
   lazer's content-addressed store there is no hash mapping to reverse — the .osu names its
   own audio.

   UNINHERITED timing points only. In the .osu format an inherited ("green") point carries
   slider velocity, not tempo, and is marked by a negative beatLength; treating those as tempo
   would import the same subdivision-versus-tempo confusion that ADOFAI charts have.

   One SONG is one example, never one difficulty. A set normally holds five or more
   difficulties over identical audio with identical timing, and letting those spread across a
   train/test split would put the same song on both sides and make every number meaningless."""
import os, io, re, json, zipfile, hashlib, subprocess, collections

def parse_osu(text):
    sec=None; tps=[]; last=0.0; meta={}
    for line in text.splitlines():
        s=line.strip()
        if not s or s.startswith('//'): continue
        if s.startswith('[') and s.endswith(']'): sec=s[1:-1]; continue
        if sec in ('General','Metadata','Difficulty') and ':' in s:
            k,v=s.split(':',1); meta[k.strip()]=v.strip()
        elif sec=='TimingPoints':
            f=s.split(',')
            if len(f)>=2:
                try:
                    t=float(f[0]); bl=float(f[1])
                except ValueError: continue
                # uninherited = positive beatLength (and flag 1 when present)
                uninh = bl>0 and ((len(f)<7) or f[6].strip()=='1')
                if uninh:
                    bpm=60000.0/bl
                    if 20.0<=bpm<=1200.0: tps.append((t/1000.0, bpm))
        elif sec=='HitObjects':
            f=s.split(',')
            if len(f)>=3:
                try: last=max(last,float(f[2])/1000.0)
                except ValueError: pass
    if not tps: return None
    tps.sort()
    return dict(tps=tps, last=last, meta=meta)

def read_text(zf, name):
    raw=zf.read(name)
    for enc in ('utf-8-sig','utf-8','cp1252','latin-1'):
        try: return raw.decode(enc)
        except Exception: continue
    return None

AUD=re.compile(r'\.(mp3|ogg|wav|m4a)$', re.I)

def ingest(osz_path, audio_dir):
    """-> list of song records. One per distinct audio file in the archive."""
    out=[]
    try: zf=zipfile.ZipFile(osz_path)
    except Exception: return out
    with zf:
        names=zf.namelist()
        diffs=collections.defaultdict(list)
        for n in names:
            if not n.lower().endswith('.osu'): continue
            t=read_text(zf,n)
            if not t: continue
            m=parse_osu(t)
            if not m: continue
            af=m['meta'].get('AudioFilename','').strip()
            if not af: continue
            diffs[af.lower()].append((n,m))
        for af,ds in diffs.items():
            member=next((n for n in names if n.lower()==af or n.lower().endswith('/'+af)), None)
            if member is None: continue
            # difficulties of one song normally agree on timing; take the richest and record
            # whether they actually agreed, since a disagreement means one of them is wrong
            ds.sort(key=lambda d: -len(d[1]['tps']))
            best=ds[0][1]
            sigs={tuple(round(b,4) for _,b in d[1]['tps']) for d in ds}
            data=zf.read(member)
            h=hashlib.sha1(data).hexdigest()[:16]
            ext=os.path.splitext(member)[1].lower() or '.mp3'
            dest=os.path.join(audio_dir,h+ext)
            if not os.path.exists(dest):
                with open(dest,'wb') as f: f.write(data)
            out.append(dict(
                song_id=h, audio=dest, osz=os.path.basename(osz_path),
                title=best['meta'].get('Title',''), artist=best['meta'].get('Artist',''),
                mode=int(best['meta'].get('Mode','0') or 0),
                difficulties=len(ds), timing_agrees=len(sigs)==1,
                tps=best['tps'], last=best['last'],
                distinct_bpm=len({round(b,3) for _,b in best['tps']})))
    return out

SUSTAIN=4.0   # seconds a tempo must hold to count as tempo

def sustained(tps, end, min_hold=SUSTAIN):
    """Drop timing points that do not HOLD.

       In mania an uninherited point is routinely used for a scroll-speed gimmick rather than a
       tempo change — GHOUL declares 42 distinct BPMs between 230 and 1150, of which 465 of 484
       segments last under a second while the music sits flat at 230. A tempo a listener could
       follow lasts seconds, so anything shorter is discarded and the previous tempo carries
       through. The same trick that separates subdivision from tempo in ADOFAI charts."""
    if not tps: return tps
    out=[]
    for i,(t,b) in enumerate(tps):
        nxt = tps[i+1][0] if i+1<len(tps) else max(end, t+min_hold+1)
        if nxt-t >= min_hold:
            if not out or abs(out[-1][1]-b) > 1e-6: out.append((t,b))
    if not out: out=[tps[0]]
    return out

def duration(p):
    try:
        r=subprocess.run(['ffprobe','-v','quiet','-show_entries','format=duration',
                          '-of','csv=p=0',p],capture_output=True,text=True,timeout=30)
        return float(r.stdout.strip())
    except Exception: return None

def tempo_at(tps,t):
    v=tps[0][1]
    for tt,b in tps:
        if tt<=t: v=b
        else: break
    return v
