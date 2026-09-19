using System;
using System.Collections.Generic;

namespace Sapphire
{
    /* Helpers for writing ordinary events the unmodded game runs — used by the script compiler
       (EditorScript). The game has no variables, but SetInputEvent keeps a global key → events
       table that a fired SetInputEvent rewrites; ScriptLang builds its state machines on that. */
    internal static class VanillaLogic
    {
        // SetInputEvent must exist AND take an eventTag, or a key can't rebind itself.
        internal static bool Registry(out string why)
        {
            why = null;
            ADOFAI.LevelEventInfo info;
            if (!GCS.levelEventsInfo.TryGetValue("SetInputEvent", out info) || info == null)
            { why = Loc.T("This game version has no SetInputEvent"); return false; }
            var keys = info.propertiesInfo != null ? new List<string>(info.propertiesInfo.Keys) : new List<string>();
            SapphireLog.Log("VanillaLogic: SetInputEvent properties [" + string.Join(", ", keys) + "]");
            if (!keys.Contains("eventTag"))
            { why = Loc.T("SetInputEvent can't carry a tag in this game version, so a key can't switch states"); return false; }
            return true;
        }

        /* Typed like the default already there — an enum by name, numbers to the stored numeric
           type — and ENABLED: a fresh event's optional properties come disabled, so a value set
           alone would do nothing. */
        internal static void Set(ADOFAI.LevelEvent ev, string key, object v)
        {
            try
            {
                object cur = ev[key];
                if (cur is Enum && v is string) v = Enum.Parse(cur.GetType(), (string)v);
                else if (cur is float && v is double) v = (float)(double)v;
                else if (cur is int && v is double) v = (int)(double)v;
                ev[key] = v;
                if (ev.disabled != null) ev.disabled[key] = false;
            }
            catch (Exception ex) { SapphireLog.Log("VanillaLogic: " + ev.eventType + "." + key + " not set: " + ex.Message); }
        }
    }
}
