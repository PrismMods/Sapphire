using UnityEngine;

namespace Sapphire.UI.Pages
{
    // Shared key-token helpers. Mirrors the friendly-name table in KeyViewer.Keys.cs so
    // tokens round-trip identically between the editor UI and the runtime parser.
    internal static class KeyTokens
    {
        public static string TokenFromKeyCode(KeyCode kc)
        {
            switch (kc)
            {
                case KeyCode.Tab:          return "Tab";
                case KeyCode.CapsLock:     return "Caps";
                case KeyCode.Space:        return "Space";
                case KeyCode.Return:       return "Enter";
                case KeyCode.Backspace:    return "Backspace";
                case KeyCode.Escape:       return "Escape";
                case KeyCode.LeftShift:    return "LShift";
                case KeyCode.RightShift:   return "RShift";
                case KeyCode.LeftControl:  return "LCtrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt:      return "LAlt";
                case KeyCode.RightAlt:     return "RAlt";
                case KeyCode.LeftCommand:  return "LCmd";
                case KeyCode.RightCommand: return "RCmd";
                case KeyCode.UpArrow:      return "Up";
                case KeyCode.DownArrow:    return "Down";
                case KeyCode.LeftArrow:    return "Left";
                case KeyCode.RightArrow:   return "Right";
                case KeyCode.LeftBracket:  return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Backslash:    return "\\";
                case KeyCode.Semicolon:    return ";";
                case KeyCode.Quote:        return "'";
                case KeyCode.Comma:        return ",";
                case KeyCode.Period:       return ".";
                case KeyCode.Slash:        return "/";
                case KeyCode.BackQuote:    return "`";
                case KeyCode.Minus:        return "-";
                case KeyCode.Equals:       return "=";
            }
            if (kc >= KeyCode.Alpha0 && kc <= KeyCode.Alpha9)
                return ((int)(kc - KeyCode.Alpha0)).ToString();
            return kc.ToString();
        }

        // Reverse of TokenFromKeyCode (inlined from Bismuth's KeyViewer parser).
        public static bool TryParseKey(string token, out KeyCode kc)
        {
            kc = KeyCode.None;
            if (string.IsNullOrEmpty(token)) return false;
            switch (token)
            {
                case "Tab":       kc = KeyCode.Tab; return true;
                case "Caps":      kc = KeyCode.CapsLock; return true;
                case "Space":     kc = KeyCode.Space; return true;
                case "Enter":     kc = KeyCode.Return; return true;
                case "Backspace": kc = KeyCode.Backspace; return true;
                case "Escape":    kc = KeyCode.Escape; return true;
                case "LShift":    kc = KeyCode.LeftShift; return true;
                case "RShift":    kc = KeyCode.RightShift; return true;
                case "LCtrl":     kc = KeyCode.LeftControl; return true;
                case "RCtrl":     kc = KeyCode.RightControl; return true;
                case "LAlt":      kc = KeyCode.LeftAlt; return true;
                case "RAlt":      kc = KeyCode.RightAlt; return true;
                case "LCmd":      kc = KeyCode.LeftCommand; return true;
                case "RCmd":      kc = KeyCode.RightCommand; return true;
                case "Up":        kc = KeyCode.UpArrow; return true;
                case "Down":      kc = KeyCode.DownArrow; return true;
                case "Left":      kc = KeyCode.LeftArrow; return true;
                case "Right":     kc = KeyCode.RightArrow; return true;
                case "[":         kc = KeyCode.LeftBracket; return true;
                case "]":         kc = KeyCode.RightBracket; return true;
                case "\\":        kc = KeyCode.Backslash; return true;
                case ";":         kc = KeyCode.Semicolon; return true;
                case "'":         kc = KeyCode.Quote; return true;
                case ",":         kc = KeyCode.Comma; return true;
                case ".":         kc = KeyCode.Period; return true;
                case "/":         kc = KeyCode.Slash; return true;
                case "`":         kc = KeyCode.BackQuote; return true;
                case "-":         kc = KeyCode.Minus; return true;
                case "=":         kc = KeyCode.Equals; return true;
            }
            if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
            {
                kc = KeyCode.Alpha0 + (token[0] - '0');
                return true;
            }
            return System.Enum.TryParse(token, true, out kc);
        }

        public static string PrettyTokenLabel(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            // Special-token passthrough (KPS / Total are not real keys).
            if (token == "KPS" || token == "Total") return token;
            if (!TryParseKey(token, out KeyCode kc)) return token;
            switch (kc)
            {
                case KeyCode.LeftShift:    return "LShift";
                case KeyCode.RightShift:   return "RShift";
                case KeyCode.LeftControl:  return "LCtrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt:      return "LAlt";
                case KeyCode.RightAlt:     return "RAlt";
                case KeyCode.LeftCommand:  return "LCmd";
                case KeyCode.RightCommand: return "RCmd";
                case KeyCode.CapsLock:     return "Caps";
                case KeyCode.Return:       return "Enter";
                case KeyCode.Backspace:    return "Back";
                case KeyCode.Escape:       return "Esc";
                case KeyCode.UpArrow:      return "↑";
                case KeyCode.DownArrow:    return "↓";
                case KeyCode.LeftArrow:    return "←";
                case KeyCode.RightArrow:   return "→";
                default:                   return token;
            }
        }
    }
}
