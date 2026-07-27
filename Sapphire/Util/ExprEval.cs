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
