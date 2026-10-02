"""Reference items of the boss loot work (treasure bags, trophies, boss drops), kept apart so parallel branches do not
collide on items.py. Same row format: id|name|stage|source."""

NOTABLE = {
    "consumable": """
treasure_bag|Treasure Bag|early|boss (expert)
""",
    "armor": """
skeletron_mask|Skeletron Mask|early|skeletron
""",
    "accessory": """
bone_glove|Bone Glove|early|skeletron (expert)
""",
    "tool": """
skeletron_hand|Skeletron Hand|early|skeletron
clothier_voodoo_doll|Clothier Voodoo Doll|early|clothier
""",
    "furniture": """
king_slime_trophy|King Slime Trophy|early|king slime
eye_of_cthulhu_trophy|Eye of Cthulhu Trophy|early|eye of cthulhu
eater_of_worlds_trophy|Eater of Worlds Trophy|early|eater of worlds
brain_of_cthulhu_trophy|Brain of Cthulhu Trophy|early|brain of cthulhu
queen_bee_trophy|Queen Bee Trophy|early|queen bee
skeletron_trophy|Skeletron Trophy|early|skeletron
deerclops_trophy|Deerclops Trophy|early|deerclops
wall_of_flesh_trophy|Wall of Flesh Trophy|early|wall of flesh
""",
}
