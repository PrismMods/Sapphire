using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ADOFAI;
using DG.Tweening;
using UnityEngine;

namespace Sapphire
{
    /* Track-tool math from AdofaiMappingHelper by Sprout34 (integrated with permission).
       Features: track fade in/out, explosion, multiple-track decoration copies (with planet
       choreography), track size ramps, generate-track-from-angles. Logic kept faithful;
       parameters come from EditorTrackTools instead of MH's inspector tab, and eases are
       evaluated with the game's own EaseManager (replacing MH's DOTween re-implementation). */
    internal static class TrackToolsEngine
    {
        // ── per-floor geometry/timing snapshots (MH's AngleData/TrackData) ───

        internal class AngleData
        {
            public float angle, head, tail;
            public AngleData(float angle, float head, float tail) { this.angle = angle; this.head = head; this.tail = tail; }
        }

        internal class TrackData
        {
            public float angle, head, tail;
            public Vector2 position;
            public float rotation;
            public bool isPause; public float pauseDuration;
            public bool isHold; public int holdDuration; public int holdDistance;
            public float bpm, arrivalTime, departureTime;
            public TrackData(float angle, float head, float tail, Vector2 position, float rotation,
                bool isPause, float pauseDuration, bool isHold, int holdDuration, int holdDistance,
                float bpm, float arrivalTime, float departureTime)
            {
                this.angle = angle; this.head = head; this.tail = tail;
                this.position = position; this.rotation = rotation;
                this.isPause = isPause; this.isHold = isHold;
                this.pauseDuration = isPause ? pauseDuration : 0;
                this.holdDuration = isHold ? holdDuration : 0;
                this.holdDistance = isHold ? holdDistance : 0;
                this.bpm = bpm; this.arrivalTime = arrivalTime; this.departureTime = departureTime;
            }
        }

        internal static Dictionary<int, Dictionary<LevelEventType, List<LevelEvent>>> BuildFloorEvents(scnEditor editor)
        {
            float[] floorAngles = scrLevelMaker.instance.floorAngles;
            var floorEvents = new Dictionary<int, Dictionary<LevelEventType, List<LevelEvent>>>();
            foreach (var e in editor.events)
            {
                if (e.floor <= 0 || e.floor > floorAngles.Length + 1) continue;
                if (!floorEvents.ContainsKey(e.floor))
                    floorEvents[e.floor] = new Dictionary<LevelEventType, List<LevelEvent>>();
                if (!floorEvents[e.floor].ContainsKey(e.eventType))
                    floorEvents[e.floor][e.eventType] = new List<LevelEvent>();
                floorEvents[e.floor][e.eventType].Add(e);
            }
            return floorEvents;
        }

        internal static AngleData[] GetAnglesData()
        {
            float[] floorAngles = scrLevelMaker.instance.floorAngles;
            List<scrFloor> listFloors = scrLevelMaker.instance.listFloors;
            var result = new AngleData[floorAngles.Length + 1];
            float head = floorAngles[0];
            float tail = 180;
            for (int i = 0; i < result.Length - 1; i++)
            {
                if (floorAngles[i] == 999f)
                {
                    result[i] = new AngleData(999f, head, tail);
                    tail = head;
                    continue;
                }
                floorAngles[i] = CorrectDirection(floorAngles[i]);
                head = floorAngles[i];
                float angle = (listFloors[i].isCCW ? head - tail : tail - head) == 0 ? 360 : listFloors[i].isCCW ? head - tail : tail - head;
                angle = CorrectAngle(angle);
                result[i] = new AngleData(angle, head, tail);
                tail = (head + 180) % 360;
            }
            result[result.Length - 1] = new AngleData(180, head, tail);
            return result;
        }

        internal static TrackData[] GetTrackDatas(Dictionary<int, Dictionary<LevelEventType, List<LevelEvent>>> floorEvents = null)
        {
            AngleData[] angles = GetAnglesData();
            var result = new TrackData[angles.Length];
            List<scrFloor> listFloors = scrLevelMaker.instance.listFloors;
            float bpm = scnEditor.instance.levelData.bpm;
            float arrivalTime = 0;
            float departureTime = angles[0].angle / (3f * bpm);
            result[0] = new TrackData(angles[0].angle, angles[0].head, angles[0].tail, Vector2.zero, 0, false, 0, false, 0, 0, bpm, arrivalTime, departureTime);
            arrivalTime = departureTime;
            for (int floor = 1; floor < angles.Length; floor++)
            {
                bool isPause = false; float pauseDuration = 0;
                bool isHold = false; int holdDuration = 0; int holdDistance = 0;
                if (floorEvents != null && floorEvents.TryGetValue(floor, out var events))
                {
                    if (events.TryGetValue(LevelEventType.Hold, out var holdEvents))
                    {
                        isHold = true;
                        holdDuration = (int)holdEvents[0]["duration"];
                        holdDistance = (int)holdEvents[0]["distanceMultiplier"];
                    }
                    if (events.TryGetValue(LevelEventType.Pause, out var pauseEvents))
                    {
                        isPause = true;
                        pauseDuration = (float)pauseEvents[0]["duration"];
                    }
                    if (events.TryGetValue(LevelEventType.SetSpeed, out var bpmEvents))
                        foreach (LevelEvent bpmEvent in bpmEvents)
                        {
                            if ((SpeedType)bpmEvent["speedType"] == SpeedType.Bpm)
                                bpm = (float)bpmEvent["beatsPerMinute"];
                            else
                                bpm *= (float)bpmEvent["bpmMultiplier"];
                        }
                }
                float duration = listFloors[floor].midSpin ? 0f : (60f / bpm) * (angles[floor].angle / 180f + pauseDuration + 2 * holdDuration);
                departureTime = arrivalTime + duration;
                result[floor] = new TrackData(angles[floor].angle, angles[floor].head, angles[floor].tail,
                    listFloors[floor].transform.position / 1.5f, listFloors[floor].transform.rotation.eulerAngles.z,
                    isPause, pauseDuration, isHold, holdDuration, holdDistance, bpm, arrivalTime, departureTime);
                arrivalTime = departureTime;
            }
            return result;
        }

        internal static float CorrectDirection(float direction)
        {
            direction %= 360f;
            if (direction < 0) direction += 360f;
            return direction;
        }

        internal static float CorrectAngle(float direction)
        {
            direction %= 360f;
            if (direction <= 0) direction += 360f;
            return direction;
        }

        internal static float GetMidDirection(float startDirection, float endDirection, bool isCCW, bool goFullCircleIfEqual)
        {
            startDirection = (startDirection % 360 + 360) % 360;
            endDirection = (endDirection % 360 + 360) % 360;
            float delta;
            if (isCCW)
            {
                delta = (endDirection - startDirection + 360) % 360;
                if (delta == 0 && goFullCircleIfEqual) delta = 360;
                return (startDirection + delta / 2) % 360;
            }
            delta = (startDirection - endDirection + 360) % 360;
            if (delta == 0 && goFullCircleIfEqual) delta = 360;
            return (startDirection - delta / 2 + 360) % 360;
        }

        internal static Vector2 GetPivotOffset(Vector2 offset, float rotation)
        {
            float rad = rotation * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            return new Vector2(cos * offset.x + sin * offset.y, -sin * offset.x + cos * offset.y);
        }

        private static float EvalEase(Ease ease, float t)
        {
            try { return DG.Tweening.Core.Easing.EaseManager.Evaluate(ease, null, t, 1f, 1.70158f, 0f); }
            catch { return t; }
        }

        // ── shared parameter shapes ──────────────────────────────────────────

        internal class RandRange
        {
            public bool On;
            public float Min, Max;
            public RandRange(bool on, float min, float max) { On = on; Min = min; Max = max; }
            public float Sample() => UnityEngine.Random.Range(Min, Max);
        }

        internal class FadeParams
        {
            public int From, To;                 // affected floor range (absolute seqIDs)
            public int WinFrom, WinTo;           // MoveTrack window, relative to each tile
            public float Duration = 1f;
            public bool DurationMatchBpm;
            public RandRange XPos, YPos, Rot, Scale;
            public bool OpacityOn = true; public float Opacity;
            public float AngleOffset;
            public Ease Ease = Ease.Linear;
            public float ScaleRevertTo = 100f;   // fade-in landing scale
            public bool ScaleRevertOn = true;
        }

        // TrackDisappearAnimation: one randomized MoveTrack per tile in range.
        internal static void FadeOut(FadeParams p)
        {
            scnEditor editor = scnEditor.instance;
            var trackDatas = GetTrackDatas(BuildFloorEvents(editor));
            for (int floor = p.From; floor <= p.To; floor++)
            {
                var ev = new LevelEvent(floor, LevelEventType.MoveTrack);
                ev.disabled["positionOffset"] = !p.XPos.On && !p.YPos.On;
                ev.disabled["rotationOffset"] = !p.Rot.On;
                ev.disabled["scale"] = !p.Scale.On;
                ev.disabled["opacity"] = !p.OpacityOn;
                ev["startTile"] = Tuple.Create(p.WinFrom, TileRelativeTo.ThisTile);
                ev["endTile"] = Tuple.Create(p.WinTo, TileRelativeTo.ThisTile);
                ev["duration"] = p.DurationMatchBpm ? p.Duration * (trackDatas[floor].bpm / trackDatas[p.From].bpm) : p.Duration;
                ev["positionOffset"] = new Vector2(p.XPos.On ? p.XPos.Sample() : float.NaN, p.YPos.On ? p.YPos.Sample() : float.NaN);
                ev["rotationOffset"] = p.Rot.Sample();
                float scale = p.Scale.Sample();
                ev["scale"] = new Vector2(scale, scale);
                ev["opacity"] = p.Opacity;
                ev["angleOffset"] = p.AngleOffset;
                ev["ease"] = p.Ease;
                editor.events.Add(ev);
            }
            Finish(editor, p.From);
        }

        /* TrackAppearAnimation: opacity-0 the whole range at floor 0 duration, then per tile an
           instant randomize (duration 0) + an animated return to rest. */
        internal static void FadeIn(FadeParams p)
        {
            scnEditor editor = scnEditor.instance;
            var trackDatas = GetTrackDatas(BuildFloorEvents(editor));
            var init = new LevelEvent(p.From, LevelEventType.MoveTrack);
            init.disabled["opacity"] = false;
            init["startTile"] = Tuple.Create(p.From + p.WinFrom, TileRelativeTo.Start);
            init["endTile"] = Tuple.Create(p.To, TileRelativeTo.Start);
            init["duration"] = 0f;
            init["opacity"] = 0f;
            editor.events.Add(init);

            for (int floor = p.From; floor <= p.To - (p.WinTo >= 0 ? p.WinTo : 0); floor++)
            {
                var ev1 = new LevelEvent(floor, LevelEventType.MoveTrack);
                var ev2 = new LevelEvent(floor, LevelEventType.MoveTrack);

                ev1.disabled["positionOffset"] = !p.XPos.On && !p.YPos.On;
                ev1.disabled["rotationOffset"] = !p.Rot.On;
                ev1.disabled["scale"] = !p.Scale.On;
                ev1.disabled["opacity"] = true;
                ev2.disabled["rotationOffset"] = !p.Rot.On;
                ev2.disabled["scale"] = !p.ScaleRevertOn;
                ev2.disabled["opacity"] = !p.OpacityOn;

                ev1["startTile"] = Tuple.Create(p.WinFrom, TileRelativeTo.ThisTile);
                ev1["endTile"] = Tuple.Create(p.WinTo, TileRelativeTo.ThisTile);
                ev1["duration"] = 0f;
                ev1["positionOffset"] = new Vector2(p.XPos.On ? p.XPos.Sample() : float.NaN, p.YPos.On ? p.YPos.Sample() : float.NaN);
                ev1["rotationOffset"] = p.Rot.Sample();
                float scale = p.Scale.Sample();
                ev1["scale"] = new Vector2(scale, scale);

                ev2["startTile"] = Tuple.Create(p.WinFrom, TileRelativeTo.ThisTile);
                ev2["endTile"] = Tuple.Create(p.WinTo, TileRelativeTo.ThisTile);
                ev2["duration"] = p.DurationMatchBpm ? p.Duration * (trackDatas[floor].bpm / trackDatas[p.From].bpm) : p.Duration;
                ev2["positionOffset"] = new Vector2(p.XPos.On ? 0f : float.NaN, p.YPos.On ? 0f : float.NaN);
                ev2["rotationOffset"] = 0f;
                ev2["scale"] = new Vector2(p.ScaleRevertTo, p.ScaleRevertTo);
                ev2["opacity"] = p.Opacity;
                ev2["angleOffset"] = p.AngleOffset;
                ev2["ease"] = p.Ease;
                editor.events.Add(ev1);
                editor.events.Add(ev2);
            }
            Finish(editor, p.From);
        }

        /* TrackExplosionAnimation: per tile, a MoveTrack per window step (WinFrom..WinTo around
           it), AngleOffset accumulating per step — the shockwave ripples outward. */
        internal static void Explode(FadeParams p)
        {
            scnEditor editor = scnEditor.instance;
            for (int floor = p.From; floor <= p.To; floor++)
            {
                float stepAngle = 0f;
                for (int i = p.WinFrom; i <= p.WinTo; i++)
                {
                    var tile = Tuple.Create(i, TileRelativeTo.ThisTile);
                    var ev = new LevelEvent(floor, LevelEventType.MoveTrack);
                    ev.disabled["positionOffset"] = !p.XPos.On && !p.YPos.On;
                    ev.disabled["rotationOffset"] = !p.Rot.On;
                    ev.disabled["scale"] = !p.Scale.On;
                    ev.disabled["opacity"] = !p.OpacityOn;
                    ev["startTile"] = tile;
                    ev["endTile"] = tile;
                    ev["duration"] = p.Duration;
                    ev["positionOffset"] = new Vector2(p.XPos.On ? p.XPos.Sample() : float.NaN, p.YPos.On ? p.YPos.Sample() : float.NaN);
                    ev["rotationOffset"] = p.Rot.Sample();
                    float scale = p.Scale.Sample();
                    ev["scale"] = new Vector2(scale, scale);
                    ev["opacity"] = p.Opacity;
                    ev["angleOffset"] = stepAngle;
                    ev["ease"] = p.Ease;
                    stepAngle += p.AngleOffset;
                    editor.events.Add(ev);
                }
            }
            Finish(editor, p.From);
        }

        internal class SizeParams
        {
            public int From, To;
            public bool PosOn; public float PosFrom = 100f, PosTo = 100f;          // PositionTrack.scale
            public bool RadiusOn; public float RadiusFrom = 100f, RadiusTo = 100f; // ScaleRadius.scale
            public bool PlanetsOn; public float PlanetsFrom = 100f, PlanetsTo = 100f; // ScalePlanets.scale
            public Ease Ease = Ease.Linear;
        }

        // TrackSizeChange: eased lerp of the three scale events across the range (replaces existing ones).
        internal static void SizeChange(SizeParams p)
        {
            scnEditor editor = scnEditor.instance;
            var floorEvents = BuildFloorEvents(editor);
            void RemoveExisting(int floor, LevelEventType type)
            {
                if (floorEvents.TryGetValue(floor, out var events) && events.TryGetValue(type, out var list))
                    foreach (var e in list) editor.events.Remove(e);
            }
            int range = p.To - p.From;
            for (int floor = p.From; floor <= p.To; floor++)
            {
                float t = range == 0 ? 0f : (float)(floor - p.From) / range;
                t = EvalEase(p.Ease, t);
                if (p.PosOn)
                {
                    RemoveExisting(floor, LevelEventType.PositionTrack);
                    var ev = new LevelEvent(floor, LevelEventType.PositionTrack);
                    ev.disabled["scale"] = false;
                    ev["scale"] = Mathf.LerpUnclamped(p.PosFrom, p.PosTo, t);
                    editor.events.Add(ev);
                }
                if (p.RadiusOn)
                {
                    RemoveExisting(floor, LevelEventType.ScaleRadius);
                    var ev = new LevelEvent(floor, LevelEventType.ScaleRadius);
                    ev["scale"] = Mathf.LerpUnclamped(p.RadiusFrom, p.RadiusTo, t);
                    editor.events.Add(ev);
                }
                if (p.PlanetsOn)
                {
                    RemoveExisting(floor, LevelEventType.ScalePlanets);
                    var ev = new LevelEvent(floor, LevelEventType.ScalePlanets);
                    ev["duration"] = 0f;
                    ev["targetPlanet"] = TargetPlanet.All;
                    ev["scale"] = Mathf.LerpUnclamped(p.PlanetsFrom, p.PlanetsTo, t);
                    editor.events.Add(ev);
                }
            }
            Finish(editor, p.From);
        }

        // ── generate track from an angle string ("45 90T 135 …", T = twirl) ──

        internal static void ParseAngleData(string trackAngleData, out List<float> angles, out List<bool> twirls)
        {
            angles = new List<float>();
            twirls = new List<bool>();
            foreach (Match match in Regex.Matches(trackAngleData ?? "", @"(-?\d+(?:\.\d+)?)([Tt]?)"))
            {
                float value = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) % 360;
                if (value <= 0) value += 360;
                angles.Add(value);
                twirls.Add(match.Groups[2].Length > 0);
            }
        }

        internal static int Generate(int atTile, string trackAngleData, int generationCount)
        {
            scnEditor editor = scnEditor.instance;
            List<scrFloor> listFloors = scrLevelMaker.instance.listFloors;
            var trackDatas = GetTrackDatas(BuildFloorEvents(editor));
            float tail = (trackDatas[atTile].head + 180) % 360;
            bool isCCW = listFloors[atTile].isCCW;

            ParseAngleData(trackAngleData, out var parsed, out var twirl);
            if (parsed.Count == 0) return 0;

            var newTrack = new List<float>();
            var newTwirl = new List<bool>();
            var twirlFloors = new List<int>();
            for (int i = 0; i < generationCount; i++) { newTrack.AddRange(parsed); newTwirl.AddRange(twirl); }

            for (int i = 0; i < newTrack.Count; i++)
            {
                if (newTwirl[i])
                {
                    twirlFloors.Add(atTile + 1 + i);
                    isCCW = !isCCW;
                }
                newTrack[i] = CorrectDirection(tail + (isCCW ? newTrack[i] : -newTrack[i]));
                tail = (newTrack[i] + 180) % 360;
            }
            OffsetFloorIDsInEvents(editor, atTile, newTrack.Count);
            editor.levelData.angleData.InsertRange(atTile + 1, newTrack);
            foreach (var f in twirlFloors)
                editor.events.Add(new LevelEvent(f, LevelEventType.Twirl));
            editor.RemakePath(true, true);
            return newTrack.Count;
        }

        // shared with MagicShapeEngine.CreateShape — re-index events/decorations past an insert
        internal static void OffsetFloorIDsInEvents(scnEditor editor, int startFloorID, int offset)
        {
            var lists = new List<LevelEvent>[] { editor.events, editor.decorations };
            foreach (var list in lists)
                foreach (LevelEvent e in list)
                    if (e.floor > startFloorID)
                        e.floor += offset;
        }

        // ── multiple tracks (decoration copies of the real track ± planets) ──

        internal class MultiParams
        {
            public int From, To;
            public bool Centralized;            // all decos on one tile, laid out via pivotOffset
            public float TrackRotation;         // centralized only
            public string Tag = "";
            public bool UsePlanet;
            public bool PlanetConcentrated;     // planet choreography all on the first tile
            public bool UseIncreasingDepth; public int InitialDepth; public int IncreasingValue = 1;
            public bool ChangeParallax; public float ParallaxFrom, ParallaxTo = 100f;
            public bool AffectPlanet;
        }

        internal static void MultiTrackCreate(MultiParams p)
        {
            scnEditor editor = scnEditor.instance;
            List<scrFloor> listFloors = scrLevelMaker.instance.listFloors;
            var trackDatas = GetTrackDatas(BuildFloorEvents(editor));
            string tag = p.Tag ?? "";
            float pivotTrackOffset = p.Centralized ? p.TrackRotation : 0;
            float startBpm = trackDatas[p.From].bpm;
            int trackCount = p.To - p.From + 1;

            if (p.UsePlanet)
            {
                var blue = new LevelEvent(p.From, LevelEventType.AddObject);
                var red = new LevelEvent(p.From, LevelEventType.AddObject);
                blue["relativeTo"] = DecPlacementType.Tile;
                blue["objectType"] = ObjectDecorationType.Planet;
                blue["planetColorType"] = PlanetDecorationColorType.Custom;
                blue["planetColor"] = "0000ffff";
                blue["planetTailColor"] = "0000ff00";
                blue["position"] = new Vector2(0, 0);
                blue["pivotOffset"] = new Vector2(-Mathf.Cos(pivotTrackOffset * Mathf.Deg2Rad), -Mathf.Sin(pivotTrackOffset * Mathf.Deg2Rad));
                blue["tag"] = string.IsNullOrEmpty(tag) ? "_BluePlanet" : tag + " " + tag + "_BluePlanet";
                blue["depth"] = p.UseIncreasingDepth ? p.InitialDepth - 1 : -2;
                blue["scale"] = new Vector2(100 - p.ParallaxFrom, 100 - p.ParallaxFrom);
                blue["parallax"] = new Vector2(p.ParallaxFrom, p.ParallaxFrom);

                red["relativeTo"] = DecPlacementType.Tile;
                red["objectType"] = ObjectDecorationType.Planet;
                red["planetColorType"] = PlanetDecorationColorType.Custom;
                red["planetColor"] = "ff0000ff";
                red["planetTailColor"] = "ff000000";
                red["position"] = new Vector2(Mathf.Cos(pivotTrackOffset * Mathf.Deg2Rad), Mathf.Sin(pivotTrackOffset * Mathf.Deg2Rad));
                red["pivotOffset"] = new Vector2(-Mathf.Cos(pivotTrackOffset * Mathf.Deg2Rad), -Mathf.Sin(pivotTrackOffset * Mathf.Deg2Rad));
                red["tag"] = string.IsNullOrEmpty(tag) ? "_RedPlanet" : tag + " " + tag + "_RedPlanet";
                red["depth"] = p.UseIncreasingDepth ? p.InitialDepth - 1 : -2;
                red["scale"] = new Vector2(100 - p.ParallaxFrom, 100 - p.ParallaxFrom);
                red["parallax"] = new Vector2(p.ParallaxFrom, p.ParallaxFrom);

                editor.levelData.decorations.Add(blue);
                scrDecorationManager.instance.CreateDecoration(blue, out _, -1);
                editor.levelData.decorations.Add(red);
                scrDecorationManager.instance.CreateDecoration(red, out _, -1);
            }

            bool moveRedPlanet = false;
            int trackDepth = p.InitialDepth;
            for (int floor = p.From, i = 1; floor <= p.To; floor++, i++)
            {
                Vector2 curFloorPos = trackDatas[floor].position - trackDatas[p.From].position;
                int nextFloor = floor + 1 >= trackDatas.Length ? floor : floor + 1;
                Vector2 nextFloorPos = trackDatas[nextFloor].position - trackDatas[p.From].position;

                var ev = new LevelEvent(p.Centralized ? p.From : floor, LevelEventType.AddObject);
                ev["relativeTo"] = DecPlacementType.Tile;
                ev["position"] = Vector2.zero;
                if (listFloors[floor].midSpin)
                    ev["trackType"] = FloorDecorationType.Midspin;
                else
                    ev["trackAngle"] = trackDatas[floor].angle;

                float trackRotation = listFloors[floor].midSpin
                    ? trackDatas[floor].head
                    : GetMidDirection(trackDatas[floor].tail, trackDatas[floor].head, listFloors[floor].isCCW, true)
                      - GetMidDirection(180, (360 - ((trackDatas[floor].angle + 180) % 360)) % 360, false, true);
                ev["rotation"] = trackRotation + trackDatas[floor].rotation;

                if (!string.IsNullOrEmpty(tag)) ev["tag"] = tag + " " + tag + i;
                if (p.Centralized) ev["pivotOffset"] = GetPivotOffset(curFloorPos, trackRotation + trackDatas[floor].rotation);
                if (p.UseIncreasingDepth)
                {
                    ev["depth"] = trackDepth;
                    trackDepth += p.IncreasingValue;
                }

                if (p.UsePlanet)
                    PlanetChoreography(editor, p, trackDatas, listFloors, floor, i, trackCount, startBpm,
                        curFloorPos, nextFloorPos, nextFloor, pivotTrackOffset, tag, ref moveRedPlanet);

                ApplyFloorIcon(ev, listFloors[floor], trackDatas[floor], trackRotation);
                if (p.Centralized) ev["rotation"] = (float)ev["rotation"] + p.TrackRotation;

                if (p.ChangeParallax)
                {
                    float t = (i - 1) / (float)(trackCount - 1);
                    float parallaxValue = Mathf.Lerp(p.ParallaxFrom, p.ParallaxTo, t);
                    ev["scale"] = new Vector2(100 - parallaxValue, 100 - parallaxValue);
                    ev["parallax"] = new Vector2(parallaxValue, parallaxValue);
                }

                editor.levelData.decorations.Add(ev);
                scrDecorationManager.instance.CreateDecoration(ev, out _, -1);
            }
            editor.RemakePath(true, true);
        }

        /* The two fake planets ride the copied track: per real tile, position/rotation snaps
           (duration 0) plus a spin over the tile's angle; pause adds half-turns, hold adds full
           revolutions and a travel to the release tile ("mambo"). Planets alternate which one
           orbits. Faithful port — this block is choreography, not geometry. */
        private static void PlanetChoreography(scnEditor editor, MultiParams p, TrackData[] trackDatas,
            List<scrFloor> listFloors, int floor, int i, int trackCount, float startBpm,
            Vector2 curFloorPos, Vector2 nextFloorPos, int nextFloor, float pivotTrackOffset, string tag,
            ref bool moveRedPlanet)
        {
            bool distributed = !p.PlanetConcentrated;
            if (p.ChangeParallax && p.AffectPlanet)
            {
                LevelEvent adjust;
                if (distributed)
                    adjust = new LevelEvent(floor, LevelEventType.MoveDecorations);
                else
                {
                    adjust = new LevelEvent(p.From, LevelEventType.MoveDecorations);
                    if (!listFloors[floor].midSpin)
                    {
                        float time = trackDatas[floor].arrivalTime - trackDatas[p.From].arrivalTime;
                        adjust["angleOffset"] = time / (60f / trackDatas[p.From].bpm) * 180;
                    }
                }
                adjust.disabled["positionOffset"] = true;
                adjust.disabled["scale"] = false;
                adjust.disabled["parallax"] = false;
                adjust["duration"] = 0f;
                adjust["tag"] = tag + "_RedPlanet " + tag + "_BluePlanet";
                float t = (i - 1) / (float)(trackCount - 1);
                float parallaxValue = Mathf.Lerp(p.ParallaxFrom, p.ParallaxTo, t);
                adjust["scale"] = new Vector2(100f - parallaxValue, 100f - parallaxValue);
                adjust["parallax"] = new Vector2(parallaxValue, parallaxValue);
                editor.events.Add(adjust);
            }

            LevelEvent red1, red2, blue1, blue2;
            if (distributed)
            {
                red1 = new LevelEvent(floor, LevelEventType.MoveDecorations);
                red2 = new LevelEvent(floor, LevelEventType.MoveDecorations);
                blue1 = new LevelEvent(floor, LevelEventType.MoveDecorations);
                blue2 = new LevelEvent(floor, LevelEventType.MoveDecorations);
            }
            else
            {
                red1 = new LevelEvent(p.From, LevelEventType.MoveDecorations);
                red2 = new LevelEvent(p.From, LevelEventType.MoveDecorations);
                blue1 = new LevelEvent(p.From, LevelEventType.MoveDecorations);
                blue2 = new LevelEvent(p.From, LevelEventType.MoveDecorations);
                if (!listFloors[floor].midSpin)
                {
                    float time = trackDatas[floor].arrivalTime - trackDatas[p.From].arrivalTime;
                    float angleOffsetValue = time / (60f / trackDatas[p.From].bpm) * 180;
                    red1["angleOffset"] = angleOffsetValue;
                    red2["angleOffset"] = angleOffsetValue;
                    blue1["angleOffset"] = angleOffsetValue;
                    blue2["angleOffset"] = angleOffsetValue;
                }
            }

            foreach (var e in new[] { red1, red2, blue1, blue2 })
            {
                e.disabled["positionOffset"] = false;
                e.disabled["rotationOffset"] = false;
            }
            red1["tag"] = tag + "_RedPlanet"; red2["tag"] = tag + "_RedPlanet";
            blue1["tag"] = tag + "_BluePlanet"; blue2["tag"] = tag + "_BluePlanet";

            float spinDuration = distributed
                ? trackDatas[floor].angle / 180f
                : trackDatas[floor].angle / 180f * startBpm / trackDatas[floor].bpm;
            float durScale = distributed ? 1f : startBpm / trackDatas[floor].bpm;
            if (trackDatas[floor].isPause) spinDuration += trackDatas[floor].pauseDuration * durScale;
            if (trackDatas[floor].isHold) spinDuration += trackDatas[floor].holdDuration * 2 * durScale;
            red1["duration"] = 0f; blue1["duration"] = 0f;
            red2["duration"] = spinDuration; blue2["duration"] = spinDuration;

            bool mambo = false;
            if (moveRedPlanet)
            {
                red1["positionOffset"] = GetPivotOffset(new Vector2(curFloorPos.x - 1, curFloorPos.y), -pivotTrackOffset);
                red1["rotationOffset"] = trackDatas[floor].tail - 180;
                red2["rotationOffset"] = trackDatas[floor].tail - 180 + (listFloors[floor].isCCW ? 1 : -1) * trackDatas[floor].angle;
                blue1["positionOffset"] = GetPivotOffset(new Vector2(curFloorPos.x + 1, curFloorPos.y), -pivotTrackOffset);

                if (trackDatas[floor].isPause)
                    red2["rotationOffset"] = (float)red2["rotationOffset"] + (listFloors[floor].isCCW ? 1 : -1) * Mathf.Floor(trackDatas[floor].pauseDuration / 2.0f);
                if (trackDatas[floor].isHold)
                {
                    red2["rotationOffset"] = (float)red2["rotationOffset"] + (listFloors[floor].isCCW ? 1 : -1) * trackDatas[floor].holdDuration * 360f;
                    red2["positionOffset"] = GetPivotOffset(new Vector2(nextFloorPos.x + Mathf.Cos(trackDatas[nextFloor].tail * Mathf.Deg2Rad) - 1, nextFloorPos.y + Mathf.Sin(trackDatas[nextFloor].tail * Mathf.Deg2Rad)), -pivotTrackOffset);
                    blue2["positionOffset"] = GetPivotOffset(new Vector2(nextFloorPos.x + Mathf.Cos(trackDatas[nextFloor].tail * Mathf.Deg2Rad) + 1, nextFloorPos.y + Mathf.Sin(trackDatas[nextFloor].tail * Mathf.Deg2Rad)), -pivotTrackOffset);
                    mambo = true;
                }
                if (!listFloors[floor].midSpin)
                {
                    editor.events.Add(blue1);
                    editor.events.Add(red1);
                    editor.events.Add(red2);
                    if (mambo) editor.events.Add(blue2);
                }
            }
            else
            {
                blue1["positionOffset"] = GetPivotOffset(curFloorPos, -pivotTrackOffset);
                blue1["rotationOffset"] = trackDatas[floor].tail - 180;
                blue2["rotationOffset"] = trackDatas[floor].tail - 180 + (listFloors[floor].isCCW ? 1 : -1) * trackDatas[floor].angle;
                red1["positionOffset"] = GetPivotOffset(curFloorPos, -pivotTrackOffset);

                if (trackDatas[floor].isPause)
                    blue2["rotationOffset"] = (float)blue2["rotationOffset"] + (listFloors[floor].isCCW ? 1 : -1) * Mathf.Floor(trackDatas[floor].pauseDuration / 2.0f);
                if (trackDatas[floor].isHold)
                {
                    blue2["rotationOffset"] = (float)blue2["rotationOffset"] + (listFloors[floor].isCCW ? 1 : -1) * trackDatas[floor].holdDuration * 360f;
                    red2["positionOffset"] = GetPivotOffset(new Vector2(nextFloorPos.x + Mathf.Cos(trackDatas[nextFloor].tail * Mathf.Deg2Rad), nextFloorPos.y + Mathf.Sin(trackDatas[nextFloor].tail * Mathf.Deg2Rad)), -pivotTrackOffset);
                    blue2["positionOffset"] = GetPivotOffset(new Vector2(nextFloorPos.x + Mathf.Cos(trackDatas[nextFloor].tail * Mathf.Deg2Rad), nextFloorPos.y + Mathf.Sin(trackDatas[nextFloor].tail * Mathf.Deg2Rad)), -pivotTrackOffset);
                    mambo = true;
                }
                if (!listFloors[floor].midSpin)
                {
                    editor.events.Add(red1);
                    editor.events.Add(blue1);
                    editor.events.Add(blue2);
                    if (mambo) editor.events.Add(red2);
                }
            }
            moveRedPlanet = !moveRedPlanet;
        }

        private static void ApplyFloorIcon(LevelEvent ev, scrFloor floor, TrackData data, float trackRotation)
        {
            switch (floor.floorIcon)
            {
                case FloorIcon.None:
                    ev["trackIcon"] = CustomFloorIcon.None; break;
                case FloorIcon.Snail:
                case FloorIcon.AnimatedSnail:
                    ev["trackIcon"] = CustomFloorIcon.Snail; ev["trackIconAngle"] = 360f - trackRotation; break;
                case FloorIcon.DoubleSnail:
                case FloorIcon.AnimatedDoubleSnail:
                    ev["trackIcon"] = CustomFloorIcon.DoubleSnail; ev["trackIconAngle"] = 360f - trackRotation; break;
                case FloorIcon.Rabbit:
                case FloorIcon.AnimatedRabbit:
                    ev["trackIcon"] = CustomFloorIcon.Rabbit; ev["trackIconAngle"] = 360f - trackRotation; break;
                case FloorIcon.DoubleRabbit:
                case FloorIcon.AnimatedDoubleRabbit:
                    ev["trackIcon"] = CustomFloorIcon.DoubleRabbit; ev["trackIconAngle"] = 360f - trackRotation; break;
                case FloorIcon.Swirl:
                    ev["trackIcon"] = CustomFloorIcon.Swirl;
                    ev["trackRedSwirl"] = floor.midSpin || data.angle < 180;
                    ev["trackIconFlipped"] = floor.isCCW;
                    ev["trackIconAngle"] = floor.midSpin
                        ? data.tail - (90 + trackRotation)
                        : GetMidDirection(data.tail, data.head, floor.isCCW, true) - (90 + trackRotation);
                    break;
                case FloorIcon.Checkpoint:
                    ev["trackIcon"] = CustomFloorIcon.Checkpoint; break;
                case FloorIcon.HoldArrowShort:
                case FloorIcon.HoldArrowLong:
                    ev["trackIcon"] = CustomFloorIcon.HoldArrowLong; break;
                case FloorIcon.HoldReleaseShort:
                    ev["trackIcon"] = CustomFloorIcon.HoldReleaseShort; break;
                case FloorIcon.HoldReleaseLong:
                    ev["trackIcon"] = CustomFloorIcon.HoldReleaseLong; break;
                case FloorIcon.MultiPlanetTwo:
                    ev["trackIcon"] = CustomFloorIcon.MultiPlanetTwo; ev["trackIconAngle"] = 360f - trackRotation; break;
                case FloorIcon.MultiPlanetThreeMore:
                    ev["trackIcon"] = CustomFloorIcon.MultiPlanetThreeMore; ev["trackIconAngle"] = 360f - trackRotation; break;
                case FloorIcon.Portal:
                    ev["trackIcon"] = CustomFloorIcon.Portal; break;
            }
        }

        internal class MultiAnimParams
        {
            public int From, To;
            public bool Appear;                 // else disappear
            public bool Distributed = true;     // events per tile vs all on first tile
            public string Tag = "";
            public int TagOffset;               // MH's startTile.Item1 — shifts which copy animates
            public float Duration = 1f;
            public RandRange XPos, YPos, Rot, Scale, Parallax;
            public bool OpacityOn = true; public float Opacity;
            public float AngleOffset;
            public Ease Ease = Ease.Linear;
            public float ScaleRevertTo = 100f; public bool ScaleRevertOn = true;
            public float ParallaxRevertTo; public bool ParallaxRevertOn;
        }

        // MultipleTracks/CreateAnimation: animate the tagged copies via MoveDecorations.
        internal static void MultiTrackAnimate(MultiAnimParams p)
        {
            scnEditor editor = scnEditor.instance;
            List<scrFloor> listFloors = scrLevelMaker.instance.listFloors;
            var trackDatas = GetTrackDatas(BuildFloorEvents(editor));
            for (int floor = p.From, i = 1; floor <= p.To; floor++, i++)
            {
                LevelEvent md1, md2;
                if (p.Distributed)
                {
                    md1 = new LevelEvent(floor, LevelEventType.MoveDecorations);
                    md2 = new LevelEvent(floor, LevelEventType.MoveDecorations);
                }
                else
                {
                    md1 = new LevelEvent(p.From, LevelEventType.MoveDecorations);
                    md2 = new LevelEvent(p.From, LevelEventType.MoveDecorations);
                }

                if (!p.Appear)
                {
                    if (i + p.TagOffset <= 0) continue;
                    if (p.Distributed)
                        md2["angleOffset"] = p.AngleOffset;
                    else
                    {
                        float time = trackDatas[floor - 1].departureTime - trackDatas[p.From].arrivalTime;
                        md2["angleOffset"] = p.AngleOffset + time / (60f / trackDatas[p.From].bpm) * 180;
                    }
                    md2.disabled["positionOffset"] = !p.XPos.On && !p.YPos.On;
                    md2.disabled["rotationOffset"] = !p.Rot.On;
                    md2.disabled["scale"] = !p.Scale.On;
                    md2.disabled["opacity"] = !p.OpacityOn;
                    md2.disabled["parallax"] = !p.Parallax.On;

                    md2["tag"] = p.Tag + (i + p.TagOffset);
                    md2["duration"] = p.Duration;
                    md2["positionOffset"] = new Vector2(p.XPos.On ? p.XPos.Sample() : float.NaN, p.YPos.On ? p.YPos.Sample() : float.NaN);
                    md2["rotationOffset"] = p.Rot.Sample();
                    float parallax = p.Parallax.Sample();
                    md2["parallax"] = new Vector2(parallax, parallax);
                    float scale = p.Scale.Sample();
                    md2["scale"] = new Vector2(scale, scale);
                    md2["opacity"] = p.Opacity;
                    md2["ease"] = p.Ease;
                    editor.events.Add(md2);
                }
                else
                {
                    if (floor > p.To - (p.TagOffset >= 0 ? p.TagOffset : 0)) continue;
                    if (p.Distributed)
                    {
                        md1["angleOffset"] = p.AngleOffset;
                        md2["angleOffset"] = p.AngleOffset;
                    }
                    else
                    {
                        float time = trackDatas[floor].arrivalTime - trackDatas[p.From].arrivalTime;
                        float angleOffsetValue = p.AngleOffset + time / (60f / trackDatas[p.From].bpm) * 180;
                        md1["angleOffset"] = angleOffsetValue;
                        md2["angleOffset"] = angleOffsetValue;
                    }

                    md1.disabled["positionOffset"] = !p.XPos.On && !p.YPos.On;
                    md1.disabled["rotationOffset"] = !p.Rot.On;
                    md1.disabled["scale"] = !p.Scale.On;
                    md1.disabled["opacity"] = !p.OpacityOn;
                    md1.disabled["parallax"] = !p.Parallax.On;

                    md2.disabled["positionOffset"] = !p.XPos.On && !p.YPos.On;
                    md2.disabled["rotationOffset"] = !p.Rot.On;
                    md2.disabled["scale"] = !p.ScaleRevertOn;
                    md2.disabled["opacity"] = !p.OpacityOn;
                    md2.disabled["parallax"] = !p.ParallaxRevertOn;

                    md1["tag"] = p.Tag + (i + p.TagOffset);
                    md1["duration"] = 0f;
                    md1["positionOffset"] = new Vector2(p.XPos.On ? p.XPos.Sample() : float.NaN, p.YPos.On ? p.YPos.Sample() : float.NaN);
                    md1["rotationOffset"] = p.Rot.Sample();
                    float parallax1 = p.Parallax.Sample();
                    md1["parallax"] = new Vector2(parallax1, parallax1);
                    float scale1 = p.Scale.Sample();
                    md1["scale"] = new Vector2(scale1, scale1);
                    md1["opacity"] = 0f;

                    md2["tag"] = p.Tag + (i + p.TagOffset);
                    md2["duration"] = p.Duration;
                    md2["positionOffset"] = new Vector2(p.XPos.On ? 0f : float.NaN, p.YPos.On ? 0f : float.NaN);
                    md2["rotationOffset"] = 0f;
                    md2["parallax"] = new Vector2(p.ParallaxRevertTo, p.ParallaxRevertTo);
                    md2["scale"] = new Vector2(p.ScaleRevertTo, p.ScaleRevertTo);
                    md2["opacity"] = p.Opacity;
                    md2["ease"] = p.Ease;

                    editor.events.Add(md1);
                    editor.events.Add(md2);
                }
            }
            Finish(editor, p.From);
        }

        // MH ends each feature by re-selecting the range start (shows the new events' tile)
        private static void Finish(scnEditor editor, int firstFloor)
        {
            editor.RemakePath(true, true);
            editor.DeselectFloors();
            editor.SelectFloor(scrLevelMaker.instance.listFloors[firstFloor]);
        }
    }
}
