using System;
using System.Collections.Generic;

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
    }
}
