#!/usr/bin/env python3
"""Build Sapphire/Resources/SapphireSymbols.ttf — the UI-symbol fallback font.

The user's fonts (and Paperlogy, which Sapphire ships) drop most symbol glyphs: arrows,
geometric shapes like ▲▼●○, ⚙ ♪, ✓ ✕. Without a fallback we control, TMP borrows them from
whatever asset happens to have them — the game's CJK font — whose normalized metrics draw
them tiny and off-baseline, or shows tofu. This subsets DejaVu Sans down to the symbol blocks
Sapphire's UI uses, renames it (the license requires derivatives not carry the original
name), and applies Bismuth's two keycap glyph fixes so a shared key label looks the same in
both mods.

Ported from Bismuth's tools/build-symbol-font.py with three more blocks (Geometric Shapes,
Misc Symbols, Dingbats) — the ones Sapphire's icons and key hints actually draw from.

Usage:  python3 tools/build-symbol-font.py [path/to/DejaVuSans.ttf]
        pip install fonttools; DejaVu: https://unpkg.com/dejavu-fonts-ttf@2.37/ttf/DejaVuSans.ttf
"""
import sys, os
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.boundsPen import BoundsPen

SRC = sys.argv[1] if len(sys.argv) > 1 else "DejaVuSans.ttf"
DST = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   "..", "Sapphire", "Resources", "SapphireSymbols.ttf")

# Whole blocks, not a hand-picked list: a missing codepoint looks like a bug, and the blocks
# are small. Absent codepoints are simply skipped by the subsetter.
RANGES = (list(range(0x2190, 0x2200))     # Arrows
        + list(range(0x2300, 0x2400))     # Misc Technical (⏎ ⇥ ⎵ …)
        + list(range(0x2400, 0x2440))     # Control Pictures (␣ …)
        + list(range(0x25A0, 0x2600))     # Geometric Shapes (▲▼◀▶ ● ○ ■ □)
        + list(range(0x2600, 0x2700))     # Misc Symbols (⚙ ♪ ♫ ☰)
        + list(range(0x2700, 0x27C0)))    # Dingbats (✓ ✕ ❚)

NAMES = [(1, "Sapphire Symbols"), (2, "Regular"),
         (3, "Sapphire Symbols; derived from DejaVu Sans"), (4, "Sapphire Symbols"),
         (6, "SapphireSymbols-Regular"), (16, "Sapphire Symbols"), (17, "Regular")]

# Every glyph Sapphire's UI relies on. The build FAILS if one is missing, so a source-font
# change can never silently hand a symbol back to the game font.
REQUIRED = {
    '↑': 0x2191, '↓': 0x2193, '←': 0x2190, '→': 0x2192,          # key labels, hints
    '▲': 0x25B2, '▼': 0x25BC, '◀': 0x25C0, '▶': 0x25B6,          # steppers, transport
    '●': 0x25CF, '○': 0x25CB, '■': 0x25A0, '□': 0x25A1,          # padlock / state dots
    '⚙': 0x2699, '♪': 0x266A, '☰': 0x2630,                       # settings, hz, menus
    '✓': 0x2713, '✕': 0x2715, '❚': 0x275A,                       # confirm, cancel, bars
    '⇥': 0x21E5, '⎵': 0x23B5, '␣': 0x2423, '⏎': 0x23CE, '↵': 0x21B5, '⇧': 0x21E7,  # keycaps
}


def main():
    opts = subset.Options()
    opts.name_IDs = ['*']          # keep the license/name table
    opts.hinting = False
    opts.desubroutinize = True
    font = subset.load_font(SRC, opts)
    sub = subset.Subsetter(options=opts)
    sub.populate(unicodes=RANGES)
    sub.subset(font)
    subset.save_font(font, DST, opts)

    font = TTFont(DST)
    upem, glyf, cmap = font['head'].unitsPerEm, font['glyf'], font.getBestCmap()
    cap = 0.73 * upem          # DejaVu cap height; the band the letter keys occupy

    # 1. U+2423 OPEN BOX hangs BELOW the baseline in DejaVu, which reads as "too low" on a
    #    keycap. Centre its ink on the cap band instead.
    g = glyf[cmap[0x2423]]
    g.expand(glyf)
    ys = [p[1] for p in g.coordinates]
    g.coordinates.translate((0, round(cap / 2 - (min(ys) + max(ys)) / 2)))
    g.recalcBounds(glyf)

    # 2. U+23B5 BOTTOM SQUARE BRACKET is absent from DejaVu, and it is the symbol a space
    #    key wants: wide and shallow, where the open box is narrow and deep. Drawn here at
    #    DejaVu's stroke weight so it sits beside the borrowed glyphs without looking alien.
    t, W, H = 170, 1700, 560
    x0, x1 = 100, 100 + W
    y0 = round(cap / 2) - H // 2
    y1 = y0 + H
    pen = TTGlyphPen(None)
    for p in [(x0, y1), (x0, y0), (x1, y0), (x1, y1),
              (x1 - t, y1), (x1 - t, y0 + t), (x0 + t, y0 + t), (x0 + t, y1)]:
        (pen.moveTo if p == (x0, y1) else pen.lineTo)(p)
    pen.closePath()
    name = "spacebracket"
    if name not in font.getGlyphOrder():
        font.setGlyphOrder(list(font.getGlyphOrder()) + [name])
    glyf.glyphs[name] = pen.glyph()
    glyf.glyphOrder = font.getGlyphOrder()
    glyf[name].recalcBounds(glyf)
    font['hmtx'].metrics[name] = (W + 2 * x0, x0)
    font['maxp'].numGlyphs = len(font.getGlyphOrder())
    for table in font['cmap'].tables:
        if table.isUnicode():
            table.cmap[0x23B5] = name

    for nid, val in NAMES:
        font['name'].setName(val, nid, 3, 1, 0x409)
        font['name'].setName(val, nid, 1, 0, 0)
    font.save(DST)

    font = TTFont(DST, lazy=True)
    gs, cmap = font.getGlyphSet(), font.getBestCmap()
    print(f"{os.path.getsize(DST) / 1024:.1f} KB, {len(cmap)} codepoints")
    missing = [ch for ch, cp in REQUIRED.items() if cp not in cmap]
    for ch, cp in REQUIRED.items():
        if cp not in cmap:
            continue
        bp = BoundsPen(gs)
        gs[cmap[cp]].draw(bp)
        x0, y0, x1, y1 = bp.bounds
        print(f"  {ch} U+{cp:04X}  y {y0/upem:.2f}..{y1/upem:.2f}")
    if missing:
        print("MISSING: " + " ".join(missing))
        sys.exit(1)
    print("all required glyphs present")


main()
