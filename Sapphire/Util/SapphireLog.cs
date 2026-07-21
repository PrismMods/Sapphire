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

        /* File.AppendAllText opens, writes and closes the file on every call. That is fine for
           the occasional one-off, but several log sites sit inside per-tile / per-group loops
           in the batch pseudo-tools (EditorToolbar), so a single operation on a large
           selection turned into hundreds of synchronous open/close syscalls on the main
           thread. Buffer instead and let Flush() coalesce a burst into one write. */
        private static readonly System.Text.StringBuilder _buf = new System.Text.StringBuilder();
        private const int FlushThreshold = 8192;

        internal static void Log(string message)
        {
            if (_path == null) return;
            bool flushNow;
            lock (_buf)
            {
                _buf.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ")
                    .Append(message).Append('\n');
                flushNow = _buf.Length >= FlushThreshold;
            }
            if (flushNow) Flush();
        }

        /* Drains the buffer to disk. Called on a cadence from the ticker, before any read of
           the log, and on unload — so a crash can lose at most the last unflushed slice. */
        internal static void Flush()
        {
            if (_path == null) return;
            string pending;
            lock (_buf)
            {
                if (_buf.Length == 0) return;
                pending = _buf.ToString();
                _buf.Length = 0;
            }
            try { File.AppendAllText(_path, pending); }
            catch { }
        }

        // Truncate the log file (Clear button in the log viewer).
        internal static void Clear()
        {
            if (_path == null) return;
            lock (_buf) _buf.Length = 0;   // drop pending lines, they'd reappear after the truncate
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
            Flush();   // the viewer must see lines that are still buffered
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
