using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace Sapphire
{
    /* Level variables: named values, and formulas like `$beat*2` in any number field.

       The unmodded game never sees a variable. A formula is evaluated when it is typed or when a
       variable changes, and the RESULT is what the event holds — so the saved level is plain
       numbers and plays anywhere. The formulas themselves ride along in a "sapphire" section of
       the .adofai, which the game's LevelData.Decode ignores (it reads only the keys it knows).
       Re-saving in the unmodded editor keeps the numbers and drops the formulas.

       Bindings are keyed by the LevelEvent OBJECT, and LevelEvent.Copy carries them — that one
       postfix is what makes undo/redo (LevelData.Copy → LevelEvent.Copy), copy/paste and the tray
       keep a field's formula. The variable table hangs off LevelData the same way, so undoing a
       variable edit restores the old table along with the old values. */
    internal static class LevelVars
    {
        internal class Var { public string Name = ""; public string Expr = "0"; }
        internal class Table { public readonly List<Var> Vars = new List<Var>(); }

        internal const string Section = "sapphire";
        private static readonly Regex NameRx = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        private static readonly ConditionalWeakTable<ADOFAI.LevelData, Table> _tables =
            new ConditionalWeakTable<ADOFAI.LevelData, Table>();
        private static readonly ConditionalWeakTable<ADOFAI.LevelEvent, Dictionary<string, string>> _binds =
            new ConditionalWeakTable<ADOFAI.LevelEvent, Dictionary<string, string>>();

        internal static bool IsFormula(string raw) => raw != null && raw.IndexOf('$') >= 0;
        internal static bool ValidName(string n) => n != null && NameRx.IsMatch(n);

        internal static Table TableOf(ADOFAI.LevelData ld) => ld == null ? null : _tables.GetValue(ld, _ => new Table());

        internal static Table Current
        {
            get { try { var ed = scnEditor.instance; return ed != null ? TableOf(ed.levelData) : null; } catch { return null; } }
        }

        // key is a property name, or "name.x" / "name.y" for one component of a Vector2.
        internal static string FormulaOf(ADOFAI.LevelEvent e, string key)
        {
            Dictionary<string, string> d;
            string f;
            return e != null && _binds.TryGetValue(e, out d) && d.TryGetValue(key, out f) ? f : null;
        }

        internal static void SetFormula(ADOFAI.LevelEvent e, string key, string formula)
        {
            if (e == null || key == null) return;
            Dictionary<string, string> d;
            if (!_binds.TryGetValue(e, out d))
            {
                if (formula == null) return;
                d = new Dictionary<string, string>();
                _binds.Add(e, d);
            }
            if (formula == null) d.Remove(key); else d[key] = formula;
        }

        // Every variable's value, top to bottom: a variable can use the ones above it.
        internal static Dictionary<string, double> Values(Table t, List<string> failed = null)
        {
            var vals = new Dictionary<string, double>();
            if (t == null) return vals;
            foreach (var v in t.Vars)
            {
                double d;
                if (ValidName(v.Name) && !vals.ContainsKey(v.Name) && Eval(v.Expr, vals, out d)) vals[v.Name] = d;
                else if (failed != null) failed.Add(v.Name);
            }
            return vals;
        }

        internal static bool Eval(string formula, Dictionary<string, double> vals, out double d)
        {
            return ExprEval.TryEval(formula, n => { double x; return vals.TryGetValue(n, out x) ? x : (double?)null; }, out d);
        }

        internal static bool Eval(string formula, out double d) => Eval(formula, Values(Current), out d);

        /* A number typed like the value already there. An int field given a fraction keeps it as a
           float — the same rule EventRows' plain typing follows, so a formula and the number it
           produced can never disagree about the stored type. */
        internal static object Coerce(double d, object like)
        {
            if (like is float) return (float)d;
            if (like is double) return d;
            if (like is int || like is long)
            {
                if (d != Math.Floor(d)) return (float)d;
                return like is long ? (object)(long)d : (object)(int)d;
            }
            return null;
        }

        internal static bool Write(ADOFAI.LevelEvent e, string key, double d)
        {
            int dot = key.LastIndexOf('.');
            string k = dot > 0 ? key.Substring(0, dot) : key;
            object cur;
            try { cur = e[k]; } catch { return false; }
            if (dot > 0)
            {
                if (!(cur is Vector2)) return false;
                var v = (Vector2)cur;
                if (key.EndsWith(".x")) v.x = (float)d; else v.y = (float)d;
                e[k] = v;
                return true;
            }
            var nv = Coerce(d, cur);
            if (nv == null) return false;
            e[k] = nv;
            return true;
        }

        private static IEnumerable<ADOFAI.LevelEvent> SettingsEvents(ADOFAI.LevelData ld)
        {
            yield return ld.songSettings; yield return ld.levelSettings; yield return ld.trackSettings;
            yield return ld.backgroundSettings; yield return ld.cameraSettings; yield return ld.miscSettings;
            yield return ld.eventSettings; yield return ld.decorationSettings;
        }

        private static IEnumerable<ADOFAI.LevelEvent> AllEvents(ADOFAI.LevelData ld)
        {
            foreach (var e in ld.levelEvents) yield return e;
            foreach (var e in ld.decorations) yield return e;
            foreach (var e in SettingsEvents(ld)) if (e != null) yield return e;
        }

        /* Re-evaluate every formula in the level and write the results — the caller owns the
           SaveStateScope. `failed` counts formulas that no longer evaluate (a deleted or renamed
           variable); their fields keep the last value they had. */
        internal static int Apply(scnEditor ed, out int failed)
        {
            failed = 0;
            int n = 0;
            if (ed == null) return 0;
            var ld = ed.levelData;
            var vals = Values(TableOf(ld));
            foreach (var e in AllEvents(ld))
            {
                Dictionary<string, string> d;
                if (e == null || !_binds.TryGetValue(e, out d) || d.Count == 0) continue;
                foreach (var kv in d)
                {
                    double x;
                    if (Eval(kv.Value, vals, out x) && Write(e, kv.Key, x)) n++; else failed++;
                }
            }
            if (n > 0)
            {
                try { ed.ApplyEventsToFloors(); } catch { }
                try { ed.RemakePath(true, true); } catch { }
                try { ed.UpdateDecorationObjects(); } catch { }
            }
            return n;
        }

        // How many fields in the level use each variable — for the panel's usage count.
        internal static Dictionary<string, int> Usage(ADOFAI.LevelData ld)
        {
            var use = new Dictionary<string, int>();
            if (ld == null) return use;
            foreach (var e in AllEvents(ld))
            {
                Dictionary<string, string> d;
                if (e == null || !_binds.TryGetValue(e, out d)) continue;
                foreach (var f in d.Values)
                    foreach (Match m in RefRx.Matches(f))
                    {
                        int c; use.TryGetValue(m.Groups[1].Value, out c); use[m.Groups[1].Value] = c + 1;
                    }
            }
            return use;
        }

        private static readonly Regex RefRx = new Regex(@"\$([A-Za-z_][A-Za-z0-9_]*)");

        // Renaming a variable rewrites every formula and variable that used the old name.
        internal static void RenameRefs(ADOFAI.LevelData ld, string from, string to)
        {
            if (ld == null || from == to) return;
            foreach (var e in AllEvents(ld))
            {
                Dictionary<string, string> d;
                if (e == null || !_binds.TryGetValue(e, out d)) continue;
                foreach (var k in d.Keys.ToList()) d[k] = RenameIn(d[k], from, to);
            }
            foreach (var v in TableOf(ld).Vars) v.Expr = RenameIn(v.Expr, from, to);
        }

        // `$a` → `$x` but never `$ab`. "$$" is a literal $ in a replacement pattern.
        internal static string RenameIn(string s, string from, string to) =>
            Regex.Replace(s ?? "", @"\$" + Regex.Escape(from) + @"(?![A-Za-z0-9_])", "$$" + to);

        internal static bool SelfCheck()
        {
            var t = new Table();
            t.Vars.Add(new Var { Name = "a", Expr = "2" });
            t.Vars.Add(new Var { Name = "b", Expr = "$a*3" });
            t.Vars.Add(new Var { Name = "c", Expr = "$d" });   // later / unknown: fails, not 0
            t.Vars.Add(new Var { Name = "d", Expr = "1" });
            var vals = Values(t);
            double x;
            bool ok = vals.ContainsKey("b") && vals["b"] == 6.0 && !vals.ContainsKey("c")
                && Eval("$b/4+1", vals, out x) && x == 2.5 && !Eval("$zz", vals, out x)
                && RenameIn("$ab+$a*($a)", "a", "x") == "$ab+$x*($x)"
                && Coerce(2.0, 5) is int && Coerce(2.5, 5) is float && Coerce(2.0, 5L) is long;
            SapphireLog.Log("LevelVars.SelfCheck: " + (ok ? "PASS" : "FAIL"));
            return ok;
        }

        // ── file section ─────────────────────────────────────────────────────────

        private static bool Active(ADOFAI.LevelEvent e)
        {
            try { return e != null && e.info != null && e.info.isActive; } catch { return e != null; }
        }

        /* A formula's event is named by (floor, type, n-th such event on that floor) in the order
           the game WRITES events — OrderBy(floor), a stable sort, skipping inactive ones. Loading
           reads them back in file order, so the same count finds the same event even if the game
           skipped one it couldn't decode. */
        private static Dictionary<string, object> EncodeSection(ADOFAI.LevelData ld)
        {
            Table t;
            var vars = new List<object>();
            if (_tables.TryGetValue(ld, out t))
                foreach (var v in t.Vars)
                    vars.Add(new Dictionary<string, object> { { "name", v.Name }, { "expr", v.Expr } });
            var forms = new List<object>();
            EncodeList(ld.levelEvents.OrderBy(e => e.floor).Where(Active), "action", forms);
            EncodeList(ld.decorations.Where(Active), "decoration", forms);
            EncodeList(SettingsEvents(ld).Where(e => e != null), "settings", forms);
            if (vars.Count == 0 && forms.Count == 0) return null;
            return new Dictionary<string, object> { { "version", 1 }, { "variables", vars }, { "formulas", forms } };
        }

        private static void EncodeList(IEnumerable<ADOFAI.LevelEvent> list, string src, List<object> outp)
        {
            var nth = new Dictionary<string, int>();
            foreach (var e in list)
            {
                string id = e.floor + ":" + e.eventType;
                int n; nth.TryGetValue(id, out n); nth[id] = n + 1;
                Dictionary<string, string> d;
                if (!_binds.TryGetValue(e, out d)) continue;
                foreach (var kv in d)
                    outp.Add(new Dictionary<string, object>
                    {
                        { "source", src }, { "floor", e.floor }, { "eventType", e.eventType.ToString() },
                        { "n", n }, { "key", kv.Key }, { "formula", kv.Value },
                    });
            }
        }

        private static void DecodeSection(ADOFAI.LevelData ld, Dictionary<string, object> root)
        {
            object o;
            if (root == null || !root.TryGetValue(Section, out o)) return;
            var sec = o as Dictionary<string, object>;
            if (sec == null) return;
            var t = TableOf(ld);
            t.Vars.Clear();
            if (sec.TryGetValue("variables", out o) && o is List<object>)
                foreach (var item in (List<object>)o)
                {
                    var vd = item as Dictionary<string, object>;
                    if (vd == null) continue;
                    object n, x;
                    vd.TryGetValue("name", out n); vd.TryGetValue("expr", out x);
                    t.Vars.Add(new Var { Name = n as string ?? "", Expr = x as string ?? "0" });
                }
            if (!sec.TryGetValue("formulas", out o) || !(o is List<object>)) return;
            var actions = Index(ld.levelEvents.Where(Active));
            var decos = Index(ld.decorations.Where(Active));
            var settings = Index(SettingsEvents(ld).Where(e => e != null));
            int lost = 0;
            foreach (var item in (List<object>)o)
            {
                var fd = item as Dictionary<string, object>;
                if (fd == null) continue;
                object src, floor, type, n, key, formula;
                fd.TryGetValue("source", out src); fd.TryGetValue("floor", out floor); fd.TryGetValue("eventType", out type);
                fd.TryGetValue("n", out n); fd.TryGetValue("key", out key); fd.TryGetValue("formula", out formula);
                var index = (src as string) == "decoration" ? decos : (src as string) == "settings" ? settings : actions;
                List<ADOFAI.LevelEvent> hits;
                int ni = n != null ? Convert.ToInt32(n) : 0;
                string id = (floor != null ? Convert.ToInt32(floor) : 0) + ":" + type;
                if (index.TryGetValue(id, out hits) && ni < hits.Count && key is string && formula is string)
                    SetFormula(hits[ni], (string)key, (string)formula);
                else lost++;
            }
            if (lost > 0) SapphireLog.Log("LevelVars: " + lost + " formula(s) had no matching event on load");
        }

        private static Dictionary<string, List<ADOFAI.LevelEvent>> Index(IEnumerable<ADOFAI.LevelEvent> list)
        {
            var map = new Dictionary<string, List<ADOFAI.LevelEvent>>();
            foreach (var e in list)
            {
                string id = e.floor + ":" + e.eventType;
                List<ADOFAI.LevelEvent> l;
                if (!map.TryGetValue(id, out l)) map[id] = l = new List<ADOFAI.LevelEvent>();
                l.Add(e);
            }
            return map;
        }

        // ── hooks ────────────────────────────────────────────────────────────────

        [HarmonyPatch(typeof(ADOFAI.LevelEvent), "Copy")]
        private static class EventCopyPatch
        {
            private static void Postfix(ADOFAI.LevelEvent __instance, ADOFAI.LevelEvent __result)
            {
                try
                {
                    Dictionary<string, string> d;
                    if (__result == null || !_binds.TryGetValue(__instance, out d) || d.Count == 0) return;
                    _binds.Remove(__result);
                    _binds.Add(__result, new Dictionary<string, string>(d));
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(ADOFAI.LevelData), "Copy")]
        private static class DataCopyPatch
        {
            private static void Postfix(ADOFAI.LevelData __instance, ADOFAI.LevelData __result)
            {
                try
                {
                    Table t;
                    if (__result == null || !_tables.TryGetValue(__instance, out t)) return;
                    var c = TableOf(__result);
                    c.Vars.Clear();
                    foreach (var v in t.Vars) c.Vars.Add(new Var { Name = v.Name, Expr = v.Expr });
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(ADOFAI.LevelData), "EncodeToDictionary")]
        private static class EncodePatch
        {
            private static void Postfix(ADOFAI.LevelData __instance, Dictionary<string, object> __result)
            {
                try
                {
                    if (__result == null) return;
                    var sec = EncodeSection(__instance);
                    if (sec != null) __result[Section] = sec; else __result.Remove(Section);
                }
                catch (Exception ex) { SapphireLog.Log("LevelVars: save failed: " + ex.Message); }
            }
        }

        [HarmonyPatch(typeof(ADOFAI.LevelData), "Decode")]
        private static class DecodePatch
        {
            private static void Postfix(ADOFAI.LevelData __instance, Dictionary<string, object> dict)
            {
                try { DecodeSection(__instance, dict); }
                catch (Exception ex) { SapphireLog.Log("LevelVars: load failed: " + ex.Message); }
            }
        }
    }
}
