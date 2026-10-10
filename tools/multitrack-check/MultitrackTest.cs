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
    // 5. Planet keys: snap both on each hit, then sweep the orbiter; durations in HOST beats.
    t = MultitrackLayout.Build(new List<double> { 0 }, null, 1);
    var k = MultitrackLayout.Planets(t, 0, 120, 240);
    Check(k.Count == 3, "straight 2-tile track: snap, sweep, snap; got " + k.Count);
    Check(k[0].Both && Near(k[0].Offset, 0) && NearDeg(k[0].Rotation, 180), "tile 0 snap at 0, orbiter at the back");
    Check(!k[1].Both && Near(k[1].Rotation, 0) && Near(k[1].Duration, 0.5), "sweep 180 CW to 0 over 0.5 host beat at 2x");
    Check(k[2].Both && Near(k[2].Offset, 90) && Near(k[2].X, 1), "tile 1 snap at 90 deg, x=1");
    t = MultitrackLayout.Build(new List<double> { 90 }, new HashSet<int> { 0 }, 1);
    k = MultitrackLayout.Planets(t, 0, 120, 120);
    Check(Near(k[1].Rotation, 450) && Near(k[1].Duration, 1.5), "CCW sweep counts up: 180 -> 450 over 1.5, got " + k[1].Rotation);
    t = MultitrackLayout.Build(new List<double> { 0, 999, 180 }, null, 1);
    k = MultitrackLayout.Planets(t, 0, 120, 120);
    Check(k.Count == 5, "midspin tile emits nothing: 0 snap+sweep, 2 snap+sweep, 3 snap; got " + k.Count);
    Check(k[2].Both && Near(k[2].Offset, 180) && Near(k[2].X, 0), "tile after the midspin snaps back onto tile 0 at beat 1");
    Check(Near(MultitrackLayout.HostOffset(2, 1, 120, 240), 360), "offset = 180*(start + beat*host/fake)");
    Check(Near(MultitrackLayout.RevealOffset(1), 0) && Near(MultitrackLayout.RevealOffset(5), 540), "reveal 2 host beats early, clamped at 0");
    t = MultitrackLayout.Build(new List<double> { 0, 0 }, null, 1);
    Check(Near(MultitrackLayout.LeaveOffset(t, 0, 0, 120, 120), 180) && Near(MultitrackLayout.EndOffset(t, 0, 120, 120), 540), "leave = next hit; end = last hit + 1 beat");
    // 6. Tokens: smt1 is not a prefix match for smt12.
    Check(MultitrackLayout.HasToken("smt1 smt1f3", "smt1f3") && !MultitrackLayout.HasToken("smt12 smt12f3", "smt1"), "exact tag tokens");
    // 7. Edits mirror the editor: insert after the cursor, twirls ride their tiles.
    var m = new MultitrackModel();
    int c = m.Insert(0, 0); c = m.Insert(c, 90); c = m.Insert(c, 0);
    Check(c == 3 && m.TileCount == 4, "three inserts at the end make 4 tiles");
    m.ToggleTwirl(2);
    c = m.Insert(1, 45);
    Check(c == 2 && m.Angles[1] == 45 && m.Angles[2] == 90, "mid insert: cursor exit becomes 45, new tile keeps the old exit");
    Check(m.Twirls.Contains(3) && !m.Twirls.Contains(2), "twirl after the cursor shifts with its tile");
    Check(m.Delete(0) == -1 && m.TileCount == 5, "tile 0 cannot be deleted");
    c = m.Delete(3);
    Check(c == 2 && m.TileCount == 4 && !m.Twirls.Contains(3), "delete removes the tile and its twirl");
    c = m.Delete(m.TileCount - 1);
    Check(c == 2 && m.TileCount == 3, "deleting the last tile");
    c = m.Insert(m.TileCount - 1, 999);
    Check(m.Angles[m.Angles.Count - 1] == 999, "midspin key turns the cursor tile into a midspin");
    // 8. Persistence round trip and clamps.
    m.Id = 12; m.Bpm = 234; m.HostBpm = 117; m.StartBeat = 4; m.Size = 1.5; m.OriginX = -2; m.OriginY = 3.5;
    var back = MultitrackModel.Parse(m.Serialize());
    Check(back != null && back.Id == 12 && Near(back.Bpm, 234) && Near(back.HostBpm, 117) && Near(back.Size, 1.5) && Near(back.OriginY, 3.5), "round trip scalars");
    Check(back != null && back.Angles.Count == m.Angles.Count && back.Angles[back.Angles.Count - 1] == 999, "round trip angles");
    Check(back != null && back.Twirls.SetEquals(m.Twirls) && back.Tag == "smt12", "round trip twirls + tag");
    Check(MultitrackModel.Parse("113.5 BPM") == null && MultitrackModel.Parse(null) == null, "a normal editor comment is not ours");
    var bad = MultitrackModel.Parse(MultitrackModel.Prefix + " v1 id=1 bpm=0 start=-3 size=0");
    Check(bad != null && bad.Bpm >= 1 && bad.StartBeat >= 0 && bad.Size > 0, "bpm/start/size clamp");
    // 9. Hits the tag fires on: every landing (tile 1 on), a midspin and its follower count once.
    t = MultitrackLayout.Build(new List<double> { 0, 999, 180 }, null, 1);
    var hb = MultitrackLayout.HitBeats(t);
    Check(hb.Count == 2 && Near(hb[0], 1) && Near(hb[1], 2), "hit beats skip tile 0 and the midspin stub, got " + string.Join(",", hb));
    t = MultitrackLayout.Build(new List<double> { 0, 0 }, null, 1);
    hb = MultitrackLayout.HitBeats(t);
    Check(hb.Count == 2 && Near(hb[0], 1) && Near(hb[1], 2), "straight track hits at 1 and 2");
    // 10. End tile + hit tag persist; a tag cannot carry spaces (the saved line splits on them).
    var e2 = new MultitrackModel { EndTile = true, HitTag = "kick drum" };
    var eb = MultitrackModel.Parse(e2.Serialize());
    Check(eb != null && eb.EndTile && eb.HitTag == "kickdrum", "end tile + hit tag round trip, got " + (eb == null ? "null" : eb.HitTag));
    Check(MultitrackModel.Parse(new MultitrackModel().Serialize()).HitTag == "" && !MultitrackModel.Parse(new MultitrackModel().Serialize()).EndTile, "defaults: no tag, no end tile");
    // 11. Press mirrors the editor: the key pointing back along the entry deletes, others insert.
    var pm = new MultitrackModel(); pm.Angles.Add(0);           // tile 1 entered moving right; back = 180
    Check(pm.Press(1, 90) == 2 && pm.TileCount == 3, "a side key inserts after the cursor");
    pm = new MultitrackModel(); pm.Angles.Add(0);
    Check(pm.Press(1, 180) == 0 && pm.TileCount == 1, "the back key deletes the cursor tile");
    Check(pm.Press(0, 180) == 1 && pm.TileCount == 2, "tile 0 has no tile behind it: back key inserts a U-turn");
    pm = new MultitrackModel(); pm.Angles.Add(0);
    Check(pm.Press(1, 999) == 2 && pm.Angles[1] == 999, "midspin key never deletes");
    Check(MultitrackModel.IsBack(MultitrackLayout.Build(new List<double> { 0 }, null, 1), 1, 180), "IsBack: 180 behind a rightward tile");
    Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
    Environment.Exit(fails == 0 ? 0 : 1);
  }
}
