"""Pixel maps for the HUD and inventory sprites (vitals, defence, trash, slot silhouettes), drawn by build_ui.py.

A map is rows of palette letters ('.' is transparent). build_ui.py draws each cell as 2x2 pixels and adds a 1 px
OUTLINE ring (alpha dilation), so a w x h map becomes a (2w + 2) x (2h + 2) PNG; maps listed in PLAIN keep their size
(2w x 2h) and get no outline (silhouettes, the pointer and frames carry their own edges). ui_sprites_b.py holds the
buttons, pointer, hints and frames; both share PALETTE so every sprite uses the same colours and outline.
"""

OUTLINE = "#1a1020"

# One palette for every UI sprite and glyph (lowercase letters only: ui_glyphs.py adds X/H/S tones of each icon's fg).
PALETTE = {
    "k": OUTLINE,     # outline dark
    "w": "#ffffff",   # white
    "l": "#d8dce8",   # light grey
    "g": "#9aa0b4",   # grey
    "d": "#5c6278",   # dark grey
    "r": "#e8283c",   # red
    "m": "#9c1428",   # maroon
    "p": "#ff8c9c",   # pink highlight
    "y": "#ffd030",   # gold
    "o": "#b87a10",   # dark gold
    "q": "#fff4b0",   # pale gold
    "b": "#4a8cff",   # blue
    "a": "#2048b0",   # dark blue
    "c": "#b4e4ff",   # pale cyan
    "n": "#a0662e",   # brown
    "t": "#5e3618",   # dark brown
    "u": "#d8a868",   # tan
    "e": "#5ccc3c",   # green
    "f": "#2a7a22",   # dark green
    "v": "#a060e8",   # violet
    "i": "#602c9c",   # dark violet
    "x": "#ff8a20",   # orange
    "z": "#b04a10",   # dark orange
    "s": "#c8d0f0",   # slot silhouette (drawn translucent by the slot)
}

# Colour swaps that derive one sprite from another (name -> (source, {letter: letter})).
SWAPS = {
    "heart_gold": ("heart", {"r": "y", "m": "o", "p": "q"}),
}

SPRITES = {
    # --- Vitals (11x11 hearts and stars, drawn at 22 px plus the outline; 9x9 bubbles) ---
    "heart": """
        .rrr...rrr.
        rrpwr.rrrrr
        rpwprrrrrrr
        rpprrrrrrrr
        rrrrrrrrrrm
        rrrrrrrrrrm
        .rrrrrrrrm.
        ..rrrrrrm..
        ...rrrrm...
        ....rrm....
        .....m.....
    """,
    "star": """
        .....b.....
        ....bcb....
        ....bcb....
        bbbbbcbbbba
        .bbcccccba.
        ..bccwcba..
        ...bcccb...
        ..bbbbbba..
        ..bba.bba..
        .bba...baa.
        .a.......a.
    """,
    "bubble": """
        ..bbbbb..
        .bwwcccb.
        bwwcccccb
        bwcccccca
        bccccccca
        bccccccca
        bccccccaa
        .bcccaaa.
        ..aaaaa..
    """,
    # --- Inventory ---
    "shield": """
        .llllllggg.
        lwlllllgggd
        llllllggggd
        llllllggggd
        llllllggggd
        ggggggddddd
        ggggggddddd
        .gggggdddd.
        .gggggdddd.
        ..ggggddd..
        ...gggdd...
        ....gdd....
    """,
    "trash": """
        ....ddd....
        .dlllllllg.
        dllllllllgd
        .ddddddddd.
        .dlglglgld.
        .dlglglgld.
        .dlglglgld.
        .dlglglgld.
        .dlglglgld.
        .dlglglgld.
        ..ldldldl..
        ..ddddddd..
    """,
    "favourite_star": """
        ...y...
        ..yqy..
        yyyqyyy
        .yqqqo.
        ..yyo..
        .yo.yo.
        .o...o.
    """,
    # --- Empty-slot silhouettes (12x12, one tone, no outline) ---
    "slot_head": """
        ............
        ....ssss....
        ..ssssssss..
        .ssssssssss.
        .ssssssssss.
        .ssssssssss.
        .sss....sss.
        .ss......ss.
        .ss......ss.
        .sss....sss.
        ..s......s..
        ............
    """,
    "slot_body": """
        ............
        ..sss..sss..
        .ssssssssss.
        ssssssssssss
        ssssssssssss
        ss.ssssss.ss
        ...ssssss...
        ...ssssss...
        ...ssssss...
        ...ssssss...
        ...ssssss...
        ............
    """,
    "slot_legs": """
        ............
        ..ssssssss..
        ..ssssssss..
        ..ssssssss..
        ..sss..sss..
        ..sss..sss..
        ..sss..sss..
        ..sss..sss..
        ..sss..sss..
        .ssss..ssss.
        .ssss..ssss.
        ............
    """,
    "slot_accessory": """
        .....ss.....
        ....ssss....
        .....ss.....
        ...ssssss...
        ..ss....ss..
        .ss......ss.
        .ss......ss.
        .ss......ss.
        .ss......ss.
        ..ss....ss..
        ...ssssss...
        ............
    """,
    "slot_dye": """
        ....ssss....
        .....ss.....
        .....ss.....
        ....ssss....
        ...ssssss...
        ..ssssssss..
        ..ssssssss..
        ..ssssssss..
        ..ssssssss..
        ..ssssssss..
        ...ssssss...
        ............
    """,
    "slot_pet": """
        ............
        ...ss..ss...
        ...ss..ss...
        .ss......ss.
        .ss......ss.
        ............
        ....ssss....
        ...ssssss...
        ..ssssssss..
        ..ssssssss..
        ...ss..ss...
        ............
    """,
    "slot_light_pet": """
        .....ss.....
        .s..ssss..s.
        ...ssssss...
        ..ssssssss..
        s.ssssssss.s
        s.ssssssss.s
        ..ssssssss..
        ...ssssss...
        .s..ssss..s.
        .....ss.....
        ............
        ............
    """,
    "slot_minecart": """
        ............
        ............
        s..........s
        ssssssssssss
        .ssssssssss.
        .ssssssssss.
        .ssssssssss.
        ..ssssssss..
        ..ss....ss..
        .ssss..ssss.
        ..ss....ss..
        ............
    """,
    "slot_mount": """
        ............
        ..ss....ss..
        .sss....sss.
        .ss......ss.
        ss........ss
        ss........ss
        ss........ss
        sss......sss
        .sss....sss.
        ..ssssssss..
        ....ssss....
        ............
    """,
    "slot_hook": """
        .....ss.....
        ....s..s....
        .....ss.....
        .....ss.....
        .....ss.....
        .s...ss...s.
        .ss..ss..ss.
        ..ss.ss.ss..
        ...ssssss...
        ....ssss....
        ............
        ............
    """,
    "slot_coin": """
        ....ssss....
        ..ssssssss..
        .sss....sss.
        .ss..ss..ss.
        ss..ssss..ss
        ss..ssss..ss
        .ss..ss..ss.
        .sss....sss.
        ..ssssssss..
        ....ssss....
        ............
        ............
    """,
    "slot_ammo": """
        .....ss.....
        ....ssss....
        ...ssssss...
        .....ss.....
        .....ss.....
        .....ss.....
        .....ss.....
        .....ss.....
        ....ssss....
        ...ss..ss...
        ..ss....ss..
        ............
    """,
}

PLAIN = {name for name in SPRITES if name.startswith("slot_")}
