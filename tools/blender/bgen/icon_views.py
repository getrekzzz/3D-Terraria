"""How each item generator's inventory icon is framed (P3-38): 'item' lays long weapons and tools on the diagonal,
'side' shows guns, launchers, drills and chainsaws side-on with the barrel right and up, 'object' is the three-quarter
view of everything else. Generators registered with @generator(view=...) give their own. Icons of ICON_POSES
generators are built from a second mesh with extra params (a whip's lash laid out as an S-curve instead of coiled)."""

LONG_ITEMS = {"sword", "shortsword", "pickaxe", "axe", "hammer", "bow", "wand", "staff", "arrow", "rod", "whip",
              "red_wrench", "wire_cutter", "bug_net", "paintbrush", "paint_scraper", "spear", "knife"}
SIDE_ITEMS = {"gun", "drill", "chainsaw", "paint_sprayer"}
ICON_POSES = {"whip": {"pose": "s_curve"}}
# Generators whose business end gets an extra fill light in the icon (socket name).
SPOTLIT = {"spear": "tip"}


def view_for(generator: str, views: dict) -> str:
    if generator in views:
        return views[generator]
    if generator in SIDE_ITEMS:
        return "side"
    return "item" if generator in LONG_ITEMS else "object"
