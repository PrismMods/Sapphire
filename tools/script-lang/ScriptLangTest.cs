// Offline check for Sapphire Script (Sapphire/Editor/Level/ScriptLang.cs) — no game needed:
//   mcs -out:/tmp/st.exe tools/script-lang/ScriptLangTest.cs Sapphire/Editor/Level/ScriptLang.cs && mono /tmp/st.exe
// Prints ALL PASS or each failure.
using System; using System.Collections.Generic; using System.Linq; using Sapphire.Script;
static class T {
  static int fails = 0;
  static void Check(bool c, string m) { if (!c) { fails++; Console.WriteLine("FAIL " + m); } }
  static Output Run(string src, out List<Diag> d, out Program p) { d = new List<Diag>(); p = Parser.Parse(src, d); return Compiler.Compile(p, t => t == "missing" ? 0 : 1, d); }
  static void Main() {
    List<Diag> d; Program p;
    // 1. the prototype, as a script: flag toggled by Up, when-edges run templates
    var o = Run("flag lights\non key Up:\n    toggle lights\nwhen lights:\n    run grey\nwhen not lights:\n    run color\n", out d, out p);
    Check(d.Count == 0, "toggle compiles: " + string.Join("; ", d));
    Check(o.States == 2 && o.Slots == 1, "toggle: 2 states 1 slot, got " + o.States + "/" + o.Slots);
    // starter bind(1) + init-when copies(color:1) + s0: copy grey + rebind(1) + s1: copy color + rebind(1) = 6
    Check(o.Events.Count == 6, "toggle event count 6, got " + o.Events.Count);
    Check(o.Events.Count(e => e.Type == "copy" && e.Template == "color" && (string)e.Props["eventTag"] == "sphx_init") == 1, "initial when runs at start");
    var rebind = o.Events.Where(e => e.Type == "SetInputEvent" && (string)e.Props["eventTag"] == "sphx_0_0").ToList();
    Check(rebind.Count == 1 && (string)rebind[0].Props["targetEventTag"] == "sphx_1_0", "state 0 Up rebinds to state 1 group");

    // 2. counter wrap + if/else + nohit + release
    o = Run("counter hits 0..3 wrap\nstate mode = calm, wild\non key Up nohit:\n    hits += 1\n    if hits == 3 and mode == calm:\n        mode = wild\n    else:\n        run flash\non key Down release:\n    mode = calm\n", out d, out p);
    Check(d.Count == 0, "counter compiles: " + string.Join("; ", d));
    Check(o.States == 8, "counter x mode reachable = 8, got " + o.States);
    Check(o.Events.Where(e => e.Type == "SetInputEvent").All(e => e.Props["target"] is string), "targets set");
    Check(o.Events.Any(e => e.Type == "SetInputEvent" && (bool)e.Props["ignoreInput"]), "nohit sets ignoreInput");
    Check(o.Events.Any(e => e.Type == "SetInputEvent" && (string)e.Props["state"] == "Up"), "release uses Up");
    var s = new[] { 3, 0 }; var runs = new List<string>(); Compiler.Exec(p, p.Handlers[0].Body, s, runs);
    Check(s[0] == 0 && s[1] == 0 && runs.SequenceEqual(new[]{"flash"}), "wrap 3->0, then hits==0 so else runs flash");
    s = new[] { 2, 0 }; runs.Clear(); Compiler.Exec(p, p.Handlers[0].Body, s, runs);
    Check(s[0] == 3 && s[1] == 1 && runs.Count == 0, "2->3 then mode wild");

    // 3. clamp without wrap, negative ranges
    o = Run("counter x -2..1\non key Left:\n    x -= 5\n", out d, out p);
    Check(d.Count == 0, "negative range compiles: " + string.Join("; ", d));
    s = new[] { 3 }; Compiler.Exec(p, p.Handlers[0].Body, s, new List<string>()); Check(s[0] == 0, "clamps at min");

    // 4. judgment handler: only run; SetConditionalEvents on the tile
    o = Run("on miss at 40:\n    run shake\non perfect at 40:\n    run glow\n", out d, out p);
    Check(d.Count == 0, "judgments compile");
    var ce = o.Events.Single(e => e.Type == "SetConditionalEvents");
    Check(ce.Floor == 40 && (string)ce.Props["missTag"] == "sphx_j40_miss" && (string)ce.Props["perfectTag"] == "sphx_j40_perfect", "conditional tags");
    Run("flag a\non miss at 3:\n    toggle a\n", out d, out p);
    Check(d.Any(x => x.Msg.Contains("judgment handler can only")), "judgment can't set");

    // 5. diagnostics
    Run("flag a\non key Up:\n    b = on\n", out d, out p); Check(d.Any(x => x.Msg.Contains("unknown variable `b`")), "unknown var");
    Run("state m = x\n", out d, out p); Check(d.Any(x => x.Msg.Contains("at least two")), "state needs 2");
    Run("flag a\non key Nope:\n    toggle a\n", out d, out p); Check(d.Any(x => x.Msg.Contains("unknown key")), "bad key");
    Run("state m = a, b\nwhen m > a:\n    run x\n", out d, out p); Check(d.Any(x => x.Msg.Contains("compares counters only")), "< on state");
    Run("flag a\non key Up:\n    run missing\n", out d, out p); Check(d.Any(x => x.Warning && x.Msg.Contains("no events are tagged")), "missing template warns");
    Run("flag a\non key Up:\n", out d, out p); Check(d.Any(x => x.Msg.Contains("empty block")), "empty block");
    Run("counter a 0..63 wrap\ncounter b 0..63\non key Up:\n    a += 1\n    if a == 0:\n        b += 1\n", out d, out p); Check(d.Any(x => x.Msg.Contains("reachable combinations")), "state cap");
    Run("flag a # comment\n\n# only comment\nhost 12\non key Up:\n\ttoggle a\n", out d, out p); Check(d.Count == 0 && p.Host == 12, "comments, tabs, host: " + string.Join("; ", d));
    Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " failed");
  }
}
