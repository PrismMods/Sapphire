using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sapphire
{
    // One fake tile, in tile units from fake tile 0. Rotation/TrackAngle are what the AddObject
    // floor decoration takes; Tail/Sweep/Ccw/Beat drive the planets.
    internal struct FakeTile
    {
        public double X, Y;
        public double Rotation;     // incoming heading
        public double TrackAngle;   // shape only: (rotation + 180 - exit) mod 360
        public bool Midspin, Twirl, Ccw;
        public double Tail;         // back direction, where the orbiting planet starts
        public double Sweep;        // degrees orbited on this tile; 0 on a midspin and the last tile
        public double Beat;         // fake beats from tile 0's hit
    }

    // A planet keyframe on the host tile. Both = snap both planets (duration 0) to a tile;
    // otherwise sweep only the orbiting planet's rotationOffset to Rotation over Duration.
    internal struct PlanetKey
    {
        public double Offset;       // host angleOffset, degrees
        public bool Both;
        public double X, Y;         // snap: positionOffset from tile 0, tile units
        public double Rotation;     // rotationOffset target (absolute, may leave 0..360)
        public double Duration;     // host beats
    }

    /* Pure geometry, timing and planet keyframes for a decoration-built fake track. No Unity or
       game types, so tools/multitrack-check compiles it with mcs and asserts it offline.

       The rules copy the real path (scrLevelMaker.InstantiateFloatFloors, verified in IL):
       Angles[i] is tile i's EXIT heading; 999 makes tile i a midspin, and the tile after it is
       placed back along tile i's entry, i.e. on top of the tile before it, hit in the same instant.
       Spin starts clockwise, flips on a tile's own twirl and after every midspin. Geometry was
       checked against Once Forgotten, Nothing Remains' hand-built fake floors (26/26 pairs). */
    internal static class MultitrackLayout
    {
        internal const double MidspinAngle = 999;

        internal static double Norm(double a)
        {
            a %= 360;
            return a < 0 ? a + 360 : a;
        }

        internal static FakeTile[] Build(IList<double> angles, ICollection<int> twirls, double size)
        {
            int n = (angles != null ? angles.Count : 0) + 1;
            var t = new FakeTile[n];
            double x = 0, y = 0, prevX = 0, prevY = 0;
            double head = 0, tail = 180;    // tile 0 is entered moving right, like floor 0
            bool ccw = false;
            double beat = 0;
            for (int i = 0; i < n; i++)
            {
                bool tw = twirls != null && twirls.Contains(i);
                if (tw) ccw = !ccw;
                var f = new FakeTile
                {
                    X = x, Y = y, Rotation = Norm(tail + 180), Tail = Norm(tail),
                    Twirl = tw, Ccw = ccw, Beat = beat,
                };
                if (i == n - 1) { f.TrackAngle = 180; t[i] = f; break; }
                if (angles[i] == MidspinAngle)
                {
                    f.Midspin = true;
                    t[i] = f;
                    double nx = prevX, ny = prevY;
                    prevX = x; prevY = y;
                    x = nx; y = ny;
                    tail = head;    // the next tile's back points at this stub
                    ccw = !ccw;
                    continue;
                }
                double exit = Norm(angles[i]);
                f.TrackAngle = Norm(f.Rotation + 180 - exit);
                double sweep = ccw ? Norm(exit - tail) : Norm(tail - exit);
                if (sweep == 0) sweep = 360;
                f.Sweep = sweep;
                t[i] = f;
                beat += sweep / 180.0;
                prevX = x; prevY = y;
                x += size * Math.Cos(exit * Math.PI / 180.0);
                y += size * Math.Sin(exit * Math.PI / 180.0);
                head = exit;
                tail = Norm(exit + 180);
            }
            return t;
        }
        internal static double HostOffset(double fakeBeat, double startBeat, double hostBpm, double fakeBpm)
            => 180.0 * (startBeat + fakeBeat * hostBpm / fakeBpm);

        /* OFNR's scheme: one planet sits on the tile and the other orbits it; they never swap roles,
           both are the same colour, so the swap a real track does is invisible. A midspin tile
           emits nothing: the tile after it snaps at the same instant, back onto the old pivot,
           with the orbiter already on the stub. The sweep's numeric direction is the spin: a
           tween from 180 to 90 turns clockwise, 180 to 450 counter-clockwise. */
        internal static List<PlanetKey> Planets(FakeTile[] t, double startBeat, double hostBpm, double fakeBpm)
        {
            var keys = new List<PlanetKey>();
            double ratio = hostBpm / fakeBpm;
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i].Midspin) continue;
                double at = HostOffset(t[i].Beat, startBeat, hostBpm, fakeBpm);
                keys.Add(new PlanetKey { Offset = at, Both = true, X = t[i].X, Y = t[i].Y, Rotation = t[i].Tail });
                if (t[i].Sweep <= 0) continue;
                keys.Add(new PlanetKey
                {
                    Offset = at,
                    Rotation = t[i].Tail + (t[i].Ccw ? t[i].Sweep : -t[i].Sweep),
                    Duration = t[i].Sweep / 180.0 * ratio,
                });
            }
            return keys;
        }

        // Fake tiles fade in two host beats before the first hit; a negative offset is not allowed.
        internal static double RevealOffset(double startBeat) => Math.Max(0, 180.0 * (startBeat - 2));

        // The planet leaves tile i when tile i+1 is hit.
        internal static double LeaveOffset(FakeTile[] t, int i, double startBeat, double hostBpm, double fakeBpm)
            => i + 1 < t.Length ? HostOffset(t[i + 1].Beat, startBeat, hostBpm, fakeBpm)
                                : EndOffset(t, startBeat, hostBpm, fakeBpm);

        internal static double EndOffset(FakeTile[] t, double startBeat, double hostBpm, double fakeBpm)
            => HostOffset(t[t.Length - 1].Beat, startBeat, hostBpm, fakeBpm) + 180.0;

        // Tags are space-separated tokens; "smt1" must not match "smt12".
        internal static bool HasToken(string tags, string token)
        {
            if (string.IsNullOrEmpty(tags) || string.IsNullOrEmpty(token)) return false;
            foreach (var s in tags.Split(' ')) if (s == token) return true;
            return false;
        }
    }

    /* A multitrack as saved in its host's EditorComment. Edits mirror the real editor: a key on
       tile c sets c's exit and the new tile c+1 inherits the old one, so tiles further on keep
       their absolute headings; 999 turns the cursor tile itself into a midspin. */
    internal sealed class MultitrackModel
    {
        internal const string Prefix = "#sapphire-multitrack";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public int Id = 1;
        public double Bpm = 120, StartBeat, Size = 1, OriginX, OriginY = 3;
        public double HostBpm;   // host tempo the events were written for; a change makes them stale
        public List<double> Angles = new List<double>();
        public HashSet<int> Twirls = new HashSet<int>();
        public string TrackColor = "ffffff", PlanetColor = "ffffff";
        public bool HideOutside = true, FadePassed = true;

        internal string Tag => "smt" + Id;
        internal int TileCount => Angles.Count + 1;
        internal int Clamp(int c) => Math.Max(0, Math.Min(c, Angles.Count));

        internal int Insert(int cursor, double angle)
        {
            cursor = Clamp(cursor);
            Angles.Insert(cursor, angle);
            Shift(cursor + 1, +1);
            return cursor + 1;
        }

        // -1 = refused (tile 0 is the track's start).
        internal int Delete(int cursor)
        {
            cursor = Clamp(cursor);
            if (cursor == 0) return -1;
            Angles.RemoveAt(cursor - 1);
            Twirls.Remove(cursor);
            Shift(cursor + 1, -1);
            return cursor - 1;
        }

        internal void ToggleTwirl(int cursor)
        {
            cursor = Clamp(cursor);
            if (!Twirls.Remove(cursor)) Twirls.Add(cursor);
        }

        private void Shift(int from, int by)
        {
            var moved = new List<int>();
            foreach (int i in Twirls) if (i >= from) moved.Add(i);
            foreach (int i in moved) Twirls.Remove(i);
            foreach (int i in moved) Twirls.Add(i + by);
        }

        internal string Serialize()
        {
            var sb = new StringBuilder(Prefix).Append(" v1");
            sb.Append(" id=").Append(Id);
            sb.Append(" bpm=").Append(Bpm.ToString("R", Inv));
            sb.Append(" hb=").Append(HostBpm.ToString("R", Inv));
            sb.Append(" start=").Append(StartBeat.ToString("R", Inv));
            sb.Append(" size=").Append(Size.ToString("R", Inv));
            sb.Append(" ox=").Append(OriginX.ToString("R", Inv));
            sb.Append(" oy=").Append(OriginY.ToString("R", Inv));
            var a = new List<string>();
            foreach (var v in Angles) a.Add(v.ToString("R", Inv));
            sb.Append(" a=").Append(string.Join(",", a.ToArray()));
            var tw = new List<int>(Twirls); tw.Sort();
            var ts = new List<string>();
            foreach (var v in tw) ts.Add(v.ToString(Inv));
            sb.Append(" tw=").Append(string.Join(",", ts.ToArray()));
            sb.Append(" tc=").Append(TrackColor).Append(" pc=").Append(PlanetColor);
            sb.Append(" hide=").Append(HideOutside ? 1 : 0).Append(" fade=").Append(FadePassed ? 1 : 0);
            return sb.ToString();
        }

        // Null when the text is not a v1 multitrack comment. Clamps what a hand edit could break:
        // BPM 0 divides by zero and a negative start is a negative angleOffset.
        internal static MultitrackModel Parse(string text)
        {
            if (text == null || !text.StartsWith(Prefix + " v1")) return null;
            var m = new MultitrackModel();
            foreach (var part in text.Split(' '))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq), v = part.Substring(eq + 1);
                switch (k)
                {
                    case "id": int.TryParse(v, NumberStyles.Integer, Inv, out m.Id); break;
                    case "bpm": m.Bpm = Num(v, m.Bpm); break;
                    case "hb": m.HostBpm = Num(v, 0); break;
                    case "start": m.StartBeat = Num(v, 0); break;
                    case "size": m.Size = Num(v, 1); break;
                    case "ox": m.OriginX = Num(v, 0); break;
                    case "oy": m.OriginY = Num(v, 3); break;
                    case "a": foreach (var s in v.Split(',')) if (s.Length > 0) m.Angles.Add(Num(s, 0)); break;
                    case "tw": foreach (var s in v.Split(',')) { int i; if (int.TryParse(s, NumberStyles.Integer, Inv, out i)) m.Twirls.Add(i); } break;
                    case "tc": m.TrackColor = v; break;
                    case "pc": m.PlanetColor = v; break;
                    case "hide": m.HideOutside = v == "1"; break;
                    case "fade": m.FadePassed = v == "1"; break;
                }
            }
            m.Bpm = Math.Max(1, m.Bpm);
            m.StartBeat = Math.Max(0, m.StartBeat);
            if (m.Size <= 0) m.Size = 1;
            return m;
        }

        private static double Num(string s, double fallback)
        {
            double d;
            return double.TryParse(s, NumberStyles.Float, Inv, out d) ? d : fallback;
        }
    }
}
