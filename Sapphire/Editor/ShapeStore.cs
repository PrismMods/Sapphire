using System;
using System.Collections.Generic;

namespace Sapphire
{
    /* Runtime shape model + store. Built-in shapes (ShapeLibrary.All) live under the "Built-in"
       category; user shapes live under named categories and persist in Settings.CustomShapes /
       ShapeCategories. A ShapeEntry has one or more key-count variants (relative charters + a
       per-tile twirl flag + a default repeat n). Ids are stable per session so the panel can key
       its per-variant overrides (edited angles, n, rotate) to a shape across rebuilds. */
    internal class ShapeVariant
    {
        public int K;
        public double[] Angles;
        public bool[] Twirls;
        public int N;
    }

    internal class ShapeEntry
    {
        public string Id;
        public string Name;
        public string Category;
        public double Base;
        public bool BuiltIn;
        public List<ShapeVariant> Variants = new List<ShapeVariant>();
    }

    internal static class ShapeStore
    {
        internal const string BuiltInCat = "Built-in";
        private static List<string> _categories;
        private static List<ShapeEntry> _shapes;
        private static int _customSeq;

        internal static List<string> Categories { get { Ensure(); return _categories; } }

        internal static List<ShapeEntry> InCategory(string cat)
        {
            Ensure();
            var r = new List<ShapeEntry>();
            foreach (var s in _shapes) if (s.Category == cat) r.Add(s);
            return r;
        }

        internal static ShapeEntry FindById(string id)
        {
            Ensure();
            if (id == null) return null;
            foreach (var s in _shapes) if (s.Id == id) return s;
            return null;
        }

        private static void Ensure()
        {
            if (_shapes != null) return;
            _categories = new List<string> { BuiltInCat };
            _shapes = new List<ShapeEntry>();
            foreach (var def in ShapeLibrary.All)
            {
                var e = new ShapeEntry { Id = "b:" + def.Name, Name = def.Name, Category = BuiltInCat, Base = def.Base, BuiltIn = true };
                foreach (var v in def.Variants)
                    e.Variants.Add(new ShapeVariant { K = v.K, Angles = def.Unit(v.K), Twirls = def.Twirls(v.K), N = def.DefaultN(v.K) });
                _shapes.Add(e);
            }
            LoadCustom();
        }

        private static void LoadCustom()
        {
            var s = MainClass.Settings; if (s == null) return;
            if (s.ShapeCategories != null)
                foreach (var c in s.ShapeCategories)
                    if (!string.IsNullOrEmpty(c) && !_categories.Contains(c)) _categories.Add(c);
            if (s.CustomShapes != null)
                foreach (var dto in s.CustomShapes)
                {
                    if (dto == null) continue;
                    var e = new ShapeEntry { Id = "c:" + (_customSeq++), Name = dto.Name, Category = dto.Category, Base = dto.Base, BuiltIn = false };
                    if (dto.Variants != null)
                        foreach (var vd in dto.Variants)
                            e.Variants.Add(new ShapeVariant
                            {
                                K = vd.K,
                                Angles = vd.Angles != null ? vd.Angles.ToArray() : new double[0],
                                Twirls = vd.Twirls != null ? vd.Twirls.ToArray() : new bool[0],
                                N = vd.N < 1 ? 3 : vd.N
                            });
                    if (!_categories.Contains(e.Category)) _categories.Add(e.Category);
                    _shapes.Add(e);
                }
        }

        // ── mutations (persist immediately) ───────────────────────────────────

        internal static void AddCategory(string name)
        {
            Ensure();
            name = (name ?? "").Trim();
            if (name.Length == 0 || _categories.Contains(name)) return;
            _categories.Add(name);
            Persist();
        }

        internal static void RenameCategory(string oldName, string newName)
        {
            Ensure();
            newName = (newName ?? "").Trim();
            if (oldName == BuiltInCat || newName.Length == 0 || _categories.Contains(newName)) return;
            int i = _categories.IndexOf(oldName); if (i < 0) return;
            _categories[i] = newName;
            foreach (var sh in _shapes) if (sh.Category == oldName) sh.Category = newName;
            Persist();
        }

        internal static void DeleteCategory(string cat)
        {
            Ensure();
            if (cat == BuiltInCat) return;
            _shapes.RemoveAll(sh => sh.Category == cat);
            _categories.Remove(cat);
            Persist();
        }

        internal static ShapeEntry AddShape(string name, string category, double baseAngle, ShapeVariant variant)
        {
            Ensure();
            if (variant == null) return null;
            category = (category ?? "").Trim(); if (category.Length == 0) category = "My shapes";
            if (!_categories.Contains(category)) _categories.Add(category);
            var e = new ShapeEntry
            {
                Id = "c:" + (_customSeq++),
                Name = string.IsNullOrEmpty(name) ? ((int)Math.Round(baseAngle)) + "°" : name,
                Category = category, Base = baseAngle, BuiltIn = false
            };
            e.Variants.Add(variant);
            _shapes.Add(e);
            Persist();
            return e;
        }

        internal static void DeleteShape(ShapeEntry e)
        {
            Ensure();
            if (e == null || e.BuiltIn) return;
            _shapes.Remove(e);
            Persist();
        }

        private static void Persist()
        {
            var s = MainClass.Settings; if (s == null) return;
            s.ShapeCategories = new List<string>();
            foreach (var c in _categories) if (c != BuiltInCat) s.ShapeCategories.Add(c);
            s.CustomShapes = new List<CustomShapeDto>();
            foreach (var e in _shapes)
            {
                if (e.BuiltIn) continue;
                var dto = new CustomShapeDto { Name = e.Name, Category = e.Category, Base = e.Base, Variants = new List<ShapeVariantDto>() };
                foreach (var v in e.Variants)
                    dto.Variants.Add(new ShapeVariantDto { K = v.K, Angles = new List<double>(v.Angles), Twirls = new List<bool>(v.Twirls), N = v.N });
                s.CustomShapes.Add(dto);
            }
            MainClass.SaveSettings();
        }

        // ── build a variant from an angle-pad expression ("30t 30t 180") ──────
        // Each token is a relative charter (math via ExprEval) with an optional trailing 't' twirl.
        // Base = sum of the angles. Returns null on any malformed token.
        internal static ShapeVariant ParseUnit(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return null;
            var toks = expr.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length == 0) return null;
            var angles = new List<double>(); var twirls = new List<bool>(); double sum = 0;
            foreach (var tk in toks)
            {
                string t = tk;
                bool tw = t.EndsWith("t") || t.EndsWith("T");
                if (tw) t = t.Substring(0, t.Length - 1);
                if (!ExprEval.TryEval(t, out double v)) return null;
                angles.Add(v); twirls.Add(tw); sum += v;
            }
            return new ShapeVariant { K = angles.Count, Angles = angles.ToArray(), Twirls = twirls.ToArray(), N = 3 };
        }

        internal static double SumOf(ShapeVariant v)
        {
            double s = 0; if (v != null && v.Angles != null) foreach (var a in v.Angles) s += a; return s;
        }

        // ── capture a variant from the current editor selection ───────────────
        // Reads the selected floors' relative charters (from absolute facings) and their Twirl
        // events. Charter of tile s = 180 − (facing[s] − facing[s−1]); twirl flag = a Twirl event
        // one tile BEFORE (the build/insert convention). Returns null if nothing usable is selected.
        internal static ShapeVariant CaptureSelection()
        {
            try
            {
                var ed = scnEditor.instance; if (ed == null) return null;
                var sel = ed.selectedFloors; if (sel == null || sel.Count == 0) return null;
                var seqs = new List<int>();
                foreach (var f in sel) if (f != null) seqs.Add(f.seqID);
                if (seqs.Count == 0) return null;
                seqs.Sort();
                var af = ADOBase.lm.floorAngles;
                var twSet = new HashSet<int>();
                foreach (var ev in ed.events)
                    if (ev != null && ev.eventType == ADOFAI.LevelEventType.Twirl) twSet.Add(ev.floor);

                var angles = new List<double>(); var twirls = new List<bool>();
                foreach (int seq in seqs)
                {
                    if (seq <= 0 || seq >= af.Length) return null;
                    double turn = Norm180(af[seq] - af[seq - 1]);
                    angles.Add(System.Math.Round(180.0 - turn, 3));      // relative charter
                    twirls.Add(twSet.Contains(seq - 1));                 // twirl sits one tile before
                }
                return new ShapeVariant { K = angles.Count, Angles = angles.ToArray(), Twirls = twirls.ToArray(), N = 3 };
            }
            catch { return null; }
        }

        private static double Norm180(double a) { a %= 360.0; if (a > 180) a -= 360; if (a < -180) a += 360; return a; }
    }
}
