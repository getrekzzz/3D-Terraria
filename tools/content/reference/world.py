"""Reference tables: enemies, biomes, tiles (blocks/objects), buffs, projectiles, key recipes."""

# id|Name|stage|biome/source|mechanics(comma)
ENEMIES = """
green_slime|Green Slime|early|surface day|hop
blue_slime|Blue Slime|early|surface day|hop
red_slime|Red Slime|early|underground|hop
purple_slime|Purple Slime|early|surface|hop
yellow_slime|Yellow Slime|early|cavern|hop
black_slime|Black Slime|early|cavern|hop
mother_slime|Mother Slime|early|cavern|hop,split
pinky|Pinky|early|surface|hop,rare
jungle_slime|Jungle Slime|early|jungle|hop
ice_slime|Ice Slime|early|snow|hop
sand_slime|Sand Slime|early|desert|hop
lava_slime|Lava Slime|prehardmode|underworld|hop,lava
dungeon_slime|Dungeon Slime|prehardmode|dungeon|hop
spiked_ice_slime|Spiked Ice Slime|early|snow|hop,projectiles
zombie|Zombie|early|surface night|walker
bald_zombie|Bald Zombie|early|surface night|walker
frozen_zombie|Frozen Zombie|early|snow night|walker
blood_zombie|Blood Zombie|early|blood moon|walker
demon_eye|Demon Eye|early|surface night|flyer
wandering_eye|Wandering Eye|hardmode|surface night|flyer
drippler|Drippler|early|blood moon|flyer
the_groom|The Groom|early|blood moon|walker
skeleton|Skeleton|early|underground|walker
undead_miner|Undead Miner|early|cavern|walker
armored_skeleton|Armored Skeleton|hardmode|cavern|walker
cave_bat|Cave Bat|early|underground|flyer
jungle_bat|Jungle Bat|early|underground jungle|flyer
ice_bat|Ice Bat|early|underground snow|flyer
lava_bat|Lava Bat|hardmode|underworld|flyer
giant_bat|Giant Bat|hardmode|cavern|flyer
hellbat|Hellbat|prehardmode|underworld|flyer
giant_worm|Giant Worm|early|underground|segmented,burrow
devourer|Devourer|prehardmode|corruption|segmented,burrow
digger|Digger|hardmode|cavern|segmented,burrow
bone_serpent|Bone Serpent|prehardmode|underworld|segmented
tomb_crawler|Tomb Crawler|early|desert|segmented
eater_of_souls|Eater of Souls|prehardmode|corruption|flyer
crimera|Crimera|prehardmode|crimson|flyer
face_monster|Face Monster|prehardmode|crimson|walker
blood_crawler|Blood Crawler|prehardmode|crimson caves|wall_crawler
hornet|Hornet|prehardmode|jungle|flyer,projectiles
man_eater|Man Eater|prehardmode|jungle|anchored
snatcher|Snatcher|early|jungle|anchored
piranha|Piranha|early|jungle water|swimmer
shark|Shark|early|ocean|swimmer
crab|Crab|early|ocean|walker
jellyfish|Jellyfish|early|water|swimmer
antlion|Antlion|early|desert|anchored,projectiles
antlion_charger|Antlion Charger|early|desert|walker
vulture|Vulture|early|desert|flyer
cactus_man|Cactus Man|early|desert|walker
harpy|Harpy|early|sky|flyer,projectiles
wyvern|Wyvern|hardmode|sky|segmented,flyer
angry_bones|Angry Bones|prehardmode|dungeon|walker
dark_caster|Dark Caster|prehardmode|dungeon|caster,teleport
cursed_skull|Cursed Skull|prehardmode|dungeon|flyer
dungeon_guardian|Dungeon Guardian|prehardmode|dungeon|guardian
fire_imp|Fire Imp|prehardmode|underworld|caster,teleport
demon|Demon|prehardmode|underworld|flyer,projectiles
voodoo_demon|Voodoo Demon|prehardmode|underworld|flyer
goblin_peon|Goblin Peon|prehardmode|goblin army|walker
goblin_thief|Goblin Thief|prehardmode|goblin army|walker
goblin_warrior|Goblin Warrior|prehardmode|goblin army|walker
goblin_sorcerer|Goblin Sorcerer|prehardmode|goblin army|caster
goblin_archer|Goblin Archer|prehardmode|goblin army|ranged
spore_zombie|Spore Zombie|early|glowing mushroom|walker
mushi_ladybug|Mushi Ladybug|early|glowing mushroom|walker
anomura_fungus|Anomura Fungus|early|glowing mushroom|walker
granite_golem|Granite Golem|early|granite cave|walker
granite_elemental|Granite Elemental|early|granite cave|flyer
hoplite|Hoplite|early|marble cave|walker,ranged
medusa|Medusa|early|marble cave|caster
wall_creeper|Wall Creeper|early|spider cave|wall_crawler
black_recluse|Black Recluse|hardmode|spider cave|wall_crawler
salamander|Salamander|early|cavern|wall_crawler
crawdad|Crawdad|early|cavern|walker
giant_shelly|Giant Shelly|early|cavern|walker
nymph|Nymph|early|cavern|walker,rare
tim|Tim|early|cavern|caster,rare
mimic|Mimic|early|cavern|hop,rare
unicorn|Unicorn|hardmode|hallow|charger
pixie|Pixie|hardmode|hallow|flyer
chaos_elemental|Chaos Elemental|hardmode|underground hallow|teleport
gastropod|Gastropod|hardmode|hallow night|flyer
clinger|Clinger|hardmode|underground corruption|anchored
corruptor|Corruptor|hardmode|corruption|flyer
herpling|Herpling|hardmode|crimson|hop
ichor_sticker|Ichor Sticker|hardmode|underground crimson|flyer
possessed_armor|Possessed Armor|hardmode|surface night|walker
wraith|Wraith|hardmode|surface night|flyer
werewolf|Werewolf|hardmode|full moon|walker
mummy|Mummy|hardmode|desert|walker
arapaima|Arapaima|hardmode|jungle water|swimmer
derpling|Derpling|hardmode|jungle|hop
giant_tortoise|Giant Tortoise|hardmode|underground jungle|charger
moth|Moth|hardmode|underground jungle|flyer
angler_fish|Angler Fish|hardmode|underground water|swimmer
ice_elemental|Ice Elemental|hardmode|underground snow|flyer
ice_tortoise|Ice Tortoise|hardmode|underground snow|charger
wolf|Wolf|hardmode|snow|walker
red_devil|Red Devil|hardmode|underworld|flyer,projectiles
lihzahrd|Lihzahrd|post-plantera|temple|walker
flying_snake|Flying Snake|post-plantera|temple|flyer
paladin|Paladin|post-plantera|dungeon|walker,projectiles
necromancer|Necromancer|post-plantera|dungeon|caster
skeleton_sniper|Skeleton Sniper|post-plantera|dungeon|ranged
eyezor|Eyezor|post-mech|eclipse|walker,projectiles
mothron|Mothron|post-plantera|eclipse|flyer,boss_like
reaper|Reaper|post-mech|eclipse|flyer
frankenstein|Frankenstein|post-mech|eclipse|walker
pirate_deckhand|Pirate Deckhand|hardmode|pirates|walker
pirate_captain|Pirate Captain|hardmode|pirates|walker,ranged
snow_flinx|Snow Flinx|early|snow|walker
cochineal_beetle|Cochineal Beetle|early|underground|walker
cyan_beetle|Cyan Beetle|early|underground snow|walker
lac_beetle|Lac Beetle|early|underground jungle|walker
blue_jellyfish|Blue Jellyfish|early|underground water|swimmer
flying_fish|Flying Fish|early|rain|flyer
umbrella_slime|Umbrella Slime|early|rain|hop
raven|Raven|post-plantera|pumpkin moon|flyer
scarecrow|Scarecrow|post-plantera|pumpkin moon|walker
gingerbread_man|Gingerbread Man|post-plantera|frost moon|walker
elf_archer|Elf Archer|post-plantera|frost moon|ranged
martian_walker|Martian Walker|post-golem|martians|walker
tesla_turret|Tesla Turret|post-golem|martians|turret
selenian|Selenian|endgame|solar pillar|charger
vortexian|Vortexian|endgame|vortex pillar|walker
predictor|Predictor|endgame|nebula pillar|flyer
stargazer|Stargazer|endgame|stardust pillar|caster
etherian_goblin|Etherian Goblin|prehardmode|old one's army|walker
servant_of_cthulhu|Servant of Cthulhu|early|eye of cthulhu|flyer,minion
"""

# id|Name|layer|kind
BIOMES = """
forest|Forest|surface|base
ocean|Ocean|surface|edge
desert|Desert|surface|major
underground_desert|Underground Desert|underground|major
snow_biome|Snow|surface|major
jungle|Jungle|surface|major
the_corruption|The Corruption|surface|evil
the_crimson|The Crimson|surface|evil
the_hallow|The Hallow|surface|hardmode
glowing_mushroom_biome|Glowing Mushroom|underground|mini
dungeon|Dungeon|underground|structure
underworld|Underworld|underworld|layer
floating_island|Sky Island|sky|structure
space|Space|space|layer
granite_cave|Granite Cave|cavern|mini
marble_cave|Marble Cave|cavern|mini
spider_cave|Spider Cave|cavern|mini
hive|Bee Hive|underground|mini
jungle_temple|Jungle Temple|underground|structure
meteorite|Meteorite|surface|event
graveyard|Graveyard|surface|mini
aether|Aether|cavern|mini
living_tree_biome|Living Tree|surface|structure
underground_jungle|Underground Jungle|cavern|major
underground_snow|Ice Biome|cavern|major
"""

# Tiles and block-like world elements: id|Name|stage|kind
TILES = """
dirt_block|Dirt Block|early|soil
grass_block|Grass|early|grass
stone_block|Stone Block|early|stone
clay_block|Clay Block|early|soil
sand_block|Sand Block|early|sand
hardened_sand_block|Hardened Sand|early|stone
sandstone_block|Sandstone Block|early|stone
desert_fossil|Desert Fossil|early|ore
silt_block|Silt Block|early|sand
slush_block|Slush Block|early|sand
mud_block|Mud Block|early|soil
jungle_grass|Jungle Grass|early|grass
mushroom_grass|Mushroom Grass|early|grass
corrupt_grass|Corrupt Grass|early|grass
crimson_grass|Crimson Grass|early|grass
ebonstone_block|Ebonstone Block|early|stone
crimstone_block|Crimstone Block|early|stone
snow_block|Snow Block|early|soil
ice_block|Ice Block|early|stone
granite_block|Granite Block|early|stone
marble_block|Marble Block|early|stone
obsidian|Obsidian|prehardmode|stone
ash_block|Ash Block|prehardmode|soil
hellstone|Hellstone|prehardmode|ore
meteorite|Meteorite|prehardmode|ore
demonite_ore|Demonite Ore|prehardmode|ore
crimtane_ore|Crimtane Ore|prehardmode|ore
cloud|Cloud|early|sky
rain_cloud|Rain Cloud|early|sky
sunplate_block|Sunplate Block|early|built
living_wood|Living Wood|early|wood
leaf_block|Leaf Block|early|leaves
hive|Hive|early|special
cobweb|Cobweb|early|special
glass|Glass|early|built
gray_brick|Gray Brick|early|built
red_brick|Red Brick|early|built
sandstone_brick|Sandstone Brick|early|built
snow_brick|Snow Brick|early|built
mud_brick|Mud Brick|early|built
copper_brick|Copper Brick|early|built
gold_brick|Gold Brick|early|built
blue_brick|Blue Dungeon Brick|prehardmode|built
green_brick|Green Dungeon Brick|prehardmode|built
pink_brick|Pink Dungeon Brick|prehardmode|built
obsidian_brick|Obsidian Brick|prehardmode|built
hellstone_brick|Hellstone Brick|prehardmode|built
green_moss|Green Moss|early|moss
lava_moss|Lava Moss|prehardmode|moss
tree|Forest Tree|early|tree
boreal_tree|Boreal Tree|early|tree
palm_tree|Palm Tree|early|tree
mahogany_tree|Mahogany Tree|early|tree
ebonwood_tree|Ebonwood Tree|early|tree
cactus|Cactus|early|plant
giant_glowing_mushroom|Giant Glowing Mushroom|early|tree
glowing_mushroom|Glowing Mushroom|early|plant
pot|Pot|early|breakable
enchanted_sword_shrine|Enchanted Sword Shrine|early|structure
life_crystal_node|Life Crystal (placed)|early|breakable
shadow_orb|Shadow Orb|prehardmode|breakable
crimson_heart|Crimson Heart|prehardmode|breakable
demon_altar|Demon Altar|prehardmode|special
honey_block|Honey Block|early|liquid_mix
crispy_honey_block|Crispy Honey Block|early|liquid_mix
"""

# Liquids and liquid mechanics: id|Name|kind
LIQUIDS = """
water|Water|liquid
lava|Lava|liquid
honey|Honey|liquid
shimmer|Shimmer|liquid
liquid_flow|Liquid Flow|mechanic
liquid_mixing|Liquid Mixing|mechanic
swimming|Swimming|mechanic
lava_contact|Lava Contact Damage|mechanic
item_incineration|Items Destroyed by Lava|mechanic
"""

# id|Name|kind(buff/debuff)
BUFFS = """
on_fire|On Fire!|debuff
poisoned|Poisoned|debuff
bleeding|Bleeding|debuff
chilled|Chilled|debuff
frozen|Frozen|debuff
confused|Confused|debuff
cursed_inferno|Cursed Inferno|debuff
ichor|Ichor|debuff
potion_sickness|Potion Sickness|debuff
mana_sickness|Mana Sickness|debuff
regeneration|Regeneration|buff
swiftness|Swiftness|buff
ironskin|Ironskin|buff
shine|Shine|buff
night_owl|Night Owl|buff
spelunker|Spelunker|buff
mining|Mining|buff
builder|Builder|buff
cozy_fire|Cozy Fire|buff
heart_lamp|Heart Lamp|buff
well_fed|Well Fed|buff
honey|Honey|buff
obsidian_skin|Obsidian Skin|buff
gills|Gills|buff
wet|Wet|debuff
"""

# id|Name|kind
PROJECTILES = """
wooden_arrow|Wooden Arrow|arrow
flaming_arrow|Flaming Arrow|arrow
spark|Spark|magic
amethyst_bolt|Amethyst Bolt|magic
topaz_bolt|Topaz Bolt|magic
enchanted_beam|Enchanted Beam|melee_projectile
water_bolt|Water Bolt|magic
star|Falling Star|special
bullet|Musket Ball|bullet
grenade|Grenade|explosive
stinger_projectile|Stinger (hornet)|hostile
imp_fireball|Imp Fireball|hostile
"""

# output|Name|stations(+)|inputs
RECIPES = """
work_bench|Work Bench|none|10 wood
furnace|Furnace|work_bench|20 stone, 4 wood, 3 torch
iron_anvil|Iron Anvil|work_bench|5 iron bar
torch|Torch|none|1 gel, 1 wood
wooden_sword|Wooden Sword|work_bench|7 wood
copper_bar|Copper Bar|furnace|3 copper ore
copper_pickaxe|Copper Pickaxe|anvil|12 copper bar, 4 wood
lesser_healing_potion|Lesser Healing Potion|placed_bottle|2 gel, 2 mushroom, 2 bottle
bottle|Bottle|furnace|1 glass
glass|Glass|furnace|1 sand
mana_crystal|Mana Crystal|none|5 fallen star
night_s_edge|Night's Edge|demon_altar|light's bane, muramasa, blade of grass, fiery greatsword
hallowed_bar|Hallowed Bar|drop|n/a
terra_blade|Terra Blade|mythril_anvil|true night's edge, true excalibur, broken hero sword
celestial_sigil|Celestial Sigil|ancient_manipulator|fragments
"""
