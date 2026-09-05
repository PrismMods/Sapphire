using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ADOFAI;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.Video;

namespace Sapphire
{
    /* Decoration/lyric tools from AdofaiMappingHelper by Sprout34 (integrated with
       permission): image-sequence flipbooks, video frame extraction (Unity VideoPlayer),
       lerped 3D decoration stacks, and lyric generation. MH's GDI+ text renderer is
       replaced with a TMP SDF render-to-texture (TextToPng below) — same feature, no
       System.Drawing dependency, so it works on the game's Mono runtime everywhere. */
    internal static class DecoToolsEngine
    {
        // ── natural filename order (MH's NaturalStringComparer) ─────────────

        private static readonly Regex NumRuns = new Regex(@"(\d+)", RegexOptions.Compiled);

        internal static string[] NaturalSort(string[] names)
            => names.OrderBy(x => x, new NaturalComparer()).ToArray();

        private class NaturalComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (x == null && y == null) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                string[] xs = NumRuns.Split(x), ys = NumRuns.Split(y);
                for (int i = 0; i < Math.Min(xs.Length, ys.Length); i++)
                {
                    if (xs[i] == ys[i]) continue;
                    long a, b;
                    if (long.TryParse(xs[i], out a) && long.TryParse(ys[i], out b))
                        return a.CompareTo(b);
                    return string.Compare(xs[i], ys[i], StringComparison.OrdinalIgnoreCase);
                }
                return xs.Length.CompareTo(ys.Length);
            }
        }

        // ── color gradient (MH's ColorGradientUtil, Unity-native already) ────

        internal static List<string> SplitGradientHex(string startHex, string endHex, int n)
        {
            Color c1, c2;
            if (!ColorUtility.TryParseHtmlString(startHex, out c1)) c1 = Color.white;
            if (!ColorUtility.TryParseHtmlString(endHex, out c2)) c2 = Color.white;
            var list = new List<string>(n);
            if (n == 1) { list.Add(ToHex(c1)); return list; }
            for (int i = 0; i < n; i++)
                list.Add(ToHex(Color.LerpUnclamped(c1, c2, i / (float)(n - 1))));
            return list;
        }

        private static string ToHex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255);
            return r.ToString("X2") + g.ToString("X2") + b.ToString("X2") + a.ToString("X2");
        }

        internal static string LevelDir()
        {
            try
            {
                string p = scnEditor.instance.customLevel.levelPath;
                return string.IsNullOrEmpty(p) ? null : Path.GetDirectoryName(p);
            }
            catch { return null; }
        }

        // ── flipbook: image sequence folder → AddDecoration + frame swaps ───

        internal class FlipbookParams
        {
            public int From, To;
            public string Folder = "";       // folder name inside the level directory
            public string Tag = "", EventTag = "";
            public bool WindowOn; public int FrameStart = 1, FrameEnd = 1;
            public float InitialAngleOffset;
            public float AngleStep;          // per-frame angleOffset increment
        }

        // returns frames-per-tile used
        internal static int Flipbook(FlipbookParams p)
        {
            scnEditor editor = scnEditor.instance;
            string levelDir = LevelDir();
            string folderName = Path.GetFileName(p.Folder.TrimEnd('/', Path.DirectorySeparatorChar));
            string framesDir = Path.Combine(levelDir, folderName);
            string[] images = Directory.GetFiles(framesDir);
            for (int i = 0; i < images.Length; i++) images[i] = Path.GetFileName(images[i]);
            images = NaturalSort(images);
            if (images.Length == 0) return 0;

            var add = new LevelEvent(p.From, LevelEventType.AddDecoration);
            add["decorationImage"] = Path.Combine(folderName, images[0]);
            add["tag"] = p.Tag;
            add["relativeTo"] = DecPlacementType.Tile;
            add["depth"] = 1;
            editor.levelData.decorations.Add(add);
            scrDecorationManager.instance.CreateDecoration(add, out _, -1);

            int start = p.WindowOn ? p.FrameStart : 1;
            int end = p.WindowOn ? p.FrameEnd : images.Length;
            start = Mathf.Clamp(start, 1, images.Length);
            end = Mathf.Clamp(end, 1, images.Length);
            int step = start <= end ? 1 : -1;
            int used = Math.Abs(end - start) + 1;

            for (int floor = p.From; floor <= p.To; floor++)
            {
                float curAngle = p.InitialAngleOffset;
                for (int i = start; step > 0 ? i <= end : i >= end; i += step)
                {
                    var md = new LevelEvent(floor, LevelEventType.MoveDecorations);
                    md.disabled["decorationImage"] = false;
                    md.disabled["positionOffset"] = true;
                    md["duration"] = 0f;
                    md["tag"] = p.Tag;
                    md["decorationImage"] = Path.Combine(folderName, images[i - 1]);
                    md["angleOffset"] = curAngle;
                    md["eventTag"] = p.EventTag;
                    curAngle += p.AngleStep;
                    editor.events.Add(md);
                }
            }
            editor.ApplyEventsToFloors();
            editor.RemakePath(true, true);
            editor.DeselectFloors();
            editor.SelectFloor(scrLevelMaker.instance.listFloors[p.From]);
            return used;
        }

        // ── video → frame folder (MH's ExtraVideo, Unity VideoPlayer) ───────

        internal static void ExtractFrames(string videoAbsPath, string outputFolder, bool jpg, Action<int> onDone)
        {
            var runnerGo = new GameObject("SapphireVideoExtract");
            UnityEngine.Object.DontDestroyOnLoad(runnerGo);
            var runner = runnerGo.AddComponent<DecoCoroutineRunner>();
            runner.StartCoroutine(ExtractFramesCo(runnerGo, videoAbsPath, outputFolder, jpg, onDone));
        }

        private static IEnumerator ExtractFramesCo(GameObject runnerGo, string videoPath, string outputFolder, bool jpg, Action<int> onDone)
        {
            int frameIndex = 0;
            if (File.Exists(videoPath))
            {
                var videoPlayer = new GameObject("TempVideoPlayer").AddComponent<VideoPlayer>();
                videoPlayer.url = videoPath;
                videoPlayer.playOnAwake = false;
                videoPlayer.isLooping = false;
                videoPlayer.playbackSpeed = 0f; // paused; StepForward walks frame by frame

                if (!Directory.Exists(outputFolder))
                    Directory.CreateDirectory(outputFolder);

                videoPlayer.Prepare();
                while (!videoPlayer.isPrepared)
                    yield return null;

                var rt = new RenderTexture((int)videoPlayer.width, (int)videoPlayer.height, 0, RenderTextureFormat.ARGB32);
                rt.Create();
                videoPlayer.targetTexture = rt;

                string baseName = Path.GetFileNameWithoutExtension(videoPath);
                string ext = jpg ? "jpg" : "png";
                while (videoPlayer.frame < (long)videoPlayer.frameCount - 1)
                {
                    videoPlayer.StepForward();
                    yield return new WaitForEndOfFrame();

                    string path = Path.Combine(outputFolder, baseName + " (" + (frameIndex + 1) + ")." + ext);
                    if (!File.Exists(path))
                    {
                        RenderTexture.active = rt;
                        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;
                        File.WriteAllBytes(path, jpg ? tex.EncodeToJPG() : tex.EncodeToPNG());
                        UnityEngine.Object.Destroy(tex);
                    }
                    frameIndex++;
                }

                rt.Release();
                UnityEngine.Object.Destroy(rt);
                UnityEngine.Object.Destroy(videoPlayer.gameObject);
            }
            try { onDone?.Invoke(frameIndex); } catch { }
            UnityEngine.Object.Destroy(runnerGo);
        }

        internal class DecoCoroutineRunner : MonoBehaviour { }

        // ── 3D stack: N lerped decoration copies per tile ────────────────────

        internal class Deco3DParams
        {
            public int From, To;
            public string Image = "";        // relative to level dir
            public string Tag = "";
            public int Count = 10;
            public Vector2 PosA, PosB;
            public Vector2 PivotA, PivotB;
            public float RotA, RotB;
            public Vector2 ScaleA = new Vector2(100, 100), ScaleB = new Vector2(100, 100);
            public float OpacityA = 100, OpacityB = 100;
            public int DepthA = 1, DepthB = 1;
            public Vector2 ParA, ParB;
            public string ColorA = "ffffffff", ColorB = "ffffffff";
        }

        internal static void Deco3D(Deco3DParams p)
        {
            scnEditor editor = scnEditor.instance;
            var colors = SplitGradientHex("#" + p.ColorA, "#" + p.ColorB, p.Count);
            for (int floor = p.From; floor <= p.To; floor++)
            {
                for (int i = 0; i < p.Count; i++)
                {
                    float t = (p.Count - 1) == 0 ? 0f : i / (float)(p.Count - 1);
                    var ev = new LevelEvent(floor, LevelEventType.AddDecoration);
                    ev["decorationImage"] = p.Image;
                    ev["tag"] = string.IsNullOrEmpty(p.Tag) ? string.Empty : p.Tag + " " + p.Tag + (i + 1);
                    ev["relativeTo"] = DecPlacementType.Tile;
                    ev["position"] = Vector2.LerpUnclamped(p.PosA, p.PosB, t);
                    ev["pivotOffset"] = Vector2.LerpUnclamped(p.PivotA, p.PivotB, t);
                    ev["rotation"] = Mathf.LerpUnclamped(p.RotA, p.RotB, t);
                    ev["scale"] = Vector2.LerpUnclamped(p.ScaleA, p.ScaleB, t);
                    ev["color"] = colors[i];
                    ev["opacity"] = Mathf.LerpUnclamped(p.OpacityA, p.OpacityB, t);
                    ev["depth"] = p.DepthA + (int)((p.DepthB - p.DepthA) / (float)((p.Count - 1) == 0 ? 1 : p.Count - 1) * i);
                    ev["parallax"] = Vector2.LerpUnclamped(p.ParA, p.ParB, t);
                    editor.levelData.decorations.Add(ev);
                    scrDecorationManager.instance.CreateDecoration(ev, out _, -1);
                }
            }
            editor.RemakePath(true, true);
        }

        // ── lyrics ────────────────────────────────────────────────────────────

        internal class LyricParams
        {
            public int From, To;
            public string Text = "";
            public bool SplitByChar;           // else by whitespace
            public bool AllAtOnce = true;      // else only the first part (MH's one-by-one)
            public bool AsDecoration;          // else built-in AddText
            public string Tag = "";
            public string ColorHex = "ffffffff";
            public Vector2 PositionInterval = new Vector2(1f, 0f);
            public float TimeInterval = 45f;   // angleOffset stagger between parts
            public float Duration = 1f;
            public TrackToolsEngine.RandRange XPos, YPos, XPivot, YPivot, Rot, Scale, Parallax;
            public bool OpacityOn = true; public float Opacity = 100f;
            public float AngleOffset;
            public Ease Ease = Ease.Linear;
            public bool ScaleRevertOn = true; public float ScaleRevertTo = 100f;
            public bool ParallaxRevertOn; public float ParallaxRevertTo;
            // rendered-PNG mode
            public string FontPath = "";
            public bool Stroke; public int StrokeSize = 2; public string StrokeColor = "000000ff";
            public bool Shadow; public Vector2 ShadowOffset; public int ShadowSpread = 15;
            public float ShadowDensity = 5f; public string ShadowColor = "000000ff";
            // disappear animation
            public bool Disappear;
            public float DisappearAfter = 1f, DisappearDuration = 1f;
            public TrackToolsEngine.RandRange DisXPos, DisYPos, DisRot, DisScale;
            public bool DisOpacityOn = true; public float DisOpacity;
            public Ease DisappearEase = Ease.Linear;
        }

        // returns number of lyric parts generated
        internal static int Lyric(LyricParams p)
        {
            scnEditor editor = scnEditor.instance;
            string[] parts = p.SplitByChar
                ? p.Text.ToCharArray().Select(c => c.ToString()).ToArray()
                : p.Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return 0;

            var xInterval = new float[parts.Length];
            var yInterval = new float[parts.Length];
            // centered row: even counts straddle the tile, odd counts sit on it
            for (int i = 0; i < parts.Length; i++)
            {
                float k = parts.Length % 2 == 0 ? i - parts.Length / 2 + 0.5f : i - (parts.Length - 1) / 2f;
                xInterval[i] = k * p.PositionInterval.x;
                yInterval[i] = k * p.PositionInterval.y;
            }

            int generationNum = p.AllAtOnce ? parts.Length : 1;
            string levelDir = LevelDir();

            for (int i = 0; i < generationNum; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i])) continue;

                string pngName = null;
                if (p.AsDecoration)
                {
                    string fontName = Path.GetFileNameWithoutExtension(p.FontPath);
                    pngName = p.Tag + "_[" + fontName + "]" + CleanFileName(parts[i]) + ".png";
                    TextToPng.Generate(CleanFileName(parts[i]), Path.Combine(levelDir, pngName), p.FontPath, 80,
                        p.Stroke, p.StrokeSize, ParseColor(p.StrokeColor),
                        p.Shadow, p.ShadowOffset, p.ShadowSpread, 0.1f + (p.ShadowDensity / 10f) * 1.9f, ParseColor(p.ShadowColor));
                }

                for (int floor = p.From; floor <= p.To; floor++)
                {
                    var add = new LevelEvent(floor, p.AsDecoration ? LevelEventType.AddDecoration : LevelEventType.AddText);
                    var md1 = new LevelEvent(floor, LevelEventType.MoveDecorations);
                    var md2 = new LevelEvent(floor, LevelEventType.MoveDecorations);
                    float scale = p.Scale.Sample();
                    float parallax = p.Parallax.Sample();
                    string partTag = p.Tag + "_" + parts[i] + "_" + (i + 1);

                    add["tag"] = p.Tag + " " + partTag;
                    add["relativeTo"] = DecPlacementType.Tile;
                    add["floor"] = floor;
                    add["position"] = new Vector2(xInterval[i], yInterval[i]);
                    add["parallax"] = p.Parallax.On ? new Vector2(parallax, parallax) : new Vector2(0f, 0f);
                    add["depth"] = (int)parallax;
                    add["scale"] = p.Scale.On ? new Vector2(scale, scale) : new Vector2(100f, 100f);
                    add["color"] = p.ColorHex;
                    if (p.AsDecoration)
                        add["decorationImage"] = pngName;
                    else
                    {
                        add["decText"] = parts[i];
                        add["font"] = FontName.Arial;
                    }

                    md1.disabled["positionOffset"] = !p.XPos.On && !p.YPos.On;
                    md1.disabled["pivotOffset"] = !p.XPivot.On && !p.YPivot.On;
                    md1.disabled["rotationOffset"] = !p.Rot.On;
                    md1.disabled["scale"] = true;
                    md1.disabled["opacity"] = true;
                    md2.disabled["positionOffset"] = !p.XPos.On && !p.YPos.On;
                    md2.disabled["pivotOffset"] = !p.XPivot.On && !p.YPivot.On;
                    md2.disabled["parallax"] = !p.ParallaxRevertOn;
                    md2.disabled["rotationOffset"] = !p.Rot.On;
                    md2.disabled["scale"] = !p.ScaleRevertOn;
                    md2.disabled["opacity"] = !p.OpacityOn;

                    float stagger = p.AngleOffset + (p.AllAtOnce ? p.TimeInterval * i : 0f);

                    md1["duration"] = 0f;
                    md1["tag"] = partTag;
                    md1["relativeTo"] = DecPlacementType.Tile;
                    md1["positionOffset"] = new Vector2(p.XPos.On ? p.XPos.Sample() : float.NaN, p.YPos.On ? p.YPos.Sample() : float.NaN);
                    md1["pivotOffset"] = new Vector2(p.XPivot.On ? p.XPivot.Sample() : float.NaN, p.YPivot.On ? p.YPivot.Sample() : float.NaN);
                    md1["rotationOffset"] = p.Rot.Sample();
                    md1["angleOffset"] = stagger;

                    md2["duration"] = p.Duration;
                    md2["tag"] = partTag;
                    md2["relativeTo"] = DecPlacementType.Tile;
                    md2["positionOffset"] = new Vector2(p.XPos.On ? 0f : float.NaN, p.YPos.On ? 0f : float.NaN);
                    md2["pivotOffset"] = new Vector2(p.XPivot.On ? 0f : float.NaN, p.YPivot.On ? 0f : float.NaN);
                    md2["parallax"] = p.ParallaxRevertOn ? new Vector2(p.ParallaxRevertTo, p.ParallaxRevertTo) : new Vector2(0f, 0f);
                    md2["rotationOffset"] = 0f;
                    md2["scale"] = new Vector2(p.ScaleRevertTo, p.ScaleRevertTo);
                    md2["opacity"] = p.Opacity;
                    md2["angleOffset"] = stagger;
                    md2["ease"] = p.Ease;

                    editor.levelData.decorations.Add(add);
                    editor.events.Add(md1);
                    editor.events.Add(md2);
                    scrDecorationManager.instance.CreateDecoration(add, out _, -1);

                    if (p.Disappear)
                    {
                        var md3 = new LevelEvent(floor, LevelEventType.MoveDecorations);
                        float scale2 = p.DisScale.Sample();
                        md3.disabled["positionOffset"] = !p.DisXPos.On && !p.DisYPos.On;
                        md3.disabled["pivotOffset"] = true;
                        md3.disabled["parallaxOffset"] = true;
                        md3.disabled["rotationOffset"] = !p.DisRot.On;
                        md3.disabled["scale"] = !p.DisScale.On;
                        md3.disabled["opacity"] = !p.DisOpacityOn;
                        md3.disabled["parallax"] = true;

                        md3["duration"] = p.DisappearDuration;
                        md3["tag"] = partTag;
                        md3["relativeTo"] = DecPlacementType.Tile;
                        md3["positionOffset"] = new Vector2(p.DisXPos.On ? p.DisXPos.Sample() : float.NaN, p.DisYPos.On ? p.DisYPos.Sample() : float.NaN);
                        md3["rotationOffset"] = p.DisRot.Sample();
                        md3["scale"] = p.DisScale.On ? new Vector2(scale2, scale2) : new Vector2(100f, 100f);
                        md3["opacity"] = p.DisOpacityOn ? p.DisOpacity : 100f;
                        md3["angleOffset"] = stagger + p.DisappearAfter * 180f;
                        md3["ease"] = p.DisappearEase;
                        editor.events.Add(md3);
                    }
                }
            }
            editor.RemakePath(true, true);
            return generationNum;
        }

        internal static string CleanFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Where(c => !invalid.Contains(c)));
        }

        internal static Color ParseColor(string rgbaHex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString("#" + (rgbaHex ?? "").TrimStart('#'), out c) ? c : Color.black;
        }
    }

    /* Text → PNG via TMP SDF rendered by an off-world ortho camera. Replaces MH's GDI+
       renderer: stroke = SDF outline, shadow = SDF underlay (offset/softness/dilate).
       Font assets come from loose .ttf/.otf files exactly like Sapphire's FontLoader
       (Unity 6 TMP CreateFontAsset(filePath) reloads faces on demand). Not pixel-identical
       to GDI+ output, but the same knobs steer the same visual features. */
    internal static class TextToPng
    {
        private static readonly Dictionary<string, TMP_FontAsset> _fonts = new Dictionary<string, TMP_FontAsset>();

        internal static void Dispose()
        {
            foreach (var f in _fonts.Values) if (f != null) UnityEngine.Object.Destroy(f);
            _fonts.Clear();
        }

        private static TMP_FontAsset GetFont(string path)
        {
            if (_fonts.TryGetValue(path, out var cached) && cached != null) return cached;
            var fa = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
            if (fa == null) throw new IOException("Font load failed: " + path);
            _fonts[path] = fa;
            return fa;
        }

        internal static void Generate(string text, string outputPath, string fontPath, float fontSize,
            bool stroke, int strokeSize, Color strokeColor,
            bool shadow, Vector2 shadowOffset, int shadowSpread, float shadowDensity, Color shadowColor)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var fa = GetFont(fontPath);

            // far off-world so the render camera sees only this text
            var textGo = new GameObject("SapphireLyricText");
            textGo.transform.position = new Vector3(100000f, 100000f, 0f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.font = fa;
            tmp.fontSize = fontSize;
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = Color.white;

            Material mat = new Material(tmp.fontMaterial);
            if (stroke)
            {
                mat.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp01(strokeSize / 20f));
                mat.SetColor(ShaderUtilities.ID_OutlineColor, strokeColor);
            }
            if (shadow)
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, shadowOffset.x / 10f);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, shadowOffset.y / 10f);
                mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Clamp01(shadowSpread / 30f));
                mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, Mathf.Clamp(shadowDensity - 1f, -1f, 1f));
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, shadowColor);
            }
            tmp.fontMaterial = mat;
            tmp.ForceMeshUpdate();

            // TMP world units = fontSize/10; render at 10 px per unit → glyphs ≈ fontSize px
            Vector2 size = tmp.GetRenderedValues(false);
            float margin = fontSize / 10f * 0.5f + (shadow ? Mathf.Max(Mathf.Abs(shadowOffset.x), Mathf.Abs(shadowOffset.y)) / 10f : 0f);
            float wUnits = Mathf.Max(0.1f, size.x + margin * 2f);
            float hUnits = Mathf.Max(0.1f, size.y + margin * 2f);
            int wPx = Mathf.Clamp(Mathf.CeilToInt(wUnits * 10f), 8, 4096);
            int hPx = Mathf.Clamp(Mathf.CeilToInt(hUnits * 10f), 8, 4096);

            var camGo = new GameObject("SapphireLyricCam");
            camGo.transform.position = textGo.transform.position + new Vector3(0f, 0f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = hUnits * 0.5f;
            cam.aspect = wUnits / hUnits;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.enabled = false; // manual Render() only

            var rt = new RenderTexture(wPx, hPx, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(wPx, hPx, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, wPx, hPx), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            File.WriteAllBytes(outputPath, tex.EncodeToPNG());

            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(mat);
            UnityEngine.Object.Destroy(textGo);
            UnityEngine.Object.Destroy(camGo);
        }
    }
}
