using System;
using System.IO;

namespace Sapphire
{
    internal static class SapphireLog
    {
        private static string _path;

        internal static void Init()
        {
            _path = Path.Combine(MainClass.ModPath, "SapphireLog.txt");
            try
            {
                File.WriteAllText(_path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Sapphire session start\n");
            }
            catch { _path = null; }
        }

        internal static void Log(string message)
        {
            if (_path == null) return;
            try { File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss}] {message}\n"); }
            catch { }
        }

        // Truncate the log file (Clear button in the log viewer).
        internal static void Clear()
        {
            if (_path == null) return;
            try { File.WriteAllText(_path, $"[{DateTime.Now:HH:mm:ss}] log cleared\n"); }
            catch { }
        }

        /* High-frequency diagnostics: hook traces, per-attempt dumps. Written to file
           like everything else, but in-game viewer hides [dbg] lines unless Debug
           toggle on */
        internal static void Debug(string message) => Log("[dbg] " + message);

        /* Tail of current log for in-game viewer. Capped well below uGUI Text
           65k-vertex limit, ~4 verts per glyph */
        internal static string ReadTail(int maxChars = 12000)
        {
            if (_path == null) return "(log not initialized)";
            try
            {
                string s = File.ReadAllText(_path);
                if (s.Length <= maxChars) return s;
                s = s.Substring(s.Length - maxChars);
                int nl = s.IndexOf('\n');
                return "…\n" + (nl >= 0 ? s.Substring(nl + 1) : s);
            }
            catch (Exception e)
            {
                return "(log unavailable: " + e.Message + ")";
            }
        }
    }
}
