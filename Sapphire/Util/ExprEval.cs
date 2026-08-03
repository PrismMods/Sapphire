using System;
using System.Globalization;

namespace Sapphire
{
    /* Tiny arithmetic evaluator for editor input fields (quick-chart angle pads, pause/XY
       prompts). Supports + - * / % ^, parentheses, unary +/-, decimals and a few constants
       (pi, tau, e). Recursive-descent; returns false on any malformed input instead of
       throwing, so a half-typed expression never blows up a placement. */
    internal static class ExprEval
    {
        /* Editor input fields accept arithmetic the way vanilla does. Vanilla's
           PropertyControl_Text.Validate tries float.TryParse first and only then falls back to
           evaluating the text, so a plain number never takes the slower/looser path — same order
           here. Vanilla's evaluator is System.Data.DataTable.Compute, which does INTEGER division
           ("10/4" → 2); ExprEval is double throughout, so the same input gives 2.5. That
           divergence is deliberate: the vanilla result is a long-standing gotcha, not a spec.

           Both current and invariant culture are tried because the fields FORMAT with
           ToString("0.###") (culture-sensitive) but much of the codebase parses invariant —
           accepting both means neither locale loses a value it could previously type. */
        internal static bool TryParseDouble(string s, out double d)
        {
            s = (s ?? "").Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out d)) return true;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return true;
            return TryEval(s, out d);
        }

        internal static bool TryParseFloat(string s, out float f)
        {
            double d;
            if (TryParseDouble(s, out d) && d >= float.MinValue && d <= float.MaxValue)
            {
                f = (float)d;
                return true;
            }
            f = 0f;
            return false;
        }

        // Rounds rather than truncates, so "7/2" in an int field lands on 4. AwayFromZero, not
        // .NET's default banker's rounding, so 2.5 and 3.5 don't round in opposite directions.
        internal static bool TryParseInt(string s, out int n)
        {
            double d;
            if (TryParseDouble(s, out d) && d >= int.MinValue && d <= int.MaxValue)
            {
                n = (int)Math.Round(d, MidpointRounding.AwayFromZero);
                return true;
            }
            n = 0;
            return false;
        }

        internal static bool TryEval(string expr, out double result)
        {
            result = 0.0;
            if (string.IsNullOrEmpty(expr)) return false;
            try
            {
                var p = new Parser(expr);
                double v = p.ParseExpr();
                if (!p.AtEnd) return false;                       // trailing junk = malformed
                if (double.IsNaN(v) || double.IsInfinity(v)) return false;
                result = v;
                return true;
            }
            catch { return false; }
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;
            internal Parser(string s) { _s = s; _i = 0; }

            internal bool AtEnd { get { Skip(); return _i >= _s.Length; } }

            private void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
            private char Peek() { Skip(); return _i < _s.Length ? _s[_i] : '\0'; }

            // expr := term (('+'|'-') term)*
            internal double ParseExpr()
            {
                double v = ParseTerm();
                for (;;)
                {
                    char c = Peek();
                    if (c == '+') { _i++; v += ParseTerm(); }
                    else if (c == '-') { _i++; v -= ParseTerm(); }
                    else return v;
                }
            }

            // term := pow (('*'|'/'|'%') pow)*
            private double ParseTerm()
            {
                double v = ParsePow();
                for (;;)
                {
                    char c = Peek();
                    if (c == '*') { _i++; v *= ParsePow(); }
                    else if (c == '/') { _i++; v /= ParsePow(); }
                    else if (c == '%') { _i++; v %= ParsePow(); }
                    else return v;
                }
            }

            // pow := unary ('^' pow)?   (right-associative)
            private double ParsePow()
            {
                double b = ParseUnary();
                if (Peek() == '^') { _i++; return Math.Pow(b, ParsePow()); }
                return b;
            }

            // unary := ('+'|'-') unary | primary
            private double ParseUnary()
            {
                char c = Peek();
                if (c == '-') { _i++; return -ParseUnary(); }
                if (c == '+') { _i++; return ParseUnary(); }
                return ParsePrimary();
            }

            // primary := '(' expr ')' | ident | number
            private double ParsePrimary()
            {
                char c = Peek();
                if (c == '(')
                {
                    _i++;
                    double v = ParseExpr();
                    if (Peek() != ')') throw new FormatException();
                    _i++;
                    return v;
                }
                if (char.IsLetter(c)) return ParseIdent();
                return ParseNumber();
            }

            private double ParseNumber()
            {
                Skip();
                int start = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                if (_i == start) throw new FormatException();
                return double.Parse(_s.Substring(start, _i - start), CultureInfo.InvariantCulture);
            }

            private double ParseIdent()
            {
                Skip();
                int start = _i;
                while (_i < _s.Length && char.IsLetter(_s[_i])) _i++;
                switch (_s.Substring(start, _i - start).ToLowerInvariant())
                {
                    case "pi": return Math.PI;
                    case "tau": return Math.PI * 2.0;
                    case "e": return Math.E;
                    default: throw new FormatException();
                }
            }
        }
    }
}
