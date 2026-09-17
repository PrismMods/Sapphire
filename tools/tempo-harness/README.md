# Tempo harness

Offline measurement for the Audio window's tempo curve (`Sapphire/Util/AudioAnalysis.cs`).
Nothing here ships with the mod — it exists so a change to the curve can be measured against
real charts instead of argued about.

    python3 -m venv nv && nv/bin/pip install numpy scipy
    nv/bin/python label_eval.py        # our curve vs the labelled multi-tempo charts
    nv/bin/python walk7.py             # chart-time reconstruction self-check

Set `ROOT` in the scripts to a level library (the corpus used was `~/Documents/TUFLevels`).
`ffmpeg`/`ffprobe` must be on PATH.

## The mirror

`cs_curve.py`, `fast.py`, `twoslope.py`, `notch.py` mirror the C# scoring and decode so a
parameter can be swept in seconds rather than through a game reload. **If `AudioAnalysis.cs`
changes, these have to change with it** — they are a copy, not a binding, and a silently stale
mirror produces confident wrong numbers.

## Ground truth, and how much to trust it

- `pus_gt.py` — Parallel Universe Shifter's real BPM curve, transcribed from Phoenixfisch's plot
  of Camellia's own MIDI (r/Camellia). The only fully independent reference. **PUS is an
  outlier**: our curve tracks it far worse than it tracks typical shifting music, so do not tune
  against it alone. That mistake was made repeatedly.
- `charttime3.py` — reconstructs chart time from a `.adofai`, following the game's own code
  (`scrMisc.GetTimeBetweenAngles`, `scnGame.ApplyEventsToFloors`,
  `scrLevelMaker.CalculateFloorEntryTimes`). Four bugs were found by reading that IL rather than
  guessing: the angle sign, speed being ONE running number (`Bpm` assigns, `Multiplier`
  multiplies), a missing `speedType` meaning `Bpm`, and `pitch` not always being 100.
  Still imperfect — midspin timing is wrong, median chart/song duration ratio 1.18.
- `clean.py` / `relabel.py` — turn that imperfection into a FILTER: a level whose reconstructed
  duration lands on its song has a timeline worth trusting. 41 of 138 pass.
- `charttime4.py` — the one that matters. `charttime3` gives the effective TILE RATE, which is
  not the tempo: He He He holds 126.5 BPM while its tile rate walks 126.5, 253, 506, 1012, 2024
  — every step a power of two, i.e. subdivision. Labels built from the tile rate punish a
  correct flat curve for staying flat, which is exactly what happened. `charttime4` classifies
  each change (power of two = subdivision, 180/angle = magic shape, else = real) and tracks the
  BASE. Verified against a charter's own description of four songs. Writes `tuf_labels2.json`
  — **use this one**, not `tuf_labels.json`.

  Irreducible ambiguity: a magic shape's ratio is 180/angle and angles are continuous, so almost
  any ratio in (1, 3] could be one. He He He's 1.1 steps are 180/163.6, indistinguishable from a
  real 10% lift by ratio alone. Some label error cannot be removed without a second source.

## Metrics

Prefer `safe_eval.py` / `safe_bins.py` for anything octave- or precision-related: they compare
the curve after the offset against the level's declared base BPM and touch no chart timeline, so
they cannot inherit the walk's error. Use `label_eval.py` for SHAPE, since that is what needs a
timeline.

Two figures are always worth reporting separately. *Octave-invariant* error says whether the
shape is right; *band* error says whether the number a charter reads is right. They disagree
often, and the band figure is the one the tool is judged on.

## Where it currently fails

`tuf_tracking2.json` is `leval3.py`'s per-song result, worst first. Against base-tempo labels:
octave-invariant 75.1% of samples within 3%, band 59.7%, 29 of 39 songs tracked more than 70%
of the time.

Use `leval4.py`, not `leval3.py`. **Never truncate the audio when measuring.** leval3 capped
songs at 200s for speed and that alone flips the decode on some tracks — Hello (BPM) 2025 reads
126.3 at 200s and 252.6 at 250s, the same song at half the tempo. It produced a confident,
wrong finding that constant-tempo songs track far worse than shifting ones; untruncated the
two are the same within noise:

                          truncated   correct
    shifting songs (30)      80.1%     79.5%
    constant songs  (9)      66.3%     77.0%

That octave instability with respect to how much audio is analysed is a REAL defect in the
shipped code, not only the harness — the band fold takes its power of two from the median of
whatever was decoded, so the answer depends on the input length. Worth fixing on its own.

## osu! beatmaps (`osz.py`, `osz_run.py`)

`.osz` is a zip with the `.osu` difficulties and their audio side by side, so unlike lazer's
content-addressed store there is no hash mapping to reverse. Point `osz_run.py` at a directory
of archives.

Three traps, all found on the first real batch:

- **Inherited points are slider velocity, not tempo** — negative `beatLength`. Only uninherited
  points are read.
- **Uninherited points are not tempo either, in mania.** Mappers use them for scroll gimmicks.
  GHOUL declares 42 distinct BPMs from 230 to 1150 while the music sits flat at 230; 465 of its
  484 segments last under a second. `sustained()` drops any tempo that does not hold for 4s.
  Same shape of problem as subdivision-versus-tempo in ADOFAI charts.
- **Rate variants are not separate songs.** A mania set ships the same track at several speeds;
  `bpm * duration` is constant across them (KOKUSHIMUSOU: 33000 for all eight). Group by
  artist+title — the `key` field — before splitting train/test, or the same music lands on both
  sides.

Yield on the first batch: 17 archives, 53 audio files, **17 distinct works, 1 with sustained
tempo changes**. Mania packs are mostly constant-tempo, so they are a poor source of tempo-CHANGE
labels — but an excellent source of CONTROLLED tempo variation, since the rate variants give the
same music at known, exactly-related tempos with ground truth verifiable from `bpm * duration`.
