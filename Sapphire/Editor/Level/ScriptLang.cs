using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sapphire.Script
{
    /* Sapphire Script: a small language compiled into ORDINARY level events, so a level that uses
       it plays in the unmodded game.

       The only memory the game has at runtime is SetInputEvent's global key → events table, which
       a fired SetInputEvent rewrites. So every variable is FINITE (flag / named states / bounded
       counter), the program is compiled to a state machine over all of them, and each state's
       key reactions are tagged event groups on one host tile. Hit judgments (SetConditionalEvents)
       fire per tile and can't read that memory, so they may only `run` actions.

       Pure C#: no Unity or game types, so the parser and compiler can be tested outside the game.

         host 0                         # tile the machine lives on (default 0)
         flag lights                    # off / on
         state mode = calm, wild        # first name is the start value
         counter hits 0..3 wrap         # clamps at the ends unless `wrap`
         on key Up nohit:               # Up Down Left Right Action1 Action2 Confirm Any; `release`, `nohit`
             toggle lights
             hits += 1
             if hits == 3 and mode == calm:
                 mode = wild
             else:
                 run flash              # copies of every event tagged `flash`
         when lights:                   # runs when the condition BECOMES true
             run grey
         on miss at 40:                 # perfect earlyPerfect latePerfect veryEarly veryLate
             run shake                  #   tooEarly tooLate barely hit miss loss checkpoint */

    internal sealed class Diag
    {
        public int Line; public string Msg; public bool Warning;
        public override string ToString() => (Warning ? "warning" : "error") + " · line " + Line + ": " + Msg;
    }

    internal enum VarKind { Flag, State, Counter }

    internal sealed class VarDecl
    {
        public string Name; public VarKind Kind; public int Line;
        public List<string> Names = new List<string>();   // flag: off,on · state: its names
        public int Min, Max, Init; public bool Wrap;
        public int Count => Kind == VarKind.Counter ? Max - Min + 1 : Names.Count;
    }

    internal abstract class Cond { public abstract bool Eval(int[] s); }

    internal sealed class Cmp : Cond
    {
        public int Var; public string Op; public int Value;   // Value in the var's own index space
        public override bool Eval(int[] s)
        {
            int v = s[Var];
            switch (Op)
            {
                case "==": return v == Value;
                case "!=": return v != Value;
                case "<": return v < Value;
                case ">": return v > Value;
                case "<=": return v <= Value;
                default: return v >= Value;
            }
        }
    }

    internal sealed class And : Cond { public Cond A, B; public override bool Eval(int[] s) => A.Eval(s) && B.Eval(s); }
    internal sealed class Or : Cond { public Cond A, B; public override bool Eval(int[] s) => A.Eval(s) || B.Eval(s); }
    internal sealed class Not : Cond { public Cond A; public override bool Eval(int[] s) => !A.Eval(s); }

    internal abstract class Stmt { public int Line; }
    internal sealed class RunStmt : Stmt { public string Tag; }
    internal sealed class SetStmt : Stmt { public int Var; public string Op; public int Value; }   // "=", "+=", "-=", "toggle"
    internal sealed class IfStmt : Stmt { public Cond Cond; public List<Stmt> Then = new List<Stmt>(), Else = new List<Stmt>(); }

    internal sealed class Handler
    {
        public int Line; public bool IsKey;
        public string Key; public bool Release, NoHit;      // key handlers
        public string Judgment; public int Tile;            // judgment handlers
        public List<Stmt> Body = new List<Stmt>();
    }

    internal sealed class When { public int Line; public Cond Cond; public List<Stmt> Body = new List<Stmt>(); }

    internal sealed class Program
    {
        public int Host;
        public List<VarDecl> Vars = new List<VarDecl>();
        public List<Handler> Handlers = new List<Handler>();
        public List<When> Whens = new List<When>();
        public int VarIndex(string n) => Vars.FindIndex(v => v.Name == n);
    }

    internal static class Lang
    {
        internal static readonly string[] Keys = { "Any", "Action1", "Action2", "Confirm", "Up", "Down", "Left", "Right" };

        // Judgment word → SetConditionalEvents property.
        internal static readonly Dictionary<string, string> Judgments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "perfect", "perfectTag" }, { "earlyPerfect", "earlyPerfectTag" }, { "latePerfect", "latePerfectTag" },
            { "veryEarly", "veryEarlyTag" }, { "veryLate", "veryLateTag" }, { "tooEarly", "tooEarlyTag" },
            { "tooLate", "tooLateTag" }, { "barely", "barelyTag" }, { "hit", "hitTag" }, { "miss", "missTag" },
            { "loss", "lossTag" }, { "checkpoint", "onCheckpointTag" },
        };

        internal static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "host", "flag", "state", "counter", "wrap", "on", "key", "release", "nohit", "at", "when",
            "if", "else", "run", "toggle", "and", "or", "not",
        };
    }

    // ── parsing ─────────────────────────────────────────────────────────────────

    internal static class Parser
    {
        private sealed class Line { public int No, Indent; public List<string> Tok; public string Raw; }

        internal static Program Parse(string src, List<Diag> diags)
        {
            var lines = Split(src ?? "");
            var p = new Program();
            int i = 0;
            while (i < lines.Count)
            {
                var ln = lines[i];
                if (ln.Indent > 0) { Err(diags, ln.No, "unexpected indent"); i++; continue; }
                i = TopLevel(p, lines, i, diags);
            }
            return p;
        }

        // Comments stripped, tabs as 4 spaces, blank lines dropped.
        private static List<Line> Split(string src)
        {
            var outp = new List<Line>();
            var raw = src.Replace("\r", "").Split('\n');
            for (int n = 0; n < raw.Length; n++)
            {
                string s = raw[n];
                int hash = s.IndexOf('#');
                if (hash >= 0) s = s.Substring(0, hash);
                s = s.Replace("\t", "    ");
                if (s.Trim().Length == 0) continue;
                int ind = 0;
                while (ind < s.Length && s[ind] == ' ') ind++;
                outp.Add(new Line { No = n + 1, Indent = ind, Tok = Tokenize(s.Substring(ind)), Raw = s.Trim() });
            }
            return outp;
        }

        internal static List<string> Tokenize(string s)
        {
            var t = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (char.IsLetter(c) || c == '_')
                {
                    int st = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    t.Add(s.Substring(st, i - st));
                    continue;
                }
                // The language has no subtraction, so a '-' before a digit is always a sign.
                if (char.IsDigit(c) || (c == '-' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    int st = i++;
                    while (i < s.Length && char.IsDigit(s[i])) i++;
                    t.Add(s.Substring(st, i - st));
                    continue;
                }
                string two = i + 1 < s.Length ? s.Substring(i, 2) : "";
                if (two == "==" || two == "!=" || two == "<=" || two == ">=" || two == "+=" || two == "-=" || two == "..")
                { t.Add(two); i += 2; continue; }
                t.Add(c.ToString());
                i++;
            }
            return t;
        }

        private static int TopLevel(Program p, List<Line> lines, int i, List<Diag> d)
        {
            var ln = lines[i];
            var t = ln.Tok;
            string head = t[0];
            switch (head)
            {
                case "host":
                    int h;
                    if (t.Count == 2 && int.TryParse(t[1], out h) && h >= 0) p.Host = h;
                    else Err(d, ln.No, "write `host <tile number>`");
                    return i + 1;
                case "flag":
                case "state":
                case "counter":
                    Decl(p, ln, d);
                    return i + 1;
                case "on":
                {
                    var hd = new Handler { Line = ln.No };
                    if (!HandlerHead(p, hd, t, ln.No, d)) return Skip(lines, i);
                    int next;
                    hd.Body = Block(p, lines, i, d, out next);
                    p.Handlers.Add(hd);
                    return next;
                }
                case "when":
                {
                    if (t[t.Count - 1] != ":") { Err(d, ln.No, "`when <condition>:` needs a colon"); return Skip(lines, i); }
                    int k = 1;
                    var c = ParseCond(p, t.GetRange(0, t.Count - 1), ref k, ln.No, d);
                    int next;
                    var w = new When { Line = ln.No, Cond = c, Body = Block(p, lines, i, d, out next) };
                    if (c != null) p.Whens.Add(w);
                    return next;
                }
                default:
                    Err(d, ln.No, "expected host, flag, state, counter, on or when — found `" + head + "`");
                    return Skip(lines, i);
            }
        }

        private static int Skip(List<Line> lines, int i)
        {
            int ind = lines[i].Indent;
            i++;
            while (i < lines.Count && lines[i].Indent > ind) i++;
            return i;
        }

        private static void Decl(Program p, Line ln, List<Diag> d)
        {
            var t = ln.Tok;
            if (t.Count < 2 || !Ident(t[1])) { Err(d, ln.No, "`" + t[0] + "` needs a name"); return; }
            if (p.VarIndex(t[1]) >= 0) { Err(d, ln.No, "`" + t[1] + "` is already declared"); return; }
            var v = new VarDecl { Name = t[1], Line = ln.No };
            if (t[0] == "flag")
            {
                v.Kind = VarKind.Flag; v.Names.Add("off"); v.Names.Add("on");
                if (t.Count == 4 && t[2] == "=" && (t[3] == "on" || t[3] == "true")) v.Init = 1;
                else if (!(t.Count == 2 || (t.Count == 4 && t[2] == "=" && (t[3] == "off" || t[3] == "false"))))
                { Err(d, ln.No, "write `flag name` or `flag name = on`"); return; }
            }
            else if (t[0] == "state")
            {
                v.Kind = VarKind.State;
                if (t.Count < 4 || t[2] != "=") { Err(d, ln.No, "write `state name = first, second, …`"); return; }
                for (int k = 3; k < t.Count; k++)
                {
                    if (t[k] == ",") continue;
                    if (!Ident(t[k]) || v.Names.Contains(t[k])) { Err(d, ln.No, "bad or repeated state name `" + t[k] + "`"); return; }
                    v.Names.Add(t[k]);
                }
                if (v.Names.Count < 2) { Err(d, ln.No, "a state needs at least two names"); return; }
            }
            else
            {
                v.Kind = VarKind.Counter;
                int a, b;
                if (t.Count < 5 || t[3] != ".." || !int.TryParse(t[2], out a) || !int.TryParse(t[4], out b) || b <= a)
                { Err(d, ln.No, "write `counter name 0..3` (optionally `wrap`, `= start`)"); return; }
                v.Min = a; v.Max = b; v.Init = 0;
                for (int k = 5; k < t.Count; k++)
                {
                    if (t[k] == "wrap") v.Wrap = true;
                    else if (t[k] == "=" && k + 1 < t.Count && int.TryParse(t[k + 1], out int s0) && s0 >= a && s0 <= b) { v.Init = s0 - a; k++; }
                    else { Err(d, ln.No, "unexpected `" + t[k] + "`"); return; }
                }
                if (b - a > 63) { Err(d, ln.No, "a counter can span at most 64 values"); return; }
            }
            p.Vars.Add(v);
        }

        private static bool HandlerHead(Program p, Handler h, List<string> t, int no, List<Diag> d)
        {
            if (t[t.Count - 1] != ":") { Err(d, no, "a handler line ends with a colon"); return false; }
            if (t.Count >= 3 && t[1] == "key")
            {
                h.IsKey = true;
                h.Key = Lang.Keys.FirstOrDefault(k => string.Equals(k, t[2], StringComparison.OrdinalIgnoreCase));
                if (h.Key == null) { Err(d, no, "unknown key `" + t[2] + "` — use " + string.Join(", ", Lang.Keys)); return false; }
                for (int k = 3; k < t.Count - 1; k++)
                {
                    if (t[k] == "release") h.Release = true;
                    else if (t[k] == "nohit") h.NoHit = true;
                    else { Err(d, no, "unexpected `" + t[k] + "` (expected release or nohit)"); return false; }
                }
                return true;
            }
            string prop;
            if (t.Count == 5 && Lang.Judgments.TryGetValue(t[1], out prop) && t[2] == "at")
            {
                int tile;
                if (!int.TryParse(t[3], out tile) || tile < 0) { Err(d, no, "`at` needs a tile number"); return false; }
                h.Judgment = Lang.Judgments.Keys.First(k => string.Equals(k, t[1], StringComparison.OrdinalIgnoreCase));
                h.Tile = tile;
                return true;
            }
            Err(d, no, "write `on key <key>:` or `on <judgment> at <tile>:`");
            return false;
        }

        // The indented lines under lines[i].
        private static List<Stmt> Block(Program p, List<Line> lines, int i, List<Diag> d, out int next)
        {
            var body = new List<Stmt>();
            int ind = lines[i].Indent;
            next = i + 1;
            if (next >= lines.Count || lines[next].Indent <= ind) { Err(d, lines[i].No, "empty block"); return body; }
            int blockInd = lines[next].Indent;
            while (next < lines.Count && lines[next].Indent > ind)
            {
                if (lines[next].Indent != blockInd) { Err(d, lines[next].No, "inconsistent indent"); next++; continue; }
                next = Statement(p, lines, next, body, d);
            }
            return body;
        }

        private static int Statement(Program p, List<Line> lines, int i, List<Stmt> body, List<Diag> d)
        {
            var ln = lines[i];
            var t = ln.Tok;
            if (t[0] == "run")
            {
                string tag = ln.Raw.Substring(3).Trim();
                if (tag.Length == 0 || tag.Contains(" ")) Err(d, ln.No, "`run` takes one event tag");
                else body.Add(new RunStmt { Line = ln.No, Tag = tag });
                return i + 1;
            }
            if (t[0] == "toggle")
            {
                int vi = t.Count == 2 ? p.VarIndex(t[1]) : -1;
                if (vi < 0 || p.Vars[vi].Kind != VarKind.Flag) Err(d, ln.No, "`toggle` takes a flag");
                else body.Add(new SetStmt { Line = ln.No, Var = vi, Op = "toggle" });
                return i + 1;
            }
            if (t[0] == "if")
            {
                if (t[t.Count - 1] != ":") { Err(d, ln.No, "`if <condition>:` needs a colon"); return Skip(lines, i); }
                int k = 1;
                var st = new IfStmt { Line = ln.No, Cond = ParseCond(p, t.GetRange(0, t.Count - 1), ref k, ln.No, d) };
                int next;
                st.Then = Block(p, lines, i, d, out next);
                if (next < lines.Count && lines[next].Indent == ln.Indent && lines[next].Tok[0] == "else")
                {
                    var et = lines[next].Tok;
                    if (et.Count != 2 || et[1] != ":") Err(d, lines[next].No, "write `else:`");
                    st.Else = Block(p, lines, next, d, out next);
                }
                if (st.Cond != null) body.Add(st);
                return next;
            }
            if (t[0] == "else") { Err(d, ln.No, "`else` without `if`"); return Skip(lines, i); }
            if (t.Count == 3 && (t[1] == "=" || t[1] == "+=" || t[1] == "-="))
            {
                int vi = p.VarIndex(t[0]);
                if (vi < 0) { Err(d, ln.No, "unknown variable `" + t[0] + "`"); return i + 1; }
                var v = p.Vars[vi];
                if (t[1] != "=")
                {
                    int n;
                    if (v.Kind != VarKind.Counter || !int.TryParse(t[2], out n)) Err(d, ln.No, "`+=` / `-=` work on counters, with a whole number");
                    else body.Add(new SetStmt { Line = ln.No, Var = vi, Op = t[1], Value = n });
                    return i + 1;
                }
                int val;
                if (Value(v, t[2], out val, ln.No, d)) body.Add(new SetStmt { Line = ln.No, Var = vi, Op = "=", Value = val });
                return i + 1;
            }
            Err(d, ln.No, "expected run, toggle, if, or `name = value`");
            return Skip(lines, i);
        }

        // A value in the variable's index space: flag 0/1, state position, counter offset from min.
        private static bool Value(VarDecl v, string tok, out int val, int no, List<Diag> d)
        {
            val = 0;
            if (v.Kind == VarKind.Flag)
            {
                if (tok == "on" || tok == "true") { val = 1; return true; }
                if (tok == "off" || tok == "false") { val = 0; return true; }
                Err(d, no, "`" + v.Name + "` is a flag: use on or off"); return false;
            }
            if (v.Kind == VarKind.State)
            {
                val = v.Names.IndexOf(tok);
                if (val >= 0) return true;
                Err(d, no, "`" + v.Name + "` is one of: " + string.Join(", ", v.Names)); return false;
            }
            int n;
            if (int.TryParse(tok, out n) && n >= v.Min && n <= v.Max) { val = n - v.Min; return true; }
            Err(d, no, "`" + v.Name + "` runs " + v.Min + ".." + v.Max); return false;
        }

        // or := and {or and} · and := unary {and unary} · unary := not unary | ( or ) | atom
        private static Cond ParseCond(Program p, List<string> t, ref int k, int no, List<Diag> d)
        {
            var c = ParseAnd(p, t, ref k, no, d);
            while (c != null && k < t.Count && t[k] == "or") { k++; var b = ParseAnd(p, t, ref k, no, d); c = b == null ? null : new Or { A = c, B = b }; }
            if (c != null && k < t.Count && t[k] != ")") { Err(d, no, "unexpected `" + t[k] + "` in condition"); return null; }
            return c;
        }

        private static Cond ParseAnd(Program p, List<string> t, ref int k, int no, List<Diag> d)
        {
            var c = ParseUnary(p, t, ref k, no, d);
            while (c != null && k < t.Count && t[k] == "and") { k++; var b = ParseUnary(p, t, ref k, no, d); c = b == null ? null : new And { A = c, B = b }; }
            return c;
        }

        private static Cond ParseUnary(Program p, List<string> t, ref int k, int no, List<Diag> d)
        {
            if (k >= t.Count) { Err(d, no, "condition ends too early"); return null; }
            if (t[k] == "not") { k++; var a = ParseUnary(p, t, ref k, no, d); return a == null ? null : new Not { A = a }; }
            if (t[k] == "(")
            {
                k++;
                var c = ParseCond(p, t, ref k, no, d);
                if (c == null) return null;
                if (k >= t.Count || t[k] != ")") { Err(d, no, "missing `)`"); return null; }
                k++;
                return c;
            }
            int vi = p.VarIndex(t[k]);
            if (vi < 0) { Err(d, no, "unknown variable `" + t[k] + "`"); return null; }
            var v = p.Vars[vi];
            k++;
            string op = k < t.Count ? t[k] : null;
            if (op == "==" || op == "!=" || op == "<" || op == ">" || op == "<=" || op == ">=")
            {
                k++;
                if (k >= t.Count) { Err(d, no, "comparison needs a value"); return null; }
                if (op != "==" && op != "!=" && v.Kind != VarKind.Counter) { Err(d, no, "`" + op + "` compares counters only"); return null; }
                int val;
                if (!Value(v, t[k], out val, no, d)) return null;
                k++;
                return new Cmp { Var = vi, Op = op, Value = val };
            }
            if (v.Kind != VarKind.Flag) { Err(d, no, "`" + v.Name + "` needs a comparison (==, !=" + (v.Kind == VarKind.Counter ? ", <, >" : "") + ")"); return null; }
            return new Cmp { Var = vi, Op = "==", Value = 1 };
        }

        private static bool Ident(string s) => s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(ch => char.IsLetterOrDigit(ch) || ch == '_') && !Lang.Keywords.Contains(s);

        private static void Err(List<Diag> d, int line, string msg) => d.Add(new Diag { Line = line, Msg = msg });
    }

    // ── compiling ───────────────────────────────────────────────────────────────

    // One event to create. Type "copy" clones every template tagged Template onto Floor.
    internal sealed class EvSpec
    {
        public int Floor; public string Type; public string Template;
        public readonly Dictionary<string, object> Props = new Dictionary<string, object>();
    }

    internal sealed class Output
    {
        public readonly List<EvSpec> Events = new List<EvSpec>();
        public int States, Slots;
    }

    internal static class Compiler
    {
        internal const string Prefix = "sphx";
        internal const int MaxStates = 256, MaxEvents = 6000;

        private sealed class Slot { public string Key; public bool Release, NoHit; public List<Stmt> Body = new List<Stmt>(); }

        /* `copiesOf(tag)` says how many template events carry that tag (for warnings; the actual
           cloning happens when the specs are applied). */
        internal static Output Compile(Program p, Func<string, int> copiesOf, List<Diag> d)
        {
            var o = new Output();
            if (d.Any(x => !x.Warning)) return o;
            foreach (var h in p.Handlers)
            {
                if (h.IsKey) continue;
                if (HasSet(h.Body)) d.Add(new Diag { Line = h.Line, Msg = "a judgment handler can only `run` — hit judgments fire per tile and can't change variables in the unmodded game" });
            }
            foreach (var w in p.Whens)
                if (HasSet(w.Body)) d.Add(new Diag { Line = w.Line, Msg = "a `when` block can only `run`" });
            if (d.Any(x => !x.Warning)) return o;

            // Key slots: one per key + press/release, handlers for the same slot run in order.
            var slots = new List<Slot>();
            foreach (var h in p.Handlers.Where(x => x.IsKey))
            {
                var s = slots.FirstOrDefault(x => x.Key == h.Key && x.Release == h.Release);
                if (s == null) slots.Add(s = new Slot { Key = h.Key, Release = h.Release });
                s.NoHit |= h.NoHit;
                s.Body.AddRange(h.Body);
            }
            o.Slots = slots.Count;

            // Reachable states, breadth first from the start values.
            var init = p.Vars.Select(v => v.Init).ToArray();
            var ids = new Dictionary<string, int> { { Key(init), 0 } };
            var states = new List<int[]> { init };
            var trans = new List<Tuple<int[], List<string>>[]>();
            for (int si = 0; si < states.Count; si++)
            {
                var row = new Tuple<int[], List<string>>[slots.Count];
                for (int q = 0; q < slots.Count; q++)
                {
                    var runs = new List<string>();
                    var next = (int[])states[si].Clone();
                    Exec(p, slots[q].Body, next, runs);
                    foreach (var w in p.Whens)
                        if (!w.Cond.Eval(states[si]) && w.Cond.Eval(next)) Runs(w.Body, runs);
                    row[q] = Tuple.Create(next, runs);
                    string k = Key(next);
                    if (!ids.ContainsKey(k))
                    {
                        if (states.Count >= MaxStates)
                        { d.Add(new Diag { Line = 1, Msg = "more than " + MaxStates + " reachable combinations of values — use fewer or smaller variables" }); return o; }
                        ids[k] = states.Count;
                        states.Add(next);
                    }
                }
                trans.Add(row);
            }
            o.States = states.Count;

            int host = p.Host;
            // Starter: binds every slot to the start state's group when the planet reaches the host.
            for (int q = 0; q < slots.Count; q++) o.Events.Add(Input(host, Prefix + "_init", slots[q], Group(0, q)));
            foreach (var w in p.Whens)
                if (w.Cond.Eval(init)) foreach (var tag in RunTags(w.Body)) o.Events.Add(Copy(host, tag, Prefix + "_init"));

            for (int si = 0; si < states.Count; si++)
                for (int q = 0; q < slots.Count; q++)
                {
                    string g = Group(si, q);
                    var tr = trans[si][q];
                    foreach (var tag in tr.Item2) o.Events.Add(Copy(host, tag, g));
                    int ni = ids[Key(tr.Item1)];
                    if (ni == si) continue;   // same state: every binding stays as it is
                    for (int q2 = 0; q2 < slots.Count; q2++) o.Events.Add(Input(host, g, slots[q2], Group(ni, q2)));
                }

            // Judgments: a SetConditionalEvents on the tile, its runs tagged per judgment.
            foreach (var byTile in p.Handlers.Where(x => !x.IsKey).GroupBy(x => x.Tile))
            {
                var cond = new EvSpec { Floor = byTile.Key, Type = "SetConditionalEvents" };
                foreach (var byJ in byTile.GroupBy(x => x.Judgment))
                {
                    string g = Prefix + "_j" + byTile.Key + "_" + byJ.Key;
                    cond.Props[Lang.Judgments[byJ.Key]] = g;
                    foreach (var h in byJ) foreach (var tag in RunTags(h.Body)) o.Events.Add(Copy(byTile.Key, tag, g));
                }
                o.Events.Add(cond);
            }

            foreach (var tag in AllRunTags(p).Distinct())
                if (copiesOf != null && copiesOf(tag) == 0)
                    d.Add(new Diag { Line = FirstRunLine(p, tag), Msg = "no events are tagged `" + tag + "` — it will do nothing", Warning = true });
            if (o.Events.Count > MaxEvents)
                d.Add(new Diag { Line = 1, Msg = o.Events.Count + " events is too many (limit " + MaxEvents + ") — use fewer or smaller variables" });
            return o;
        }

        // Runs `body` against state `s` in place, collecting `run` tags in order.
        internal static void Exec(Program p, List<Stmt> body, int[] s, List<string> runs)
        {
            foreach (var st in body)
            {
                if (st is RunStmt) runs.Add(((RunStmt)st).Tag);
                else if (st is IfStmt)
                {
                    var i = (IfStmt)st;
                    Exec(p, i.Cond.Eval(s) ? i.Then : i.Else, s, runs);
                }
                else
                {
                    var a = (SetStmt)st;
                    var v = p.Vars[a.Var];
                    int n = v.Count;
                    switch (a.Op)
                    {
                        case "toggle": s[a.Var] = 1 - s[a.Var]; break;
                        case "=": s[a.Var] = a.Value; break;
                        default:
                            int x = s[a.Var] + (a.Op == "+=" ? a.Value : -a.Value);
                            s[a.Var] = v.Wrap ? ((x % n) + n) % n : Math.Max(0, Math.Min(n - 1, x));
                            break;
                    }
                }
            }
        }

        private static bool HasSet(List<Stmt> body) =>
            body.Any(s => s is SetStmt || (s is IfStmt && (HasSet(((IfStmt)s).Then) || HasSet(((IfStmt)s).Else))));

        private static void Runs(List<Stmt> body, List<string> runs) { foreach (var s in body) if (s is RunStmt) runs.Add(((RunStmt)s).Tag); }
        private static IEnumerable<string> RunTags(List<Stmt> body) => body.OfType<RunStmt>().Select(r => r.Tag);

        private static IEnumerable<string> AllRunTags(Program p)
        {
            var all = new List<string>();
            Action<List<Stmt>> walk = null;
            walk = b => { foreach (var s in b) { if (s is RunStmt) all.Add(((RunStmt)s).Tag); else if (s is IfStmt) { walk(((IfStmt)s).Then); walk(((IfStmt)s).Else); } } };
            foreach (var h in p.Handlers) walk(h.Body);
            foreach (var w in p.Whens) walk(w.Body);
            return all;
        }

        private static int FirstRunLine(Program p, string tag)
        {
            int line = 1;
            Func<List<Stmt>, bool> find = null;
            find = b =>
            {
                foreach (var s in b)
                {
                    if (s is RunStmt && ((RunStmt)s).Tag == tag) { line = s.Line; return true; }
                    if (s is IfStmt && (find(((IfStmt)s).Then) || find(((IfStmt)s).Else))) return true;
                }
                return false;
            };
            foreach (var h in p.Handlers) if (find(h.Body)) return line;
            foreach (var w in p.Whens) if (find(w.Body)) return line;
            return line;
        }

        private static string Key(int[] s) => string.Join(",", s);
        private static string Group(int state, int slot) => Prefix + "_" + state + "_" + slot;

        private static EvSpec Input(int floor, string ownTag, Slot s, string target)
        {
            var e = new EvSpec { Floor = floor, Type = "SetInputEvent" };
            e.Props["target"] = s.Key;
            e.Props["state"] = s.Release ? "Up" : "Down";
            e.Props["targetEventTag"] = target;
            e.Props["ignoreInput"] = s.NoHit;
            e.Props["eventTag"] = ownTag;
            return e;
        }

        private static EvSpec Copy(int floor, string template, string tag)
        {
            var e = new EvSpec { Floor = floor, Type = "copy", Template = template };
            e.Props["eventTag"] = tag;
            return e;
        }
    }
}
