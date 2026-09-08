using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Sapphire
{
    internal enum UpdateStatus { Idle, Checking, UpToDate, Available, Installing, Installed, Failed }

    internal sealed class UpdateInfo
    {
        internal string Tag;          // "v1.0.0-a3"
        internal string Name;         // release title, minus the redundant tag prefix
        internal SemVer Version;
        internal string PageUrl;
        internal string AssetUrl;
        internal string AssetSha256;  // from GitHub's asset digest; null when absent
        internal string Notes;        // release body (GitHub markdown); null/empty when absent
    }

    /* GitHub-releases self-updater. Runs entirely on a worker thread and touches NO Unity API —
       every field here is plain state that UpdateToast / PageUpdates poll from the main thread on
       their own Tick. That's deliberate: marshalling callbacks back onto Unity's thread is the
       usual source of "works in editor, crashes in build" in mods, and polling costs nothing at
       the once-per-frame rate the UI already runs at.

       Reads are lock-free on purpose — Status is the single volatile gate, and it is written
       LAST by the worker, after Available/Message/Progress are already in place, so a reader
       that sees a new Status also sees the payload that goes with it. */
    internal static class UpdateService
    {
        private const string Owner = "PrismMods", Repo = "Sapphire";
        private const string ApiUrl = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases?per_page=30";
        internal const string ReleasesPage = "https://github.com/" + Owner + "/" + Repo + "/releases";

        private static volatile UpdateStatus _status = UpdateStatus.Idle;
        internal static UpdateStatus Status { get { return _status; } }

        internal static UpdateInfo Available { get; private set; }
        internal static string Message { get; private set; }
        // 0..1 while Installing, negative when not downloading.
        internal static float Progress { get; private set; }
        private static Thread _worker;

        internal static bool Busy
        {
            get { return _status == UpdateStatus.Checking || _status == UpdateStatus.Installing; }
        }

        internal static SemVer Current
        {
            get
            {
                SemVer v;
                return SemVer.TryParse(MainClass.ModVersion, out v) ? v : new SemVer();
            }
        }

        private static void Set(UpdateStatus s, string msg)
        {
            Message = msg ?? "";
            _status = s;   // written last — see the class comment
        }

        // ── check ────────────────────────────────────────────────────────────

        /* True when the current operation was user-initiated, so the toast knows whether a
           failure is worth interrupting for. A background check that fails because the player is
           offline should stay silent (the settings page still shows it); a check or install they
           clicked deserves an answer either way. */
        internal static bool AnnounceFailures { get; private set; }

        internal static void Check(bool manual)
        {
            if (Busy) return;
            if (_status == UpdateStatus.Installed) return;   // already staged; nothing to re-check
            AnnounceFailures = manual;
            StartWorker(() =>
            {
                Progress = -1f;
                Set(UpdateStatus.Checking, "");
                try
                {
                    UpdateInfo best = FetchLatest();
                    if (best == null)
                    {
                        Available = null;
                        Set(UpdateStatus.UpToDate, "");
                    }
                    else
                    {
                        Available = best;
                        Set(UpdateStatus.Available, "");
                    }
                }
                catch (Exception ex)
                {
                    Available = null;
                    Set(UpdateStatus.Failed, Describe(ex));
                    SapphireLog.Log("[update] check failed: " + ex);
                }
            });
        }

        private static string Describe(Exception ex)
        {
            var we = ex as WebException;
            if (we != null)
            {
                var resp = we.Response as HttpWebResponse;
                if (resp != null)
                {
                    int code = (int)resp.StatusCode;
                    if (code == 403 || code == 429) return Loc.T("GitHub rate limit — try again later");
                    if (code == 404) return Loc.T("Release feed not found");
                    return "HTTP " + code;
                }
                return Loc.T("No connection");
            }
            return ex.Message;
        }

        private static UpdateInfo FetchLatest()
        {
            string json = HttpGetString(ApiUrl);
            JArray releases = JArray.Parse(json);
            SemVer current = Current;
            bool allowPre = MainClass.Settings == null || MainClass.Settings.UpdateIncludePrerelease;
            string skipped = MainClass.Settings != null ? (MainClass.Settings.SkippedUpdateTag ?? "") : "";

            UpdateInfo best = null;
            foreach (JToken rel in releases)
            {
                if ((bool?)rel["draft"] == true) continue;
                string tag = (string)rel["tag_name"];
                if (string.IsNullOrEmpty(tag) || tag == skipped) continue;
                SemVer v;
                if (!SemVer.TryParse(tag, out v)) continue;
                if (v.IsPrerelease && !allowPre) continue;
                if (v.CompareTo(current) <= 0) continue;
                if (best != null && v.CompareTo(best.Version) <= 0) continue;

                string url = null, sha = null;
                var assets = rel["assets"] as JArray;
                if (assets != null)
                {
                    foreach (JToken a in assets)
                    {
                        string name = (string)a["name"];
                        // release.sh names the zip after the version (Sapphire-1.0.0-a2.zip), so
                        // match the shape rather than a fixed filename.
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!name.StartsWith("Sapphire", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                        url = (string)a["browser_download_url"];
                        sha = ParseSha256((string)a["digest"]);
                        break;
                    }
                }
                if (url == null) continue;   // a release with no installable zip is not an update

                best = new UpdateInfo
                {
                    Tag = tag,
                    Name = CleanReleaseName((string)rel["name"], tag),
                    Version = v,
                    PageUrl = (string)rel["html_url"],
                    AssetUrl = url,
                    AssetSha256 = sha,
                    Notes = (string)rel["body"],
                };
            }
            return best;
        }

        private static string CleanReleaseName(string title, string tag)
        {
            if (string.IsNullOrEmpty(title)) return null;
            string n = title.Trim();
            if (!string.IsNullOrEmpty(tag) && n.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
                n = n.Substring(tag.Length);
            n = n.Trim(' ', '\t', '—', '–', '-', ':', '|');
            // A title that's only the tag/version adds nothing the toast doesn't already show.
            return n.Length == 0 || n.Equals(tag, StringComparison.OrdinalIgnoreCase) ? null : n;
        }

        private static string ParseSha256(string digest)
        {
            const string p = "sha256:";
            if (string.IsNullOrEmpty(digest) || !digest.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return null;
            string hex = digest.Substring(p.Length);
            return hex.Length == 64 ? hex.ToLowerInvariant() : null;
        }

        // ── install ──────────────────────────────────────────────────────────

        internal static void Install()
        {
            UpdateInfo info = Available;
            if (info == null || Busy) return;
            AnnounceFailures = true;   // always user-initiated
            StartWorker(() =>
            {
                Progress = 0f;
                Set(UpdateStatus.Installing, "");
                try
                {
                    DoInstall(info);
                    Available = null;
                    Progress = -1f;
                    Set(UpdateStatus.Installed, info.Tag);
                    SapphireLog.Log("[update] installed " + info.Tag + " — restart to apply");
                }
                catch (Exception ex)
                {
                    Progress = -1f;
                    Set(UpdateStatus.Failed, ex.Message);
                    SapphireLog.Log("[update] install failed: " + ex);
                }
            });
        }

        private static void DoInstall(UpdateInfo info)
        {
            // release.sh builds the zip with a single "Sapphire/" folder at its root, so the
            // extract root is the MODS directory (our own folder's parent), not our folder.
            string modDir = MainClass.ModPath;
            if (string.IsNullOrEmpty(modDir)) throw new Exception("mod path unknown");
            string modsRoot = Path.GetDirectoryName(modDir.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(modsRoot)) throw new Exception("could not resolve the mods folder");

            string staging = Path.Combine(Path.GetTempPath(), "SapphireUpdate");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            string zip = Path.Combine(staging, "Sapphire.zip");
            try
            {
                Download(info.AssetUrl, zip);
                VerifyChecksum(zip, info);
                ExtractOver(zip, modsRoot);
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }
        }

        private static void VerifyChecksum(string path, UpdateInfo info)
        {
            if (string.IsNullOrEmpty(info.AssetSha256))
            {
                SapphireLog.Log("[update] no digest published for " + info.Tag + " — integrity unverified");
                return;
            }
            string actual;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(actual, info.AssetSha256, StringComparison.OrdinalIgnoreCase))
                throw new Exception("checksum mismatch — download rejected");
        }

        private static void ExtractOver(string zipPath, string root)
        {
            string rootFull = Path.GetFullPath(root);
            string prefix = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootFull : rootFull + Path.DirectorySeparatorChar;
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;   // directory entry
                    string dest = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    // Zip-slip guard: an entry like "../../evil.dll" would otherwise write
                    // anywhere on disk. Never trust a path out of an archive.
                    if (!dest.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        SapphireLog.Log("[update] skipped suspicious zip entry: " + entry.FullName);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    string tmp = dest + ".sapnew";
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    entry.ExtractToFile(tmp, true);
                    ReplaceFile(tmp, dest);
                }
            }
        }

        /* The running Sapphire.dll is one of the files being replaced. Mono on macOS/Linux lets
           you unlink a mapped file, so the delete usually succeeds; when it doesn't (Windows,
           or an AV holding a handle) fall back to renaming the live file aside so the move can
           still land. The .old husk is harmless — UMM loads by name from Info.json. */
        private static void ReplaceFile(string src, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            if (File.Exists(dest))
            {
                try { File.Delete(dest); }
                catch
                {
                    string old = dest + ".old";
                    try { if (File.Exists(old)) File.Delete(old); } catch { }
                    File.Move(dest, old);
                }
            }
            File.Move(src, dest);
        }

        internal static void Skip()
        {
            UpdateInfo info = Available;
            if (info == null || MainClass.Settings == null) return;
            MainClass.Settings.SkippedUpdateTag = info.Tag;
            MainClass.SaveSettings();
            Available = null;
            Set(UpdateStatus.UpToDate, "");
        }

        internal static void ClearSkip()
        {
            if (MainClass.Settings == null) return;
            MainClass.Settings.SkippedUpdateTag = "";
            MainClass.SaveSettings();
            Check(true);
        }

        // ── plumbing ─────────────────────────────────────────────────────────

        private static void StartWorker(Action body)
        {
            if (_worker != null && _worker.IsAlive) return;
            _worker = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex)
                {
                    Progress = -1f;
                    Set(UpdateStatus.Failed, ex.Message);
                    SapphireLog.Log("[update] worker crashed: " + ex);
                }
            });
            _worker.IsBackground = true;   // never keep the game alive on quit
            _worker.Start();
        }

        private static HttpWebRequest NewRequest(string url)
        {
            // Unity's Mono defaults to TLS 1.0/1.1, which GitHub refuses outright.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Sapphire-Updater";       // GitHub 403s a missing User-Agent
            req.Timeout = 20000;
            req.ReadWriteTimeout = 30000;
            return req;
        }

        private static string HttpGetString(string url)
        {
            var req = NewRequest(url);
            req.Accept = "application/vnd.github+json";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static void Download(string url, string path)
        {
            var req = NewRequest(url);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long total = resp.ContentLength;
                byte[] buf = new byte[64 * 1024];
                long done = 0;
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    done += n;
                    // No length header (chunked) → leave the bar indeterminate rather than lie.
                    Progress = total > 0 ? (float)((double)done / total) : -1f;
                }
            }
        }
    }
}
