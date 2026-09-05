using System;
using System.Collections.Generic;
using UnityEngine;
using Sapphire.UI;

namespace Sapphire
{
    // One panel's remembered state. Serialized inside Settings, so public.
    public class PanelStateDto
    {
        public string Id = "";
        public bool Open;
        public float X, Y, W, H;
        public bool HasRect;
    }

    /* Panel layout memory.

       WITHIN a session nothing needed doing: each palette's `_open` flag and its PanelKit
       GameObject are statics that live as long as the game process, so a panel you moved and
       left open is still there after a level reload. What this adds is the choice at the OTHER
       end — by default a relaunch starts clean (the ADOFAI editor is a per-project tool and a
       stale layout from last night is usually noise), and `Settings.PersistPanelLayout` turns
       that reset off.

       Capture is polled rather than hooked: a drag has no single commit point across six
       panels, and a poll every ~2s is far cheaper than wiring DragEnd + resize callbacks into
       each of them. It writes only when something actually moved. */
    internal static class PanelLayout
    {
        private class Entry
        {
            public string Id;
            public Func<bool> IsOpen;
            public Action<bool> SetOpen;
            public Func<PanelKit> Kit;
        }

        private static readonly Entry[] Reg =
        {
            new Entry { Id = "shapelib",   IsOpen = () => EditorShapeLibrary.IsOpen, SetOpen = EditorShapeLibrary.SetOpen, Kit = () => EditorShapeLibrary.Kit },
            new Entry { Id = "levelmenu",  IsOpen = () => EditorLevelMenu.IsOpen,    SetOpen = EditorLevelMenu.SetOpen,    Kit = () => EditorLevelMenu.Kit },
            new Entry { Id = "magicshape", IsOpen = () => EditorMagicShape.IsOpen,   SetOpen = EditorMagicShape.SetOpen,   Kit = () => EditorMagicShape.Kit },
            new Entry { Id = "tracktools", IsOpen = () => EditorTrackTools.IsOpen,   SetOpen = EditorTrackTools.SetOpen,   Kit = () => EditorTrackTools.Kit },
            new Entry { Id = "decotools",  IsOpen = () => EditorDecoTools.IsOpen,    SetOpen = EditorDecoTools.SetOpen,    Kit = () => EditorDecoTools.Kit },
            new Entry { Id = "hztool",     IsOpen = () => EditorHzTool.IsOpen,       SetOpen = EditorHzTool.SetOpen,       Kit = () => EditorHzTool.Kit },
        };

        private static bool _restored;
        private static int _pollCd;
        private static string _lastSig;

        internal static void Tick()
        {
            var s = MainClass.Settings;
            if (s == null) return;
            if (!_restored) { _restored = true; if (s.PersistPanelLayout) Restore(s); }
            if (!s.PersistPanelLayout) return;
            // Never snapshot mid-drag: the rect is in flight and the poll would bank a position
            // the user is still moving away from.
            if (ResizeHandle.Dragging) return;
            if (--_pollCd > 0) return;
            _pollCd = 120;                       // ~2s at 60fps
            Capture(s);
        }

        /* Wipe the stored layout when the user turns the option OFF, so re-enabling it later
           starts from what is on screen NOW rather than resurrecting a months-old arrangement. */
        internal static void OnOptionChanged()
        {
            var s = MainClass.Settings;
            if (s == null) return;
            _pollCd = 0;
            if (!s.PersistPanelLayout) { s.PanelStates = new List<PanelStateDto>(); _lastSig = null; MainClass.SaveSettings(); }
        }

        private static void Restore(Settings s)
        {
            if (s.PanelStates == null) return;
            foreach (var dto in s.PanelStates)
            {
                if (dto == null || string.IsNullOrEmpty(dto.Id)) continue;
                var e = Find(dto.Id);
                if (e == null) continue;         // a panel that no longer exists: drop it
                try
                {
                    if (dto.HasRect) { var k = e.Kit(); if (k != null) k.SetRect(new Rect(dto.X, dto.Y, dto.W, dto.H)); }
                    e.SetOpen(dto.Open);
                }
                catch (Exception ex) { SapphireLog.Log("PanelLayout: restore " + dto.Id + " failed: " + ex.Message); }
            }
            _lastSig = Signature();
        }

        private static void Capture(Settings s)
        {
            string sig = Signature();
            if (sig == _lastSig) return;         // nothing moved — don't rewrite the settings file
            _lastSig = sig;
            var list = new List<PanelStateDto>();
            foreach (var e in Reg)
            {
                var dto = new PanelStateDto { Id = e.Id };
                try
                {
                    dto.Open = e.IsOpen();
                    var k = e.Kit();
                    if (k != null && k.TryGetRect(out Rect r))
                    { dto.HasRect = true; dto.X = r.x; dto.Y = r.y; dto.W = r.width; dto.H = r.height; }
                }
                catch { continue; }
                list.Add(dto);
            }
            s.PanelStates = list;
            MainClass.SaveSettings();
        }

        // Cheap change detector: open flags + rounded rects. Rounding to whole pixels keeps a
        // one-pixel jitter from writing the settings file every poll.
        private static string Signature()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in Reg)
            {
                try
                {
                    sb.Append(e.Id).Append(e.IsOpen() ? '1' : '0');
                    var k = e.Kit();
                    if (k != null && k.TryGetRect(out Rect r))
                        sb.Append((int)r.x).Append(',').Append((int)r.y).Append(',')
                          .Append((int)r.width).Append(',').Append((int)r.height);
                }
                catch { }
                sb.Append(';');
            }
            return sb.ToString();
        }

        private static Entry Find(string id)
        {
            foreach (var e in Reg) if (e.Id == id) return e;
            return null;
        }
    }
}
