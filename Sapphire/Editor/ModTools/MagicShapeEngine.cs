using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ADOFAI;

namespace Sapphire
{
    /* Magic-shape math, ported with permission:
       • Multiply/Create — MagicShapeMultiply by tjwogud (continued by JofoDuh)
       • Rotate + create's event re-index — AdofaiMappingHelper by Sprout34
       Logic is kept verbatim where possible; only the EditorTabLib UI layer was replaced
       (parameters come from EditorMagicShape instead of a custom inspector tab).

       Result codes (second item carries message params):
        1 ok · -1 no editor · -2 selection <2 tiles · -3 range holds FreeRoam/Pause/Hold
       -4 angle >360° and no correction chosen · -5 old-level angle not a multiple of 15° */
    internal static class MagicShapeEngine
    {
        internal enum MultiplyType { Bpm, Multiplier }
        internal enum TwirlDir { Internal, External, None }
        internal enum ShowEvent { SetSpeed, Twirl }

        private static readonly List<LevelEventType> ExceptionEventTypes = new List<LevelEventType>
            { LevelEventType.FreeRoam, LevelEventType.Pause, LevelEventType.Hold };

        internal static Tuple<int, Dictionary<string, object>> MultiplyWithBPM(
            double bpm, MultiplyType setSpeedType, ShowEvent showEvent, TwirlDir? direction = null, List<scrFloor> floors = null)
        {
            scnEditor editor = scnEditor.instance;
            if (editor == null)
                return Tuple.Create<int, Dictionary<string, object>>(-1, null);
            List<scrFloor> listFloors = scrLevelMaker.instance.listFloors;
            if (floors == null) floors = SortedSelection(editor);
            if (floors == null || floors.Count <= 1)
                return Tuple.Create<int, Dictionary<string, object>>(-2, null);
            var removeEvents = new List<LevelEvent>();
            var err = CollectExceptions(editor, floors, direction != null, removeEvents);
            if (err != null) return err;
            removeEvents.ForEach(e => editor.events.Remove(e));
            editor.ApplyEventsToFloors();
            InsertAutoTwirls(editor, listFloors, floors, direction);
            editor.ApplyEventsToFloors();
            double prevBpm = editor.levelData.bpm * (GetFloor(floors[0].seqID - 1)?.speed ?? 1);
            for (int i = floors[0].seqID; i <= floors.Last().seqID; i++)
            {
                scrFloor floor = listFloors[i];
                if (floor.nextfloor == null) continue;
                if (floor.midSpin) continue;
                double angle = GetAngleLength(floor);
                double applyBpm = bpm * angle / 180;
                if (Math.Abs(applyBpm - prevBpm) <= 0.001) continue;
                AddSetSpeed(editor, floor, applyBpm, prevBpm, setSpeedType, showEvent);
                prevBpm = applyBpm;
            }
            editor.RemakePath(true);
            return Tuple.Create<int, Dictionary<string, object>>(1, null);
        }

        internal static Tuple<int, Dictionary<string, object>> MultiplyWithMultiplier(
            double multiplier, MultiplyType setSpeedType, ShowEvent showEvent, TwirlDir? direction = null, List<scrFloor> floors = null)
        {
            scnEditor editor = scnEditor.instance;
            if (editor == null)
                return Tuple.Create<int, Dictionary<string, object>>(-1, null);
            List<scrFloor> listFloors = scrLevelMaker.instance.listFloors;
            if (floors == null) floors = SortedSelection(editor);
            if (floors == null || floors.Count <= 1)
                return Tuple.Create<int, Dictionary<string, object>>(-2, null);
            // existing SetSpeeds are edited in place (keeps their icon slot) rather than removed
            var floorEvents = new LevelEvent[listFloors.Count];
            var removeEvents = new List<LevelEvent>();
            var err = CollectExceptions(editor, floors, direction != null, removeEvents, floorEvents);
            if (err != null) return err;
            removeEvents.ForEach(e => editor.events.Remove(e));
            editor.ApplyEventsToFloors();
            InsertAutoTwirls(editor, listFloors, floors, direction);
            editor.ApplyEventsToFloors();
            double prevBpm = editor.levelData.bpm * (GetFloor(floors[0].seqID - 1)?.speed ?? 1);
            for (int i = floors[0].seqID; i <= floors.Last().seqID; i++)
            {
                scrFloor floor = listFloors[i];
                if (floor.nextfloor == null) continue;
                if (floor.midSpin) continue;
                double bpm = floor.speed * editor.levelData.bpm;
                double applyBpm = bpm * multiplier;
                if (applyBpm / prevBpm == 1)
                {
                    editor.events.Remove(floorEvents[i - floors[0].seqID]);
                    continue;
                }
                AddSetSpeed(editor, floor, applyBpm, prevBpm, setSpeedType, showEvent, floorEvents[i - floors[0].seqID]);
                prevBpm = applyBpm;
            }
            editor.RemakePath(true);
            return Tuple.Create<int, Dictionary<string, object>>(1, null);
        }

        // "Reshape" mode: keep the rhythm, rewrite the tile angles themselves by 1/multiplier.
        internal static Tuple<int, Dictionary<string, object>> MultiplyWithAngle(
            double multiplier, int? angleCorrection = null, List<scrFloor> floors = null)
        {
            scnEditor editor = scnEditor.instance;
            if (editor == null)
                return Tuple.Create<int, Dictionary<string, object>>(-1, null);
            List<scrFloor> listFloors = scrLevelMaker.instance.listFloors;
            if (floors == null) floors = SortedSelection(editor);
            if (floors == null || floors.Count <= 1)
                return Tuple.Create<int, Dictionary<string, object>>(-2, null);
            var err = CollectExceptions(editor, floors, false, null);
            if (err != null) return err;
            double totalValue = 0;
            var pathDataCopy = new StringBuilder(editor.levelData.pathData);
            List<float> angleDataCopy = editor.levelData.angleData.ToList();
            for (int i = floors[0].seqID; i <= floors.Last().seqID - 1; i++)
            {
                scrFloor floor = listFloors[i];
                if (floor.nextfloor == null) continue;
                if (floor.midSpin) continue;
                if (floor.seqID == 0) continue;
                double nextAngle = GetAngle(GetFloor(floor.seqID + 1));
                double angleLength = GetAngleLength(floor);
                double value = FixAngle((angleLength / multiplier - angleLength) * (floor.isCCW ? -1 : 1) * (editor.levelData.isOldLevel ? 1 : -1));
                totalValue = FixAngle(totalValue + value);
                float applyAngle = (float)FixAngle(nextAngle + totalValue);
                if (angleLength / multiplier > 360)
                {
                    if (angleCorrection == null)
                    {
                        var dict = new Dictionary<string, object> { ["floor"] = floor.seqID };
                        return Tuple.Create(-4, dict);
                    }
                    // >360° span survives as a Pause event holding the extra revolutions
                    LevelEvent ev = new LevelEvent(floor.seqID, LevelEventType.Pause);
                    ev["duration"] = (float)((int)(angleLength / multiplier / 360) + 1);
                    ev["angleCorrectionDir"] = angleCorrection.Value;
                    editor.events.Add(ev);
                }
                if (editor.levelData.isOldLevel)
                {
                    if (applyAngle % 15 != 0)
                    {
                        var dict = new Dictionary<string, object>
                        { ["floor"] = floor.seqID + 1, ["angle"] = nextAngle, ["changedAngle"] = applyAngle };
                        return Tuple.Create(-5, dict);
                    }
                    pathDataCopy.Remove(floor.seqID, 1);
                    pathDataCopy.Insert(floor.seqID, PathChars[(int)applyAngle / 15]);
                }
                else
                {
                    angleDataCopy.RemoveAt(floor.seqID);
                    angleDataCopy.Insert(floor.seqID, applyAngle);
                }
            }
            editor.levelData.pathData = pathDataCopy.ToString();
            editor.levelData.angleData = angleDataCopy;
            editor.RemakePath(true);
            return Tuple.Create<int, Dictionary<string, object>>(1, null);
        }

        /* Appends (vertexCount-1) rotated copies of the tile range's angles — the range swept
           through a full turn makes the "magic circle". Later events/decorations are re-indexed
           past the insert (MappingHelper's fix; MagicShapeMultiply left them behind). */
        internal static void CreateShape(int startIndex, int endIndex, int vertexCount, bool inverseAngle)
        {
            scnEditor editor = scnEditor.instance;
            if (editor == null) return;
            if (startIndex > endIndex) { int t = startIndex; startIndex = endIndex; endIndex = t; }
            int inverse = inverseAngle ? -1 : 1;
            var angles = new List<float>();
            for (int i = 1; i < vertexCount; i++)
                for (int j = startIndex; j <= endIndex; j++)
                {
                    float angle = j == 0 ? 0 : ADOBase.lm.listFloors[j].floatDirection;
                    angles.Add(angle == 999 ? 999 : (float)(angle - 360f / vertexCount * i * inverse));
                }
            editor.levelData.angleData.InsertRange(endIndex, angles);
            TrackToolsEngine.OffsetFloorIDsInEvents(editor, endIndex, angles.Count);
            editor.RemakePath();
        }

        // MappingHelper: rotate every non-midspin tile's angle in [from..to] by `degrees`.
        internal static void Rotate(int from, int to, float degrees)
        {
            scnEditor editor = scnEditor.instance;
            if (editor == null) return;
            if (from > to) { int t = from; from = to; to = t; }
            List<scrFloor> listFloors = scrLevelMaker.instance.listFloors;
            List<float> angleData = editor.levelData.angleData;
            for (int floor = from; floor <= to; floor++)
            {
                if (floor >= angleData.Count) break;
                if (floor < listFloors.Count && listFloors[floor].midSpin) continue;
                if (angleData[floor] == 999) continue;
                angleData[floor] = (float)FixAngle(angleData[floor] + degrees);
            }
            editor.RemakePath(true, true);
        }

        // ── shared multiply steps ─────────────────────────────────────────────

        private static List<scrFloor> SortedSelection(scnEditor editor)
        {
            var sel = editor.selectedFloors;
            if (sel == null) return null;
            return sel.Where(f => f != null).OrderBy(f => f.seqID).ToList();
        }

        /* Scans the range: SetSpeeds get removed (or slotted for in-place edit when
           `floorEvents` given); Twirls removed only when a twirl direction will be re-derived;
           FreeRoam/Pause/Hold abort — their timing math doesn't survive a retime. */
        private static Tuple<int, Dictionary<string, object>> CollectExceptions(
            scnEditor editor, List<scrFloor> floors, bool stripTwirls,
            List<LevelEvent> removeEvents, LevelEvent[] floorEvents = null)
        {
            var exceptionEvents = new List<LevelEvent>();
            var exceptionTypes = new List<LevelEventType>();
            foreach (LevelEvent e in editor.events.Where(e => e.floor >= floors[0].seqID && e.floor <= floors.Last().seqID))
                switch (e.eventType)
                {
                    case LevelEventType.SetSpeed:
                        if (floorEvents != null) floorEvents[e.floor - floors[0].seqID] = e;
                        else if (removeEvents != null) removeEvents.Add(e);
                        break;
                    case LevelEventType.Twirl:
                        if (stripTwirls && removeEvents != null) removeEvents.Add(e);
                        break;
                    default:
                        if (ExceptionEventTypes.Contains(e.eventType))
                        {
                            exceptionEvents.Add(e);
                            if (!exceptionTypes.Contains(e.eventType)) exceptionTypes.Add(e.eventType);
                        }
                        break;
                }
            if (exceptionTypes.Count != 0)
            {
                var dict = new Dictionary<string, object> { ["events"] = exceptionEvents, ["eventTypes"] = exceptionTypes };
                return Tuple.Create(-3, dict);
            }
            return null;
        }

        // Internal = twirl toward the acute side at each vertex; External = obtuse.
        private static void InsertAutoTwirls(scnEditor editor, List<scrFloor> listFloors, List<scrFloor> floors, TwirlDir? direction)
        {
            if (direction != TwirlDir.Internal && direction != TwirlDir.External) return;
            bool ccw = floors[0].isCCW;
            for (int i = floors[0].seqID; i <= floors.Last().seqID; i++)
            {
                scrFloor floor = listFloors[i];
                if (floor.nextfloor == null) continue;
                if (floor.midSpin) continue;
                double angle = GetAngleLength(floor, ccw);
                if ((direction == TwirlDir.Internal) == angle > GetAngleLength(floor, !ccw))
                {
                    editor.events.Add(new LevelEvent(EventFloorFor(floor), LevelEventType.Twirl));
                    ccw = !ccw;
                }
            }
        }

        // events can't sit on a midspin — walk back to the tile that owns it
        private static int EventFloorFor(scrFloor floor)
        {
            int eventFloor = floor.seqID;
            if (floor.seqID > 0)
                while (GetFloor(eventFloor - 1).midSpin)
                    eventFloor--;
            return eventFloor;
        }

        private static void AddSetSpeed(scnEditor editor, scrFloor floor, double applyBpm, double prevBpm,
            MultiplyType setSpeedType, ShowEvent showEvent, LevelEvent existing = null)
        {
            LevelEvent item = existing ?? new LevelEvent(EventFloorFor(floor), LevelEventType.SetSpeed);
            if (setSpeedType == MultiplyType.Bpm)
            {
                item["speedType"] = SpeedType.Bpm;
                item["beatsPerMinute"] = (float)applyBpm;
            }
            else
            {
                item["speedType"] = SpeedType.Multiplier;
                item["bpmMultiplier"] = (float)(applyBpm / prevBpm);
            }
            // list order decides which bubble draws on top of the tile
            if (showEvent == ShowEvent.Twirl) editor.events.Add(item);
            else editor.events.Insert(0, item);
        }

        // ── angle helpers (MagicShapeMultiply's, verbatim) ───────────────────

        internal static double GetAngleLength(scrFloor floor, bool? ccw = null)
        {
            double angle = GetAngle(floor);
            if (scnGame.instance.levelData.isOldLevel)
            {
                angle = (ccw ?? floor.isCCW)
                    ? angle - GetAngle(floor.nextfloor)
                    : -angle + GetAngle(floor.nextfloor);
            }
            else
            {
                angle = !(ccw ?? floor.isCCW)
                    ? angle - GetAngle(floor.nextfloor)
                    : -angle + GetAngle(floor.nextfloor);
            }
            angle += 180;
            if (floor.numPlanets > 2 && floor.prevfloor && !floor.prevfloor.midSpin)
                angle -= 180f * (floor.numPlanets - 2) / floor.numPlanets;
            angle = FixAngle(angle);
            return angle == 0 ? 360 : angle;
        }

        internal static double GetAngle(scrFloor floor)
        {
            double angle;
            if (scnEditor.instance.levelData.isOldLevel)
            {
                if (floor.seqID == 0) return 90;
                if (PathChar(floor.seqID - 1) == '!')
                    return FixAngle(GetAngle(GetFloor(floor.seqID - 1)) + 180);
                angle = DefaultAngle(floor);
                if (angle == -1)
                {
                    scrFloor prev = GetFloor(floor.seqID - 1);
                    angle = PathChar(floor.seqID - 1) == '7'
                        ? GetAngle(prev) - 180 + 900 / 7
                        : GetAngle(prev) - 180 + 108;
                }
            }
            else
            {
                if (floor.seqID == 0) return 0;
                if (GetFloor(floor.seqID - 1).midSpin)
                    return FixAngle(GetAngle(GetFloor(floor.seqID - 1)) + 180);
                angle = FixAngle(scnEditor.instance.levelData.angleData[floor.seqID - 1]);
            }
            return FixAngle(angle);
        }

        private static char PathChar(int id) => scnEditor.instance.levelData.pathData[id];

        internal static double FixAngle(double angle)
        {
            if (angle == 999) return angle;
            while (angle >= 360) angle -= 360;
            while (angle < 0) angle += 360;
            return angle;
        }

        internal static scrFloor GetFloor(int id)
        {
            var list = scrLevelMaker.instance.listFloors;
            return id < 0 || id >= list.Count ? null : list[id];
        }

        private const string PathChars = "UoTEJpRAMCBYDVFZNxLWHQGq";

        private static double DefaultAngle(scrFloor floor) => PathChars.IndexOf(PathChar(floor.seqID - 1)) * 15;
    }
}
