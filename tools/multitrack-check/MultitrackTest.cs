// Offline check for the multitrack pure layer — no game needed:
//   mcs -out:/tmp/mt.exe tools/multitrack-check/MultitrackTest.cs Sapphire/Editor/Charting/MultitrackLayout.cs && mono /tmp/mt.exe
// Prints ALL PASS or each failure.
using System; using System.Collections.Generic; using Sapphire;
static class T {
  static int fails = 0;
  static void Check(bool c, string m) { if (!c) { fails++; Console.WriteLine("FAIL " + m); } }
  static bool Near(double a, double b) => Math.Abs(a - b) < 1e-3;
  static bool NearDeg(double a, double b) => Math.Abs(MultitrackLayout.Norm(a - b + 180) - 180) < 1e-3;
  static void Main() {
    // 1. Geometry reproduces Once Forgotten, Nothing Remains floor 4584, tags rz401..rz415 (size 1.5).
    var ofnr = new List<double> { 0, 0, 90, 90, 0, 225, 0, 225, 0, 0, 90, 90, 0, 0 };
    var t = MultitrackLayout.Build(ofnr, new HashSet<int>(), 1.5);
    double[,] pos = { {0,0},{1.5,0},{3,0},{3,1.5},{3,3},{4.5,3},{3.43934,1.93934},{4.93934,1.93934},
      {3.87868,0.87868},{5.37868,0.87868},{6.87868,0.87868},{6.87868,2.37868},{6.87868,3.87868},{8.37868,3.87868},{9.87868,3.87868} };
    double[] rot = { 0,0,0,90,90,0,225,0,225,0,0,90,90,0,0 };
    double[] ta = { 180,180,90,180,270,315,45,315,45,180,90,180,270,180,180 };
    Check(t.Length == 15, "OFNR tile count 15, got " + t.Length);
    for (int i = 0; i < 15 && i < t.Length; i++) {
      Check(Near(t[i].X, pos[i,0]) && Near(t[i].Y, pos[i,1]), "OFNR pos " + i + " got " + t[i].X + "," + t[i].Y);
      Check(NearDeg(t[i].Rotation, rot[i]), "OFNR rotation " + i + " got " + t[i].Rotation);
      Check(NearDeg(t[i].TrackAngle, ta[i]), "OFNR trackAngle " + i + " got " + t[i].TrackAngle);
    }
    // 2. Timing: beat = sweep / 180, clockwise from a tile's back to its exit.
    t = MultitrackLayout.Build(new List<double> { 0, 0 }, null, 1);
    Check(Near(t[1].Beat, 1) && Near(t[2].Beat, 2), "straight = 1 beat per tile");
    t = MultitrackLayout.Build(new List<double> { 0, 90 }, null, 1);
    Check(Near(t[2].Beat, 1.5), "quarter left on CW = 90 deg = 0.5 beat, got " + t[2].Beat);
    t = MultitrackLayout.Build(new List<double> { 0, 180 }, null, 1);
    Check(Near(t[2].Beat, 3), "U-turn = 360 deg = 2 beats, got " + t[2].Beat);
    // 3. A twirl turns the planet on its own tile.
    t = MultitrackLayout.Build(new List<double> { 0, 90 }, new HashSet<int> { 1 }, 1);
    Check(t[1].Ccw && Near(t[1].Sweep, 270) && Near(t[2].Beat, 2.5), "twirl on tile 1 makes 90 left a 270 sweep, got " + t[1].Sweep);
    // 4. Midspin: no time, next tile back on the one before it, spin flips.
    t = MultitrackLayout.Build(new List<double> { 0, 999, 180 }, null, 1);
    Check(t[1].Midspin && Near(t[1].X, 1), "midspin stub at x=1");
    Check(Near(t[2].X, 0) && Near(t[2].Y, 0), "tile after midspin lands on tile 0, got " + t[2].X);
    Check(Near(t[2].Beat, t[1].Beat), "midspin takes no time");
    Check(t[2].Ccw, "spin flips after a midspin");
    Check(NearDeg(t[2].Tail, 0), "back of tile 2 points at the stub, got " + t[2].Tail);
    Check(Near(t[3].X, -1) && Near(t[3].Beat, 2), "continues left, 1 beat later, got x=" + t[3].X + " beat=" + t[3].Beat);
    Check(Near(t[t.Length - 1].Sweep, 0) && NearDeg(t[t.Length - 1].TrackAngle, 180), "last tile rests, straight shape");
    Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
    Environment.Exit(fails == 0 ? 0 : 1);
  }
}
