using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Sapphire
{
    internal static class FontLoader
    {
        internal class FontEntry
        {
            public readonly string Name;
            public readonly Font Font;        // bundled legacy Font (AssetBundle), or null
            private readonly string _filePath; // loose .ttf/.otf path (custom font), or null
            // Same family Bold weight, wired by LinkFamilies after scan
            internal FontEntry BoldSibling;
            private TMP_FontAsset _tmp;

            public FontEntry(string name, Font font) { Name = name; Font = font; }
            // Loose font file (user-droppable). Unity 6 TMP's CreateFontAsset(filePath) keeps
            // the path and reloads the face on demand, so glyphs (incl. Korean) populate
            // dynamically just like the bundled fonts — no AssetBundle needed.
            public FontEntry(string name, string filePath) { Name = name; _filePath = filePath; }

            /* Created on first use: dynamic SDF atlas, with family real Bold in weight
               table so <b>/FontStyles.Bold doesn't fall back to synthetic bold */
            public TMP_FontAsset TmpFont
            {
                get
                {
                    if (_tmp == null)
                    {
                        if (Font != null)
                            _tmp = TMP_FontAsset.CreateFontAsset(Font);
                        else if (!string.IsNullOrEmpty(_filePath))
                            // Match CreateFontAsset(Font)'s defaults: 90pt, 9 padding, SDFAA, 1024².
                            _tmp = TMP_FontAsset.CreateFontAsset(_filePath, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                        if (_tmp != null)
                        {
                            _tmp.name = Name + " (TMP)";
                            if (BoldSibling != null && BoldSibling != this)
                                _tmp.fontWeightTable[7].regularTypeface = BoldSibling.TmpFont;
                        }
                    }
                    return _tmp;
                }
            }

            internal void DestroyTmp()
            {
                if (_tmp == null) return;
                var atlases = _tmp.atlasTextures;
                if (atlases != null)
                    foreach (var tex in atlases)
                        if (tex != null) UnityEngine.Object.Destroy(tex);
                if (_tmp.material != null) UnityEngine.Object.Destroy(_tmp.material);
                UnityEngine.Object.Destroy(_tmp);
                _tmp = null;
            }
        }

        /* Canonical ordering for weight cycle. Names not in this list sort last, in
           scan order */
        internal static readonly string[] WeightOrder =
        {
            "Thin", "ExtraLight", "UltraLight", "Light", "Regular", "Medium",
            "SemiBold", "DemiBold", "Bold", "ExtraBold", "UltraBold", "Heavy", "Black",
        };

        /* "Pretendard SemiBold" / "Pretendard-SemiBold" maps to ("Pretendard",
           "SemiBold"). Some families prefix the weight with an ordinal to force file
           ordering ("Paperlogy-7Bold", "Paperlogy-4Regular") — the leading digits are
           stripped before matching. A last token that still isn't a known weight is a
           single-weight family, shown under its full name. */
        internal static void SplitWeight(string name, out string family, out string weight)
        {
            family = name;
            weight = "Regular";
            if (string.IsNullOrEmpty(name)) return;
            int sp = name.LastIndexOfAny(new[] { ' ', '-' });
            if (sp <= 0) return;
            string last = name.Substring(sp + 1).TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            foreach (var w in WeightOrder)
            {
                if (string.Equals(last, w, StringComparison.OrdinalIgnoreCase))
                {
                    family = name.Substring(0, sp);
                    weight = w;
                    return;
                }
            }
        }

        internal static int WeightRank(string weight)
        {
            for (int i = 0; i < WeightOrder.Length; i++)
                if (string.Equals(WeightOrder[i], weight, StringComparison.OrdinalIgnoreCase)) return i;
            return WeightOrder.Length;
        }

        /* Weight-override sentinel: resolves to family heaviest weight at apply time, so
           it tracks family switches instead of pinning specific name */
        internal const string WeightHeaviest = "Heaviest";

        /* Saved settings may spell font with spaces ("Maplestory Bold") while bundle
           asset uses hyphens ("Maplestory-Bold"), match ignoring both */
        private static string NormalizeName(string s) =>
            s == null ? "" : s.Replace(" ", "").Replace("-", "").ToLowerInvariant();

        internal static FontEntry Find(IList<FontEntry> fonts, string name)
        {
            if (fonts == null || string.IsNullOrEmpty(name)) return null;
            string norm = NormalizeName(name);
            foreach (var e in fonts)
                if (NormalizeName(e.Name) == norm) return e;
            return null;
        }

        private static string PlatformBundleSuffix()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer: return "-win";
                case RuntimePlatform.LinuxPlayer:   return "-linux";
                default:                            return "-mac";
            }
        }

        public static List<FontEntry> ScanFonts(string modPath)
        {
            var result = new List<FontEntry>();
            string resourcesDir = Path.Combine(modPath, "Resources");
            string suffix = PlatformBundleSuffix();

            if (Directory.Exists(resourcesDir))
            {
                foreach (string filePath in Directory.GetFiles(resourcesDir))
                {
                    string name = Path.GetFileName(filePath);
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext == ".meta" || ext == ".ttf" || ext == ".otf") continue; // fonts handled below
                    // Skip bundles belonging to a different platform
                    bool wrongPlatform = false;
                    foreach (string s in new[] { "-mac", "-win", "-linux" })
                        if (s != suffix && name.EndsWith(s, StringComparison.OrdinalIgnoreCase)) wrongPlatform = true;
                    if (!wrongPlatform) TryLoadBundle(filePath, result);
                }
            }

            // Loose custom fonts (user-droppable .ttf/.otf) from <mod>/Fonts and <mod>/Resources.
            ScanLooseFonts(Path.Combine(modPath, "Fonts"), result);
            ScanLooseFonts(resourcesDir, result);

            LinkFamilies(result);
            return result;
        }

        // Register loose .ttf/.otf files as custom fonts. The file name (minus extension) is the
        // entry name, so "Foo-Bold.ttf" splits into family Foo / weight Bold and bold-links like
        // a bundled weight. Skips names already present (a bundled font wins).
        private static void ScanLooseFonts(string dir, List<FontEntry> result)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string filePath in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf") continue;
                string name = Path.GetFileNameWithoutExtension(filePath);
                if (Find(result, name) != null) continue;
                result.Add(new FontEntry(name, filePath));
                MainClass.Logger.Log($"[Sapphire] Found custom font '{name}' ({Path.GetFileName(filePath)})");
            }
        }

        /* Pick each family's "bold" weight for <b>/FontStyles.Bold (TMP otherwise faux-bolds).
           Prefer an exact "Bold"; if the family has none, fall back to its heaviest weight that
           is still ≥ Bold (ExtraBold/Black), so a family shipped without a plain Bold still gets
           a REAL bold rather than synthetic. A regular gets the family's bold; the bold weight
           itself gets no sibling. */
        private static void LinkFamilies(List<FontEntry> entries)
        {
            var byFamily = new Dictionary<string, List<FontEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                SplitWeight(e.Name, out string fam, out _);
                if (!byFamily.TryGetValue(fam, out var list)) byFamily[fam] = list = new List<FontEntry>();
                list.Add(e);
            }
            int boldRank = WeightRank("Bold");
            foreach (var e in entries)
            {
                SplitWeight(e.Name, out string fam, out string w);
                if (!byFamily.TryGetValue(fam, out var family)) continue;

                FontEntry exact = null, heaviest = null;
                int heaviestRank = -1;
                foreach (var c in family)
                {
                    SplitWeight(c.Name, out _, out string cw);
                    if (string.Equals(cw, "Bold", StringComparison.OrdinalIgnoreCase)) exact = c;
                    int r = WeightRank(cw);
                    if (r > heaviestRank) { heaviestRank = r; heaviest = c; }
                }

                FontEntry bold = exact;
                // No plain Bold → use the heaviest weight if it's at least Bold and heavier than us.
                if (bold == null && heaviest != null && heaviestRank >= boldRank && heaviestRank > WeightRank(w))
                    bold = heaviest;
                if (bold != null && bold != e) e.BoldSibling = bold;
            }
        }

        internal static void DestroyTmpAssets(List<FontEntry> entries)
        {
            if (entries == null) return;
            foreach (var e in entries) e.DestroyTmp();
        }

        private static void TryLoadBundle(string path, List<FontEntry> result)
        {
            AssetBundle bundle = null;
            try
            {
                bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null) return;

                Font[] fonts = bundle.LoadAllAssets<Font>();
                if (fonts == null) return;

                foreach (Font font in fonts)
                {
                    if (font == null) continue;
                    MainClass.Logger.Log($"[Sapphire] Loaded font '{font.name}' from bundle");
                    result.Add(new FontEntry(font.name, font));
                }
            }
            catch (Exception e)
            {
                MainClass.Logger.Warning($"[Sapphire] Bundle '{Path.GetFileName(path)}': {e.Message}");
            }
            finally
            {
                // Unload bundle structure but keep assets alive in memory
                bundle?.Unload(false);
            }
        }
    }
}
