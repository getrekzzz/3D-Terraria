"""Reference items. Families are generated; notable items are listed per category as 'id|Name|stage|source'."""

PRE_METALS = ["copper", "tin", "iron", "lead", "silver", "tungsten", "gold", "platinum"]
PRE_PIECES = [("pickaxe", "Pickaxe", "tool"), ("axe", "Axe", "tool"), ("hammer", "Hammer", "tool"), ("broadsword", "Broadsword", "weapon"),
              ("shortsword", "Shortsword", "weapon"), ("bow", "Bow", "weapon"), ("helmet", "Helmet", "armor"), ("chainmail", "Chainmail", "armor"),
              ("greaves", "Greaves", "armor"), ("bar", "Bar", "material"), ("ore", "Ore", "block")]
HARD_METALS = ["cobalt", "palladium", "mythril", "orichalcum", "adamantite", "titanium"]
HARD_PIECES = [("pickaxe", "Pickaxe", "tool"), ("drill", "Drill", "tool"), ("waraxe", "Waraxe", "tool"), ("chainsaw", "Chainsaw", "tool"),
               ("sword", "Sword", "weapon"), ("repeater", "Repeater", "weapon"), ("breastplate", "Breastplate", "armor"),
               ("leggings", "Leggings", "armor"), ("bar", "Bar", "material"), ("ore", "Ore", "block")]

NOTABLE = {
    "weapon": """
wooden_sword|Wooden Sword|early|craft
wooden_bow|Wooden Bow|early|craft
cactus_sword|Cactus Sword|early|craft
enchanted_sword|Enchanted Sword|prehardmode|shrine
starfury|Starfury|prehardmode|sky chest
muramasa|Muramasa|prehardmode|dungeon
night_s_edge|Night's Edge|prehardmode|craft
blade_of_grass|Blade of Grass|prehardmode|craft
fiery_greatsword|Fiery Greatsword|prehardmode|craft
light_s_bane|Light's Bane|prehardmode|craft
blood_butcherer|Blood Butcherer|prehardmode|craft
wand_of_sparking|Wand of Sparking|early|cabin chest
amethyst_staff|Amethyst Staff|early|craft
topaz_staff|Topaz Staff|early|craft
sapphire_staff|Sapphire Staff|prehardmode|craft
emerald_staff|Emerald Staff|prehardmode|craft
ruby_staff|Ruby Staff|prehardmode|craft
diamond_staff|Diamond Staff|prehardmode|craft
water_bolt|Water Bolt|prehardmode|dungeon
book_of_skulls|Book of Skulls|prehardmode|skeletron
demon_scythe|Demon Scythe|prehardmode|voodoo demon
space_gun|Space Gun|prehardmode|craft
flamelash|Flamelash|prehardmode|craft
minishark|Minishark|prehardmode|shop
musket|Musket|prehardmode|shadow orb
the_undertaker|The Undertaker|prehardmode|crimson heart
flintlock_pistol|Flintlock Pistol|early|shop
boomstick|Boomstick|prehardmode|jungle chest
molten_fury|Molten Fury|prehardmode|craft
phoenix_blaster|Phoenix Blaster|prehardmode|craft
enchanted_boomerang|Enchanted Boomerang|early|chest
wooden_boomerang|Wooden Boomerang|early|chest
flamarang|Flamarang|prehardmode|craft
wood_yoyo|Wooden Yoyo|early|craft
rally|Rally|early|shop
amazon|Amazon|prehardmode|jungle
code_1|Code 1|prehardmode|eye of cthulhu
ball_o_hurt|Ball O' Hurt|prehardmode|shadow orb
sunfury|Sunfury|prehardmode|underworld chest
blue_moon|Blue Moon|prehardmode|dungeon
spear|Spear|early|chest
trident|Trident|prehardmode|ocean
dark_lance|Dark Lance|prehardmode|underworld
leather_whip|Leather Whip|early|shop
snapthorn|Snapthorn|prehardmode|craft
slime_staff|Slime Staff|early|slime drop
finch_staff|Finch Staff|early|living tree
imp_staff|Imp Staff|prehardmode|craft
bee_gun|Bee Gun|prehardmode|queen bee
star_cannon|Star Cannon|prehardmode|craft
grenade|Grenade|early|shop
shuriken|Shuriken|early|chest
throwing_knife|Throwing Knife|early|chest
excalibur|Excalibur|post-mech|craft
hallowed_repeater|Hallowed Repeater|post-mech|craft
true_night_s_edge|True Night's Edge|post-mech|craft
terra_blade|Terra Blade|post-plantera|craft
megashark|Megashark|post-mech|shop
clockwork_assault_rifle|Clockwork Assault Rifle|hardmode|wall of flesh
laser_rifle|Laser Rifle|hardmode|wall of flesh
crystal_storm|Crystal Storm|hardmode|craft
golden_shower|Golden Shower|hardmode|craft
cursed_flames|Cursed Flames|hardmode|craft
daedalus_stormbow|Daedalus Stormbow|hardmode|hallow mimic
rainbow_rod|Rainbow Rod|post-mech|shop
spectre_staff|Spectre Staff|post-plantera|craft
stynger|Stynger|post-golem|golem
possessed_hatchet|Possessed Hatchet|post-golem|golem
meowmere|Meowmere|endgame|moon lord
star_wrath|Star Wrath|endgame|moon lord
s_d_m_g|S.D.M.G.|endgame|moon lord
zenith|Zenith|endgame|craft
terrarian|Terrarian|endgame|moon lord
rainbow_crystal_staff|Rainbow Crystal Staff|endgame|moon lord
lunar_portal_staff|Lunar Portal Staff|endgame|moon lord
solar_eruption|Solar Eruption|endgame|craft
daybreak|Daybreak|endgame|craft
vortex_beater|Vortex Beater|endgame|craft
phantasm|Phantasm|endgame|craft
nebula_arcanum|Nebula Arcanum|endgame|craft
nebula_blaze|Nebula Blaze|endgame|craft
stardust_cell_staff|Stardust Cell Staff|endgame|craft
last_prism|Last Prism|endgame|moon lord
lunar_flare|Lunar Flare|endgame|moon lord
stardust_dragon_staff|Stardust Dragon Staff|endgame|craft
terraprisma|Terraprisma|post-plantera|empress of light
""",
    "tool": """
wooden_hammer|Wooden Hammer|early|craft
pwnhammer|Pwnhammer|hardmode|wall of flesh
cactus_pickaxe|Cactus Pickaxe|early|craft
reaver_shark|Reaver Shark|prehardmode|fishing
nightmare_pickaxe|Nightmare Pickaxe|prehardmode|craft
deathbringer_pickaxe|Deathbringer Pickaxe|prehardmode|craft
molten_pickaxe|Molten Pickaxe|prehardmode|craft
molten_hamaxe|Molten Hamaxe|prehardmode|craft
pickaxe_axe|Pickaxe Axe|post-mech|craft
chlorophyte_pickaxe|Chlorophyte Pickaxe|post-mech|craft
spectre_hamaxe|Spectre Hamaxe|post-plantera|craft
picksaw|Picksaw|post-golem|golem
luminite_drill|Luminite Drill|endgame|craft
grappling_hook|Grappling Hook|early|craft
amethyst_hook|Amethyst Hook|early|craft
topaz_hook|Topaz Hook|early|craft
sapphire_hook|Sapphire Hook|early|craft
emerald_hook|Emerald Hook|early|craft
ruby_hook|Ruby Hook|early|craft
diamond_hook|Diamond Hook|early|craft
ivy_whip|Ivy Whip|prehardmode|craft
dual_hook|Dual Hook|hardmode|craft
magic_mirror|Magic Mirror|early|chest
recall_potion|Recall Potion|early|craft
bug_net|Bug Net|early|shop
wooden_fishing_pole|Wooden Fishing Pole|early|craft
fleshcatcher|Fleshcatcher|prehardmode|craft
golden_fishing_rod|Golden Fishing Rod|hardmode|angler
wire|Wire|prehardmode|mechanic
wrench|Red Wrench|prehardmode|mechanic
minecart|Minecart|early|chest
minecart_track|Minecart Track|early|craft
bomb|Bomb|early|shop
dynamite|Dynamite|early|shop
rope|Rope|early|craft
builder_s_wand|Builder's Wand|hardmode|shop
paint_sprayer|Paint Sprayer|prehardmode|shop
empty_bucket|Empty Bucket|early|craft
water_bucket|Water Bucket|early|bucket
lava_bucket|Lava Bucket|prehardmode|bucket
honey_bucket|Honey Bucket|early|bucket
bottomless_water_bucket|Bottomless Water Bucket|hardmode|fishing
bottomless_lava_bucket|Bottomless Lava Bucket|hardmode|fishing
super_absorbant_sponge|Super Absorbant Sponge|hardmode|fishing
""",
    "armor": """
wood_helmet|Wood Helmet|early|craft
wood_breastplate|Wood Breastplate|early|craft
wood_greaves|Wood Greaves|early|craft
mining_helmet|Mining Helmet|early|shop
shadow_helmet|Shadow Helmet|prehardmode|craft
crimson_helmet|Crimson Helmet|prehardmode|craft
meteor_helmet|Meteor Helmet|prehardmode|craft
jungle_hat|Jungle Hat|prehardmode|craft
necro_helmet|Necro Helmet|prehardmode|drop
molten_helmet|Molten Helmet|prehardmode|craft
bee_headgear|Bee Headgear|prehardmode|craft
hallowed_helmet|Hallowed Helmet|post-mech|craft
hallowed_plate_mail|Hallowed Plate Mail|post-mech|craft
hallowed_greaves|Hallowed Greaves|post-mech|craft
chlorophyte_mask|Chlorophyte Mask|post-mech|craft
turtle_helmet|Turtle Helmet|post-plantera|craft
spectre_hood|Spectre Hood|post-plantera|craft
beetle_helmet|Beetle Helmet|post-golem|craft
solar_flare_helmet|Solar Flare Helmet|endgame|craft
vortex_helmet|Vortex Helmet|endgame|craft
nebula_helmet|Nebula Helmet|endgame|craft
stardust_helmet|Stardust Helmet|endgame|craft
""",
    "accessory": """
hermes_boots|Hermes Boots|early|chest
sandstorm_in_a_bottle|Sandstorm in a Bottle|early|chest
blizzard_in_a_bottle|Blizzard in a Bottle|early|chest
cloud_in_a_balloon|Cloud in a Balloon|prehardmode|craft
obsidian_skull|Obsidian Skull|prehardmode|craft
fledgling_wings|Fledgling Wings|early|sky chest
cloud_in_a_bottle|Cloud in a Bottle|early|chest
lucky_horseshoe|Lucky Horseshoe|early|sky chest
band_of_regeneration|Band of Regeneration|early|chest
aglet|Aglet|early|chest
anklet_of_the_wind|Anklet of the Wind|prehardmode|jungle
shiny_red_balloon|Shiny Red Balloon|early|sky chest
rocket_boots|Rocket Boots|prehardmode|goblin
spectre_boots|Spectre Boots|prehardmode|craft
lightning_boots|Lightning Boots|hardmode|craft
terraspark_boots|Terraspark Boots|hardmode|craft
cobalt_shield|Cobalt Shield|prehardmode|dungeon
obsidian_shield|Obsidian Shield|prehardmode|craft
ankh_shield|Ankh Shield|hardmode|craft
feral_claws|Feral Claws|early|chest
magma_stone|Magma Stone|prehardmode|underworld
mana_flower|Mana Flower|prehardmode|craft
celestial_emblem|Celestial Emblem|hardmode|craft
warrior_emblem|Warrior Emblem|hardmode|wall of flesh
ranger_emblem|Ranger Emblem|hardmode|wall of flesh
sorcerer_emblem|Sorcerer Emblem|hardmode|wall of flesh
summoner_emblem|Summoner Emblem|hardmode|wall of flesh
depth_meter|Depth Meter|early|chest
compass|Compass|early|chest
gps|GPS|prehardmode|craft
cell_phone|Cell Phone|hardmode|craft
flipper|Flipper|early|ocean
lava_charm|Lava Charm|prehardmode|underworld
angel_wings|Angel Wings|hardmode|craft
demon_wings|Demon Wings|hardmode|craft
fairy_wings|Fairy Wings|post-mech|drop
leaf_wings|Leaf Wings|post-plantera|shop
fishron_wings|Fishron Wings|post-mech|duke fishron
worm_scarf|Worm Scarf|prehardmode|eater of worlds
brain_of_confusion|Brain of Confusion|prehardmode|brain of cthulhu
shield_of_cthulhu|Shield of Cthulhu|early|eye of cthulhu
royal_gel|Royal Gel|early|king slime
hive_pack|Hive Pack|prehardmode|queen bee
""",
    "consumable": """
lesser_healing_potion|Lesser Healing Potion|early|craft
healing_potion|Healing Potion|prehardmode|craft
greater_healing_potion|Greater Healing Potion|hardmode|craft
lesser_mana_potion|Lesser Mana Potion|early|craft
mana_potion|Mana Potion|prehardmode|craft
ironskin_potion|Ironskin Potion|early|craft
swiftness_potion|Swiftness Potion|early|craft
regeneration_potion|Regeneration Potion|early|craft
spelunker_potion|Spelunker Potion|prehardmode|craft
shine_potion|Shine Potion|early|craft
night_owl_potion|Night Owl Potion|early|craft
mining_potion|Mining Potion|early|craft
builder_potion|Builder Potion|prehardmode|craft
obsidian_skin_potion|Obsidian Skin Potion|prehardmode|craft
gills_potion|Gills Potion|early|craft
life_crystal|Life Crystal|early|world
mana_crystal|Mana Crystal|early|craft
life_fruit|Life Fruit|post-mech|jungle
mushroom|Mushroom|early|world
suspicious_looking_eye|Suspicious Looking Eye|early|craft
slime_crown|Slime Crown|early|craft
worm_food|Worm Food|prehardmode|craft
bloody_spine|Bloody Spine|prehardmode|craft
abeemination|Abeemination|prehardmode|craft
guide_voodoo_doll|Guide Voodoo Doll|prehardmode|drop
mechanical_eye|Mechanical Eye|hardmode|craft
mechanical_worm|Mechanical Worm|hardmode|craft
mechanical_skull|Mechanical Skull|hardmode|craft
celestial_sigil|Celestial Sigil|endgame|craft
""",
    "material": """
feather|Feather|early|drop
shark_fin|Shark Fin|early|drop
wood|Wood|early|tree
boreal_wood|Boreal Wood|early|tree
palm_wood|Palm Wood|early|tree
rich_mahogany|Rich Mahogany|early|tree
ebonwood|Ebonwood|early|tree
shadewood|Shadewood|early|tree
gel|Gel|early|slimes
lens|Lens|early|demon eye
fallen_star|Fallen Star|early|night
bottle|Bottle|early|craft
cobweb|Cobweb|early|world
silk|Silk|early|craft
amethyst|Amethyst|early|mining
topaz|Topaz|early|mining
sapphire|Sapphire|early|mining
emerald|Emerald|early|mining
ruby|Ruby|early|mining
diamond|Diamond|early|mining
amber|Amber|prehardmode|desert
demonite_bar|Demonite Bar|prehardmode|craft
crimtane_bar|Crimtane Bar|prehardmode|craft
meteorite_bar|Meteorite Bar|prehardmode|craft
hellstone_bar|Hellstone Bar|prehardmode|craft
shadow_scale|Shadow Scale|prehardmode|eater of worlds
tissue_sample|Tissue Sample|prehardmode|brain of cthulhu
jungle_spores|Jungle Spores|prehardmode|jungle
stinger|Stinger|prehardmode|hornet
vine|Vine|prehardmode|jungle
bone|Bone|prehardmode|dungeon
pixie_dust|Pixie Dust|hardmode|pixie
unicorn_horn|Unicorn Horn|hardmode|unicorn
cursed_flame|Cursed Flame|hardmode|corruption
soul_of_light|Soul of Light|hardmode|drop
soul_of_night|Soul of Night|hardmode|drop
soul_of_flight|Soul of Flight|hardmode|wyvern
soul_of_might|Soul of Might|hardmode|destroyer
soul_of_sight|Soul of Sight|hardmode|twins
soul_of_fright|Soul of Fright|hardmode|prime
hallowed_bar|Hallowed Bar|post-mech|drop
chlorophyte_bar|Chlorophyte Bar|post-mech|craft
shroomite_bar|Shroomite Bar|post-mech|craft
spectre_bar|Spectre Bar|post-plantera|craft
beetle_husk|Beetle Husk|post-golem|golem
solar_fragment|Solar Fragment|endgame|pillar
vortex_fragment|Vortex Fragment|endgame|pillar
nebula_fragment|Nebula Fragment|endgame|pillar
stardust_fragment|Stardust Fragment|endgame|pillar
luminite_bar|Luminite Bar|endgame|craft
hook|Hook|early|drop
iron_chain|Iron Chain|early|craft
""",
    "furniture": """
work_bench|Work Bench|early|craft
furnace|Furnace|early|craft
iron_anvil|Iron Anvil|early|craft
tinkerers_workshop|Tinkerer's Workshop|prehardmode|shop
lead_anvil|Lead Anvil|early|craft
sawmill|Sawmill|early|craft
loom|Loom|early|craft
placed_bottle|Placed Bottle|early|craft
chest|Chest|early|craft
gold_chest|Gold Chest|early|world
wooden_door|Wooden Door|early|craft
wooden_table|Wooden Table|early|craft
wooden_chair|Wooden Chair|early|craft
bed|Bed|early|craft
torch|Torch|early|craft
candle|Candle|early|craft
hellforge|Hellforge|prehardmode|underworld
alchemy_table|Alchemy Table|prehardmode|dungeon
tinkerer_s_workshop|Tinkerer's Workshop|prehardmode|goblin
mythril_anvil|Mythril Anvil|hardmode|craft
orichalcum_anvil|Orichalcum Anvil|hardmode|craft
adamantite_forge|Adamantite Forge|hardmode|craft
titanium_forge|Titanium Forge|hardmode|craft
ancient_manipulator|Ancient Manipulator|endgame|cultist
heart_lantern|Heart Lantern|prehardmode|craft
campfire|Campfire|early|craft
""",
    "coin": """
copper_coin|Copper Coin|early|any
silver_coin|Silver Coin|early|any
gold_coin|Gold Coin|early|any
platinum_coin|Platinum Coin|early|any
""",
    "ammo": """
wooden_arrow|Wooden Arrow|early|craft
flaming_arrow|Flaming Arrow|early|craft
jester_s_arrow|Jester's Arrow|prehardmode|craft
unholy_arrow|Unholy Arrow|prehardmode|craft
cursed_arrow|Cursed Arrow|hardmode|craft
ichor_arrow|Ichor Arrow|hardmode|craft
musket_ball|Musket Ball|early|shop
meteor_shot|Meteor Shot|prehardmode|craft
""",
    "placeable": """
acorn|Acorn|early|tree
""",
}
