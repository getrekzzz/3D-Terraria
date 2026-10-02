"""Pixel maps for UI buttons, the pointer, interaction hints and frames (see ui_sprites.py for the format)."""

SPRITES = {
    # --- Buttons ---
    "eye_open": """
        ...www...
        .wwlllww.
        wllbbbllw
        wlbbkbblw
        wllbbbllw
        .wwlllww.
        ...www...
    """,
    "eye_closed": """
        ...ggg..d
        .ggllldd.
        glldddllg
        gldddkdlg
        gddgggllg
        .dglllgg.
        d..ggg...
    """,
    "sort": """
        .........y..
        .llllll..y..
        .gggggg..y..
        .........y..
        .lllll...y..
        .ggggg...y..
        .........y..
        .llll..yyyyy
        .gggg...yyy.
        .........y..
        .ll.........
        .gg.........
    """,
    "quick_stack": """
        .....ee.....
        ...e.ee.e...
        ....eeee....
        .....ee.....
        ............
        .nnnnnnnnnn.
        .nuuuuuuuun.
        .ttttyyttttt
        .nuuuyyuuun.
        .nuuuuuuuun.
        .nnnnnnnnnn.
        ............
    """,
    "deposit": """
        .....bb.....
        .....bb.....
        ...bbbbbb...
        ....bbbb....
        .....bb.....
        .nnnnnnnnnn.
        .nuuuuuuuun.
        .ttttyyttttt
        .nuuuyyuuun.
        .nuuuuuuuun.
        .nnnnnnnnnn.
        ............
    """,
    "loot": """
        .....yy.....
        ....yyyy....
        ...yyyyyy...
        .....yy.....
        .....yy.....
        .nnnnnnnnnn.
        .nuuuuuuuun.
        .ttttyyttttt
        .nuuuyyuuun.
        .nuuuuuuuun.
        .nnnnnnnnnn.
        ............
    """,
    "reforge_hammer": """
        ............
        ..gggggggg..
        .gllllllllg.
        .glwllllllgd
        .gllllllllgd
        ..ddddnndddd
        .....nn.....
        .....nn.....
        .....nn.....
        .....nn.....
        .....tn.....
        .....tt.....
    """,
    "toggle_on": """
        ........llll..
        .eeeeeelwwllg.
        eeeeeeelwlllg.
        eeeeeeellllllg
        eeeeeeellllllg
        eeeeeeelllllgg
        .fffffffllggg.
        ........gggg..
    """,
    "toggle_off": """
        ..llll........
        .lwwllgddddd..
        lwlllllgddddd.
        llllllldddddd.
        llllllldddddd.
        llllllgdddddd.
        .llgggdddddd..
        ..gggg........
    """,
    "tab_equipment": """
        ............
        ..ggg..ggg..
        .glllggllgd.
        glwllllllldd
        gllllllllldd
        gd.glllgd.dd
        ...glllgd...
        ...glllgd...
        ...ggggdd...
        ...glllgd...
        ...gddddd...
        ............
    """,
    "tab_misc": """
        ............
        ............
        n..........n
        nnnnnnnnnnnn
        .nuuuuuuuun.
        .nuuuuuuuun.
        .nuuuuuuuun.
        ..tttttttt..
        ..dd....dd..
        .dggd..dggd.
        ..dd....dd..
        ............
    """,
    "tab_housing": """
        ............
        .....tt.....
        ....tnnt....
        ...tnnnnt...
        ..tnnnnnnt..
        .tnnnnnnnnt.
        tttttttttttt
        .uuuuuuuuuu.
        .uccuuuttuu.
        .uccuuuttuu.
        .uuuuuuttuu.
        .dddddddddd.
    """,
    # --- Pointer (no outline: the 'k' cells are its 2 px dark border; white is tinted at runtime) ---
    "cursor": """
        kk.......
        kwk......
        kwwk.....
        kwwwk....
        kwwwwk...
        kwwwwwk..
        kwwwlllk.
        kwlllkkkk
        klkkk....
        kk.......
    """,
    "smart_cursor": """
        yy.......
        ywy......
        ywwy.....
        ywwwy....
        ywwwwy...
        ywwwwwy..
        ywwwqqqy.
        ywqqqyyyy
        yqyyy....
        yy.......
    """,
    # --- Interaction hints ---
    "talk_hint": """
        .wwwwwwwww.
        wwwwwwwwwww
        wwwwwwwwwww
        wwdwwdwwdwl
        wwwwwwwwwwl
        .wllllllll.
        ..wl.......
        .wl........
        w..........
    """,
    "open_hint": """
        ....u.u...
        ..u.u.u...
        ..u.u.u.u.
        ..u.u.u.u.
        ..uuuuuuu.
        u.uuuuuuu.
        uuuuuuuun.
        .uuuuuuun.
        .uuuuuuun.
        ..uuuuunn.
        ...uuunn..
    """,
    # --- Minimap zoom buttons (8x8 cells = 18 px with the outline) ---
    "zoom_in": """
        .aaaaaa.
        abbbbbba
        abbwwbba
        abwwwwba
        abwwwwba
        abbwwbba
        abbbbbba
        .aaaaaa.
    """,
    "zoom_out": """
        .aaaaaa.
        abbbbbba
        abbbbbba
        abwwwwba
        abwwwwba
        abbbbbba
        abbbbbba
        .aaaaaa.
    """,
    # --- Menu row icons (15x15 cells = 32 px with the outline) ---
    "menu_play": """
        ...............
        ...............
        ..ff...........
        ..feff.........
        ..fweeff.......
        ..fweeeeff.....
        ..fweeeeeeff...
        ..fweeeeeeeef..
        ..feeeeeeeff...
        ..feeeeeff.....
        ..feeeff.......
        ..feff.........
        ..ff...........
        ...............
        ...............
    """,
    "menu_delete": """
        ......ddd......
        .....d...d.....
        .ddddddddddddd.
        .dllllllllllld.
        .ddddddddddddd.
        ..dlglglglgld..
        ..dlglglglgld..
        ..dlglglglgld..
        ..dlglglglgld..
        ..dlglglglgld..
        ..dlglglglgld..
        ..dlglglglgld..
        ..dlglglglgld..
        ..dgggggggggd..
        ...ddddddddd...
    """,
    "menu_favourite": """
        .......o.......
        ......oyo......
        ......oyo......
        .....oyqyo.....
        ooooooyqyoooooo
        .oyyyyyqyyyyyo.
        ..oyyyyyyyyyo..
        ...oyyyyyyyo...
        ....oyyyyyo....
        ....oyyyyyo....
        ...oyyyoyyyo...
        ...oyyo.oyyo...
        ..oyyo...oyyo..
        ..oyo.....oyo..
        ..oo.......oo..
    """,
    # --- Settings slider knob (6x10 cells = 14x22 px) ---
    "slider_grabber": """
        .oooo.
        oyqqyo
        oyqyyo
        oyyyyo
        oyyyyo
        oyyyyo
        oyyyyo
        oyyyyo
        ozyyzo
        .oooo.
    """,
    # --- Frames (no outline; 9-slice with 4-cell = 8 px margins) ---
    "minimap_frame": """
        kkkkkkkkkkkk
        kqyyyyyyyyqk
        kyooooooooyk
        kyokkkkkkoyk
        kyok....koyk
        kyok....koyk
        kyok....koyk
        kyok....koyk
        kyokkkkkkoyk
        kyooooooooyk
        kqyyyyyyyyqk
        kkkkkkkkkkkk
    """,
}

PLAIN = {"cursor", "smart_cursor", "minimap_frame"}

# Colour swaps that derive one sprite from another (name -> (source, {letter: letter})), as in ui_sprites.py.
SWAPS = {
    "menu_favourite_off": ("menu_favourite", {"y": "g", "o": "d", "q": "l"}),
    "slider_grabber_hover": ("slider_grabber", {"y": "q", "q": "w", "z": "o"}),
}
