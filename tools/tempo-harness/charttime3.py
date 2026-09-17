"""Chart time in AUDIO seconds, following the game's own code.

   scrMisc.GetTimeBetweenAngles (from the IL):
       moved   = mod((exit - entry) * s, 2pi)
       seconds = (moved / pi) * (60 / bpm) / speed
   entry is the direction back to the previous tile, exit is this tile's facing. angleData is
   CLOCKWISE degrees while the game works in standard maths angles, so the conversion flips the
   sign — which makes it mod(((prev + 180) - cur) * cw, 360).

   scnGame.ApplyEventsToFloors: speed is ONE running number. A Bpm event ASSIGNS it, a
   Multiplier event MULTIPLIES it. A MISSING speedType is Bpm — the enum's zero value — not
   Multiplier; 171 old-format events in the corpus were being dropped entirely.

   scrLevelMaker.CalculateFloorEntryTimes divides by AudioSource.pitch, so a level that plays
   its song at 200% covers twice the chart in the same audio."""
import math

def chart_timeline(j):
    ang=j.get('angleData'); s=j.get('settings',{})
    if not ang: return None
    base=float(s.get('bpm') or 0)
    if base<=0: return None
    offset=float(s.get('offset') or 0)/1000.0
    pitch=float(s.get('pitch') or 100)/100.0
    if pitch<=0: pitch=1.0
    acts=[a for a in (j.get('actions') or []) if isinstance(a,dict)]
    twirl=set(a.get('floor') for a in acts if a.get('eventType')=='Twirl')
    per={}
    for a in acts:
        if a.get('eventType') in ('SetSpeed','Pause','Hold'): per.setdefault(a.get('floor'),[]).append(a)
    cw=1.0; eff=base; t=0.0; prev=None
    out=[(offset, base)]
    for T in range(len(ang)):
        if T in twirl: cw=-cw
        for a in per.get(T,[]):
            et=a.get('eventType')
            if et=='SetSpeed':
                if a.get('speedType','Bpm')!='Multiplier':      # missing == Bpm
                    v=a.get('beatsPerMinute')
                    if v and float(v)>0: eff=float(v)
                else:
                    m=a.get('bpmMultiplier')
                    if m and float(m)>0: eff*=float(m)
                out.append((t/pitch+offset, eff))
            else:
                d=a.get('duration')
                if d and eff>0: t+=float(d)*60.0/eff
        cur=ang[T]
        if cur==999:
            cw=-cw; continue
        if prev is not None:
            moved=(((prev+180.0)-cur)*cw)%360.0
            t+=(moved/180.0)*60.0/(eff if eff>0 else base)
        prev=cur
    return out, t/pitch+offset

def tempo_at(tl,t):
    v=tl[0][1]
    for tt,b in tl:
        if tt<=t: v=b
        else: break
    return v
