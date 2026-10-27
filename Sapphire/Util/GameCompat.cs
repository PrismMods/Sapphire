using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace Sapphire
{
    /* Game members whose shape differs between ADOFAI builds, reached by NAME so one dll runs on all
       of them (2.9 on Unity 2022, 3.x on Unity 6). A direct reference binds to one build's signature
       and throws MissingMember on the others — tools/refcheck lists them. */
    internal static class GameCompat
    {
        /* Probes for members a build may lack use plain reflection: MelonLoader logs a WARNING for
           every AccessTools miss, and on the build without the member that is every launch. */
        // LevelEvent.data is public on 2.9 and protected on 3.x, which wraps it in ContainsKey/GetData.
        private static readonly AccessTools.FieldRef<ADOFAI.LevelEvent, Dictionary<string, object>> _evData =
            AccessTools.FieldRefAccess<ADOFAI.LevelEvent, Dictionary<string, object>>("data");

        internal static Dictionary<string, object> Data(this ADOFAI.LevelEvent e) => e == null ? null : _evData(e);

        internal static bool HasKey(this ADOFAI.LevelEvent e, string key)
        {
            var d = e.Data();
            return d != null && key != null && d.ContainsKey(key);
        }

        // RDString.Get/GetWithCheck: 2.9 adds a trailing LangSection parameter.
        private static readonly MethodInfo _rdGet = Lead(typeof(RDString), "Get", typeof(string));
        private static readonly MethodInfo _rdGetCheck = Lead(typeof(RDString), "GetWithCheck", typeof(string), typeof(bool).MakeByRefType());

        internal static string Str(string key, Dictionary<string, object> parameters = null)
            => (string)_rdGet.Invoke(null, Args(_rdGet, key, parameters));

        internal static string StrCheck(string key, out bool exists, Dictionary<string, object> parameters = null)
        {
            var a = Args(_rdGetCheck, key, false, parameters);
            var s = (string)_rdGetCheck.Invoke(null, a);
            exists = (bool)a[1];
            return s;
        }

        // A field on 2.9's TMP, a property on Unity 6's.
        private static readonly PropertyInfo _matProp = typeof(TMP_Asset).GetProperty("material", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo _matField = _matProp == null ? typeof(TMP_Asset).GetField("material", BindingFlags.Public | BindingFlags.Instance) : null;

        internal static Material MaterialOf(TMP_Asset a)
        {
            if (a == null) return null;
            return (Material)(_matProp != null ? _matProp.GetValue(a, null) : _matField?.GetValue(a));
        }

        // TMP_FontAsset's on Unity 2022, TMP_Asset's on Unity 6: naming it binds to whichever the
        // build ran against, and a 3.x-built dll then fails on 2.9.
        private static readonly PropertyInfo _faceInfo = typeof(TMP_FontAsset).GetProperty("faceInfo", BindingFlags.Public | BindingFlags.Instance);

        internal static FaceInfo FaceInfoOf(TMP_FontAsset fa) => fa == null || _faceInfo == null ? default(FaceInfo) : (FaceInfo)_faceInfo.GetValue(fa, null);

        // int on Unity 2022, float on Unity 6.
        private static readonly PropertyInfo _pointSize = typeof(FaceInfo).GetProperty("pointSize", BindingFlags.Public | BindingFlags.Instance);

        internal static float PointSize(FaceInfo fi) => Convert.ToSingle(_pointSize.GetValue(fi, null));

        // The public static overload whose parameters start with `lead`; the rest are optional.
        private static MethodInfo Lead(Type t, string name, params Type[] lead)
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length < lead.Length) continue;
                bool ok = true;
                for (int i = 0; i < lead.Length && ok; i++) ok = ps[i].ParameterType == lead[i];
                for (int i = lead.Length; i < ps.Length && ok; i++) ok = ps[i].IsOptional;
                if (ok) return m;
            }
            return null;
        }

        // Leading values, then each remaining parameter's declared default.
        private static object[] Args(MethodInfo m, params object[] lead)
        {
            var ps = m.GetParameters();
            var a = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < lead.Length) { a[i] = lead[i]; continue; }
                var p = ps[i];
                object d = p.DefaultValue;
                if (d == null || d is DBNull || d == Type.Missing)
                    a[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                else
                    a[i] = p.ParameterType.IsEnum ? Enum.ToObject(p.ParameterType, d) : d;
            }
            return a;
        }

        // 3.x only; 2.9's editor has no paused-in-play state to mirror.
        private static readonly PropertyInfo _pausedInPlay = typeof(scnEditor).GetProperty("pausedInPlayMode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        internal static void SetPausedInPlayMode(scnEditor ed, bool v)
        {
            if (_pausedInPlay != null && ed != null) _pausedInPlay.SetValue(ed, v, null);
        }

        // Public on 3.x, private on 2.9.
        private static readonly MethodInfo _approval = typeof(scnEditor).GetMethod("ApprovalLevelForArtist", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);

        internal static bool TryApprovalLevel(scnEditor ed, string artist, out ApprovalLevel level)
        {
            level = default(ApprovalLevel);
            if (_approval == null || ed == null) return false;
            level = (ApprovalLevel)_approval.Invoke(ed, new object[] { artist });
            return true;
        }

        // Path letter → absolute angle: ADOFAI.FloorHelper on 3.x, scrLevelMaker on 2.9.
        private static readonly MethodInfo _pathDeg3 =
            typeof(scnEditor).Assembly.GetType("ADOFAI.FloorHelper")?.GetMethod("TryGetPathInDegrees", BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo _pathDeg2 =
            typeof(scrLevelMaker).GetMethod("GetAngleFromFloorCharDirectionWithCheck", BindingFlags.Public | BindingFlags.Static);

        internal static bool TryPathDegrees(char ch, out float deg)
        {
            deg = 0f;
            if (_pathDeg3 != null)
            {
                var args = new object[] { ch, 0f };
                bool ok = (bool)_pathDeg3.Invoke(null, args);
                deg = (float)args[1];
                return ok;
            }
            if (_pathDeg2 != null)
            {
                var args = new object[] { ch, false };
                deg = (float)_pathDeg2.Invoke(null, args);
                return (bool)args[1];
            }
            return false;
        }
    }
}
