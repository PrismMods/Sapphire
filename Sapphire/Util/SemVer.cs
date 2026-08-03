using System;
using System.Globalization;

namespace Sapphire
{
    /* Version ordering for the updater. Sapphire tags look like "v1.0.0-a2": a semver core plus
       an optional prerelease. Ordering is semver's — numeric core first, then a plain release
       outranks any prerelease of the same core, then prerelease identifiers dot-by-dot.

       ONE DELIBERATE DEVIATION from the spec: strict semver compares alphanumeric identifiers
       lexically, which puts "a10" BEFORE "a2" and would make the updater refuse to see a10 as
       newer. Identifiers here split into a letter prefix and a trailing number, compared
       separately — so a1 < a2 < a10, and a* < b* < rc* still falls out of the prefix compare. */
    internal struct SemVer : IComparable<SemVer>
    {
        internal int Major, Minor, Patch;
        internal string Pre;    // "" = plain release
        internal string Raw;

        internal bool IsPrerelease { get { return !string.IsNullOrEmpty(Pre); } }

        internal static bool TryParse(string s, out SemVer v)
        {
            v = new SemVer();
            v.Pre = "";
            v.Raw = "";
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
            v.Raw = s;
            int plus = s.IndexOf('+');              // build metadata never affects precedence
            if (plus >= 0) s = s.Substring(0, plus);
            int dash = s.IndexOf('-');
            string core = dash >= 0 ? s.Substring(0, dash) : s;
            v.Pre = dash >= 0 ? s.Substring(dash + 1) : "";
            string[] parts = core.Split('.');
            if (parts.Length == 0 || parts.Length > 3) return false;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out v.Major)) return false;
            if (parts.Length > 1 &&
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out v.Minor)) return false;
            if (parts.Length > 2 &&
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out v.Patch)) return false;
            return true;
        }

        public int CompareTo(SemVer o)
        {
            int c = Major.CompareTo(o.Major); if (c != 0) return c;
            c = Minor.CompareTo(o.Minor);     if (c != 0) return c;
            c = Patch.CompareTo(o.Patch);     if (c != 0) return c;
            bool a = IsPrerelease, b = o.IsPrerelease;
            if (!a && !b) return 0;
            if (!a) return 1;    // 1.0.0 is newer than 1.0.0-a2
            if (!b) return -1;
            return ComparePre(Pre, o.Pre);
        }

        private static int ComparePre(string x, string y)
        {
            string[] xs = x.Split('.'), ys = y.Split('.');
            int n = Math.Min(xs.Length, ys.Length);
            for (int i = 0; i < n; i++)
            {
                int c = CompareIdent(xs[i], ys[i]);
                if (c != 0) return c;
            }
            return xs.Length.CompareTo(ys.Length);   // more identifiers = higher precedence
        }

        // "a2" → prefix "a", number 2. A missing number sorts before any numbered sibling.
        private static int CompareIdent(string x, string y)
        {
            string xp, yp; long xn, yn;
            SplitIdent(x, out xp, out xn);
            SplitIdent(y, out yp, out yn);
            int c = string.CompareOrdinal(xp, yp);
            if (c != 0) return c < 0 ? -1 : 1;
            return xn.CompareTo(yn);
        }

        private static void SplitIdent(string s, out string prefix, out long num)
        {
            s = s ?? "";
            int i = s.Length;
            while (i > 0 && s[i - 1] >= '0' && s[i - 1] <= '9') i--;
            prefix = s.Substring(0, i);
            num = -1;
            if (i < s.Length) long.TryParse(s.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out num);
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Raw)
                ? Major + "." + Minor + "." + Patch + (IsPrerelease ? "-" + Pre : "")
                : Raw;
        }
    }
}
