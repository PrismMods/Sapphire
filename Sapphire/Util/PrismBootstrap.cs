using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace PrismLib.Bootstrap
{
    /* Drop this ONE file into a mod (add it to the csproj <Compile> list) and call
       PrismBootstrap.Ensure(log) first thing in OnLoad. It finds, installs or updates PrismLib and
       makes it loadable — so the user installs Sapphire and gets PrismLib, without a second entry
       in the mod manager.

       Rules this file lives by:

       - It references NO PrismLib type. The CLR resolves a method's types when that method is
         JITted, so the resolver must be installed by a method that cannot possibly mention the
         assembly it is resolving. That is also why the caller must keep its own PrismLib usage in a
         separate [MethodImpl(NoInlining)] method — see Usage in the README.
       - It never throws. Every failure path returns false and the mod runs standalone.
       - It also resolves PrismLib.UI, which mods SHIP in their own folder rather than install.
         The UI half needs no shared instance — two mods each drawing their own toast is correct,
         and ToastStack coordinates them by GameObject name, not shared memory — so it is an
         ordinary dependency and its call sites need no Available guard. The resolver is installed
         before anything else here, so a mod's UI keeps working even when the network half fails.
       - It blocks on the network only when there is no usable copy at all, and then briefly. An
         update to an existing copy is fetched in the background and picked up next launch, because
         a game that hangs on load over a library nobody asked for is worse than being one version
         behind. */
    public static class PrismBootstrap
    {
        // Raw feed. Same shape as a UMM repository entry, minus the parts only UMM reads.
        private const string Feed = "https://raw.githubusercontent.com/PrismMods/PrismLib/main/prismlib.json";
        private const int NetTimeoutMs = 10000;

        private static Action<string> _log = _ => { };
        private static bool _ran;
        private static bool _ok;
        private static string _dir;      // the shared PrismLib folder, next to the mod folders

        /// Returns true when PrismLib is present and loadable. Safe to call more than once.
        public static bool Ensure(Action<string> log = null, Version minimum = null)
        {
            if (log != null) _log = log;
            if (_ran) return _ok;
            _ran = true;
            // First, unconditionally: PrismLib.UI ships beside the mod and must resolve whether or
            // not the shared PrismLib below can be reached.
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            try { _ok = Run(minimum ?? new Version(0, 1, 0)); }
            catch (Exception e) { Say("bootstrap failed: " + e.Message); _ok = false; }
            return _ok;
        }

        private static bool Run(Version minimum)
        {
            // Another Prism mod may already have loaded it this session. Nothing to do.
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "PrismLib")
                {
                    Say("already loaded: " + a.GetName().Version);
                    return a.GetName().Version >= minimum;
                }

            _dir = SharedDir();
            if (_dir == null) { Say("no mods folder found"); return false; }
            string path = Path.Combine(_dir, "PrismLib.dll");

            Version have = VersionOf(path);
            if (have == null || have < minimum)
            {
                Say(have == null ? "not installed, fetching" : "have " + have + ", need " + minimum + ", fetching");
                if (!Install(path)) return false;
                have = VersionOf(path);
                if (have == null || have < minimum) { Say("fetched copy is still too old"); return false; }
            }
            else
            {
                // Good enough to run with. Look for a newer one without making the game wait.
                var t = new Thread(() => { try { UpdateIfNewer(path); } catch { } }) { IsBackground = true };
                t.Start();
            }

            Assembly.LoadFrom(path);
            Say("loaded " + have + " from " + path);
            return true;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string want = new AssemblyName(args.Name).Name;
                if (!want.StartsWith("PrismLib", StringComparison.OrdinalIgnoreCase)) return null;

                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    if (string.Equals(a.GetName().Name, want, StringComparison.OrdinalIgnoreCase)) return a;

                // Beside the mod first (that is where a shipped PrismLib.UI lives), then the
                // shared folder (that is where the installed PrismLib lives).
                foreach (var dir in new[] { SelfDir(), _dir })
                {
                    if (dir == null) continue;
                    string p = Path.Combine(dir, want + ".dll");
                    if (File.Exists(p)) return Assembly.LoadFrom(p);
                }
                return null;
            }
            catch { return null; }
        }

        private static string SelfDir()
        {
            try { return Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath); }
            catch { return null; }
        }

        /* <game>/Mods/<ThisMod>/This.dll  ->  <game>/Mods/PrismLib/
           Derived from this assembly's own location so it works for both "Mods" and UMM's
           "UMMMods" layout, and for a game installed anywhere. */
        private static string SharedDir()
        {
            try
            {
                string self = SelfDir();
                string modsRoot = self != null ? Path.GetDirectoryName(self) : null;
                if (modsRoot == null || !Directory.Exists(modsRoot)) return null;
                string dir = Path.Combine(modsRoot, "PrismLib");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch { return null; }
        }

        private static Version VersionOf(string path)
        {
            // Metadata-only read: does not load the assembly, so a stale copy can be overwritten.
            try { return File.Exists(path) ? AssemblyName.GetAssemblyName(path).Version : null; }
            catch { return null; }
        }

        private static void UpdateIfNewer(string path)
        {
            // Quiet: a background update that cannot reach GitHub is not actionable — the mod is
            // already running on a good copy — and the thread is aborted outright at shutdown.
            var feed = Fetch(true);
            if (feed == null) return;
            var have = VersionOf(path);
            if (have != null && have >= feed.Version) return;
            Say("update available: " + feed.Version + " (active next launch)");
            Download(feed, path);
        }

        private static bool Install(string path)
        {
            var feed = Fetch();
            if (feed == null) { Say("feed unreachable"); return false; }
            return Download(feed, path);
        }

        /* Verified before it lands: the bytes are hashed in memory and only written once they match
           the feed's sha256. A wrong hash means the file is discarded, never loaded — this is code
           that will execute inside the game. */
        private static bool Download(FeedEntry feed, string path)
        {
            try
            {
                byte[] dll = Get(feed.Url);
                if (dll == null || dll.Length == 0) { Say("download failed"); return false; }
                string got;
                using (var sha = SHA256.Create())
                    got = BitConverter.ToString(sha.ComputeHash(dll)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(got, feed.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Say("SHA-256 mismatch, discarding (want " + feed.Sha256 + ", got " + got + ")");
                    return false;
                }
                string tmp = path + ".new";
                File.WriteAllBytes(tmp, dll);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                Say("installed " + feed.Version);
                return true;
            }
            catch (Exception e) { Say("install failed: " + e.Message); return false; }
        }

        private sealed class FeedEntry { public Version Version; public string Url; public string Sha256; }

        private static FeedEntry Fetch(bool quiet = false)
        {
            try
            {
                byte[] raw = Get(Feed, quiet);
                if (raw == null) return null;
                string json = System.Text.Encoding.UTF8.GetString(raw);
                // Three flat string fields; a JSON dependency would be the only reason this file
                // could not be dropped into any mod as-is.
                string v = Field(json, "version"), u = Field(json, "url"), s = Field(json, "sha256");
                Version ver;
                if (v == null || u == null || s == null || !Version.TryParse(v.Split('-')[0], out ver)) return null;
                return new FeedEntry { Version = ver, Url = u, Sha256 = s };
            }
            catch { return null; }
        }

        private static string Field(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static byte[] Get(string url, bool quiet = false)
        {
            try
            {
                // Mono's default protocol list predates GitHub dropping everything below TLS 1.2.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = req.ReadWriteTimeout = NetTimeoutMs;
                req.UserAgent = "PrismBootstrap";
                using (var res = req.GetResponse())
                using (var s = res.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[16384];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    return ms.ToArray();
                }
            }
            catch (Exception e) { if (!quiet) Say("GET " + url + " failed: " + e.Message); return null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Say(string msg)
        {
            try { _log("PrismLib bootstrap: " + msg); } catch { }
            Debug.WriteLine("PrismLib bootstrap: " + msg);
        }
    }
}
