using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sapphire
{
    // Per-frame key polling for rebind flows. Active flag is flipped by the caller; fires
    // once per key-down. (Sapphire's version also consults the game's async press list for
    // Proton setups; Sapphire polls legacy input only.)
    internal class KeyListener : MonoBehaviour
    {
        public bool Active;
        public Action<KeyCode> OnKey;

        private static readonly KeyCode[] Watched = BuildWatched();

        private static KeyCode[] BuildWatched()
        {
            var list = new List<KeyCode>();
            for (int k = (int)KeyCode.A; k <= (int)KeyCode.Z; k++) list.Add((KeyCode)k);
            for (int k = (int)KeyCode.Alpha0; k <= (int)KeyCode.Alpha9; k++) list.Add((KeyCode)k);
            for (int k = (int)KeyCode.F1; k <= (int)KeyCode.F12; k++) list.Add((KeyCode)k);
            list.AddRange(new[]
            {
                KeyCode.LeftShift, KeyCode.RightShift,
                KeyCode.LeftControl, KeyCode.RightControl,
                KeyCode.LeftAlt, KeyCode.RightAlt,
                KeyCode.LeftCommand, KeyCode.RightCommand,
                KeyCode.Space, KeyCode.Return, KeyCode.Tab, KeyCode.CapsLock, KeyCode.Backspace,
                KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.Backslash,
                KeyCode.Semicolon, KeyCode.Quote, KeyCode.Comma, KeyCode.Period, KeyCode.Slash,
                KeyCode.BackQuote, KeyCode.Minus, KeyCode.Equals,
                KeyCode.Insert, KeyCode.Delete, KeyCode.Home, KeyCode.End,
                KeyCode.PageUp, KeyCode.PageDown,
            });
            return list.ToArray();
        }

        private void Update()
        {
            if (!Active || OnKey == null) return;
            for (int i = 0; i < Watched.Length; i++)
            {
                var k = Watched[i];
                if (Input.GetKeyDown(k))
                {
                    OnKey(k);
                    return;
                }
            }
        }
    }
}
