# Chart scan

Corpus statistics over `.adofai` charts — which patterns charters build by hand, and how often.
Nothing here ships with the mod; it exists so tool and UI design can be argued from counts.
Shares the tolerant loader in `../tempo-harness/adofai.py`. Plain Python 3, no dependencies.

    python3 scan.py [root ...]   # pass 1: per-chart metrics -> out/scan.json (slow: reads every chart)
    python3 scan2.py             # pass 2: dedup + periodic trains, cascades, geometry+event copies
    python3 crowd.py             # pass 3: crowded-tile mix, MoveCamera shapes

Default roots are the local libraries (`~/Documents/CustomDL`, `~/Documents/TUFLevels`, the iCloud
ADOFAI folders). Charts under a `자작맵` folder are flagged `mine`. `out/` is gitignored — it
holds local paths.

## Metrics, and what they undercount

- `rep_geom` — share of tiles inside an 8-tile run of charter angles that occurs elsewhere.
- `dup_events` — non-twirl events whose full parameters (minus floor/tag) have a twin.
- `train` — same event at a constant beat spacing, 4 or more in a row (2% tolerance).
- `casc` — same event on the same/consecutive tiles with up to 3 fields stepping by a constant.
  Strict: an eased or non-numeric step is missed, so treat it as a floor.
- `comp_tiles` — runs of 4+ tiles that each carry a SetSpeed and keep an identical hit gap: the
  "free angle, constant rhythm" technique (Once Forgotten, Nothing Remains floors 4584–4607).
- `hide` — tiles hidden by PositionTrack scale/opacity 0, alpha-00 ColorTrack, Hide, or MoveTrack
  opacity 0.

`walk.py` ignores pauses, holds and free-roam, so beat positions drift across those; fine for
spacing statistics, wrong for absolute song time (use `charttime3.py` for that).

## Results (2026-10-06, 295 unique charts, 1.33M tiles, 1.14M events)

79% of non-twirl events have an exact twin; 84% of tiles sit in a repeated 8-tile run, but only
26% of those repeats carry the same events. Periodic trains are 18% of non-twirl events,
cascades ≥8%. MoveCamera is 14% of all events with 29k set+tween pairs. 20% of events sit on
tiles holding 10+; those stacks are 30% filters, 25% deco moves. Tags: median 0 per chart.
47 charts use constant-rhythm free-angle runs, 149 hide 20+ tiles, 156 type 20+ track offsets.
