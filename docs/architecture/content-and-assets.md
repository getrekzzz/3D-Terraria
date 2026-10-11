# Content and assets

## Content (`data/`)
* `manifest.json` lists files in load order (`Strata.Tools manifest` regenerates it; `reference/` and
  `config/` are excluded). Strict JSON with comments; unknown members are errors.
* Registries give dense runtime ids; saves store string ids with per-save tables and placeholders.
* Derived definitions: placeable blocks and tile objects imply their items.
* Open-ended behaviour uses typed specs: item `behaviors`, enemy `brain`, buff `effects`, projectile `onHit`,
  recipe/spawn `conditions` — validated against `SpecCatalog`.
* `ContentValidator`: references, specs, DAG cycles in progression, obtainability, texture layers and
  conventional asset paths (every tile-object variant, item icon/model, creature and projectile model, buff icon
  and invasion icon).
* `ItemSourceIndex` (`Content/Index/ItemSources.cs`): reverse lookup from an item to every way it is obtained
  (recipes, enemy and boss loot, named world-generation chest tables such as "Gold Chests", tile objects, blocks,
  bug-net catches, shops, fishing, crates and bags, Angler rewards, buckets, and `starter`/`world` tags). Loot tables
  resolve to their owners, and nested tables pass on their owner and conditions. The obtainability check is a loop
  over `Unobtainable`. A `shop` tag no longer excuses an item; a resident has to sell it. A loot table that nothing
  rolls is a warning. `ItemSourcesTests` pins the shipped unobtainable list (only items a pending WP will add).
* `data/reference/*.json`: Terraria concepts (names only) generated from `tools/content/reference/`.
  Content links to them with `covers`; `Strata.Tools coverage` reports coverage and CI publishes it.
* Furniture families (X-03): `tools/content/furniture_families.json` lists the pieces (door, chair, table, bed, chest,
  work bench, bookcase, piano, sofa, bathtub, lamp, lantern, candle, candelabra, chandelier, grandfather clock,
  platform: size, recipe, housing roles, light) and the materials (wood, boreal, palm, rich mahogany, ebonwood,
  shadewood, cactus, mushroom, granite, marble, obsidian, glass, bone, lihzahrd: material item, look, flame colour);
  `tools/content/gen_furniture.py` writes `data/items/generated/furniture_families.json` and the model specs
  `tools/blender/assets.d/x-03-families.json` (generator `furniture_piece`, `bgen/furniture_family.py`). The plain
  wooden set keeps its original ids. `FurnitureFamilyTests` checks every family is complete, craftable and up to date.
* **Enemy banners** (X-03) are generated too: `tools/content/banners.json` (variant groups, emblems, palette) →
  `tools/content/gen_banners.py` → `data/items/generated/banners.json` (one wall banner per coin-worthy enemy, its
  `banner.enemies`, and the Monster Banner buff) and `tools/blender/assets.d/x-03-banners.json` (the Dungeon's `banner`
  generator, cloth in the enemy's tint, emblem by its tags). CI runs `gen_furniture.py --check` and
  `gen_banners.py --check`.

## Assets
| Asset | Tool | Output |
|---|---|---|
| Terrain texture array (104 layers, among them the Dungeon wall panels and Bone Block; height in albedo alpha) + detail array | `tools/textures/build_textures.py --set=terrain` | `assets/textures/terrain/`, `assets/textures/detail/` |
| Prop texture array (bark, leaf-card sprigs, metals, cloth, leather…) | `--set=props` | `assets/textures/props/` |
| Block item icons | `tools/textures/build_icons.py` | `assets/icons/items/<block>.png` |
| UI pixel sprites (hearts, stars, bubbles, slot silhouettes, buttons, pointer, frames) | `tools/textures/build_ui.py` | `assets/ui/<name>.png` |
| Buff/debuff and world-event icons (32 px glyph tiles) | `tools/textures/build_ui.py` (`buff_icons.json`, `event_icons.json`) | `assets/icons/buffs/<icon>.png`, `assets/icons/events/<icon>.png` |
| Tile objects, items, creatures + icons | `tools/blender/build_all.py` (+ `assets.json`) | `assets/models/{objects,items,creatures}/*.glb`, icons, `assets/manifest.json` |

Textures are synthesised in numpy (`tools/textures/gen_{natural,rock,built,plants,detail}.py` on `synth.py`):
stones, soils, bark, bricks, planks, metals, cloth and leather with their height (albedo alpha for opaque layers),
roughness and metalness; cut-out and translucent layers keep opacity in alpha instead.

UI sprites are ASCII pixel maps (`tools/textures/ui_sprites.py`, `ui_sprites_b.py`) on one shared palette: each cell
becomes 2x2 pixels and gets a 1 px `#1a1020` outline, except silhouettes, the pointer and 9-slice frames, which carry
their own edges. The game reads them through `UiSprites.Get(name)` (`AssetPaths.UiSprite`), which the HUD preloads;
draw them at integral sizes (`TextureFilter = Nearest` when scaled). `build_ui.py --check` runs in CI without Pillow,
and `AssetCoverageTests.UiSpritesUsedByCodeExist` checks every name the game code asks for.

Buff and event icons are glyphs from a 16x16 library (`ui_glyphs.py`, `ui_glyphs_b.py`; X/H/S tones of the
recipe's fg) on a bevelled 32 px tile in the recipe's bg; debuffs get a 2 px red frame. A new buff or event needs a
recipe line, or `build_ui.py --check` fails naming it, and `ContentValidator` reports a missing buff icon (and a
missing icon for any event with a kill goal) as an asset error. The game reads them through `StatusIcons.Get`
(`AssetPaths.BuffIcon`/`EventIcon`, `EventDef.Icon`), which falls back to a lettered square.

Blender conventions: 1 unit = 1 m, Z up, front faces +Y (glTF −Z); tile objects have their origin at the
footprint's bottom centre, items at the grip with the business end along +Z. Material slots are named
`prop:<layer>[:glow]` (object-space triplanar props shader), `card:<layer>` (alpha-tested leaf cards with UVs and
crown-shaped custom normals: tree crowns and bushes) or `char:<kind>` (people); vertex colour tints (written as
sRGB). Creatures are rigidly skinned; actions are exported as glTF animations. `build_all.py --check` verifies
every tile object, item and enemy has a generator (no Blender needed). The spec is `assets.json` merged with the
drop-in files `assets.d/*.json` (`asset_spec.load_spec()`; duplicate ids fail), and generator modules can register
functions with `@generator` (`bgen/__init__.py`), which `build_all.py` finds by importing every `bgen` module.

Bevels: `MeshBuilder.box(..., bevel=r, bevel_segments=n)` / `bevel_box(lo, hi, material, color, radius, segments)` and
`extrude_polygon(..., bevel=r)` (convex outlines) chamfer (1 segment, 44 tris per box) or round (2, 108 tris) every edge,
the radius clamped to 45 % of the thinnest extent. `bgen/furniture.py` uses two shared radii, `SOFT` (0.018 m, 2
segments: tops, seats, chest bodies, frames) and `EDGE` (0.010 m chamfer: legs, slats, bands, planks), scaled per entry
by `params.bevel` (0 = hard boxes); other furniture modules should import those constants and `bevels(p)`.

People (`generator: humanoid`) are built by `tools/blender/bgen/human/`: a proportioned frame (height, build,
weight, age, sex; muscle and fat above average add visible mass), metaball body, a head sculpted as a signed
distance field (`headshape.py` puts together `faceforms.py`: skull, cheekbones, cheeks and a lofted lower face with
a clean jaw line, rounder and fuller-cheeked on women, fuller with weight, lined and sagging with age; `nosemouth.py`:
a nose in one piece with wings and nostrils, lips with a cupid's bow; `earshape.py`: rolled ears; `creature.py`: a
reptile's low, long skull; almond eyelids with creases; meshed by `sdf.py`/`fieldmesh.py` with the meshes cached in
`tools/blender/_cache`, not committed; creature params for goblin and elf ears, long hooked or wide noses, wide grins,
heavy brows over deep-set eyes, low foreheads, heavy or pointed jaws, an underbite with tusks, a reptile's snout, and
short legs under a goblin's big head) with separate eyeballs, brows and lashes; hands
with jointed fingers and nails; painted skin (lips, flush, knuckles, stubble); hair sculpted from traced locks into
one surface with grooves between the clumps (`locks.py` traces where each lock roots and how it flows: a neat cut
with a side-swept fringe, fuller on top and close at the sides and back, messy hair in flat, uneven locks, a woman's
short hair as a chin-length bob, a buzz cut as a close crop, `crop.py`; `hairsdf.py` blends them into lens-shaped
clumps that stay full round bends and end in rounded or blunt tips, a cut tapering into the skin toward its edge
at the sides and nape, with UVs along the flow, `hairpaint.py`; long hair falls over a smooth dome over each ear, `hair.py`, and with
ponytails hangs onto the shoulders, back and clothes, `longhair.py`, `hairdrape.py`, `hairtails.py`; buns are a
rope coiled round a core; hair under a hat is pressed flat inside it, comes out from under its rim no thicker than
the hat and is cut along the hat's own edge, `underhat.py`);
beards in styles (`beard.py`: where the beard grows, a feathered cheek line up to the sideburns, the lips left
clear; `beardlocks.py` lays a combed mass over the face that thins out to a soft edge where it grows out of the
skin, with the moustache rising out of it over the upper lip and sinking into it at the corners of the mouth;
`beardbib.py` hangs one mass below the jaw, from the chin and the throat down to a few big tapered locks with soft
grooves, resting on the neck and the clothes and sinking into the face's mass toward its sides; `beardsdf.py` melts
them into one mesh); clothing cut from body shells and tailored
(`tailoring.py`, `drape.py`: eased off the skin, bridged across clefts and settled over hollows, hanging from the
chest, cinched under the belt and boot tops, folded inside the elbows and knees) then finished (`finishing.py`:
pressed seams, slim collars and cuffs, hems, turned-down boot rims, soles, buttons): shirts, trousers, coats, robes
as one loose garment with a skirt and a tied sash (`robes.py`, `skirts.py`), torn undead clothing (`tatters.py`: a
few large rips and ragged hems with turned edges over skin), body suits for creatures, bib aprons tied at the neck
and waist (`apron.py`, `straps.py`), belts with buckles, boots, masks (`facemask.py`), caps and hoods sculpted as
cloth fields (`headwear.py`, `capshapes.py`; `hood.py` drapes the cowl over whatever is under it), metal helmets
(`helms.py`), wide hats (`hats.py`, meshed with clean colour edges and strip bands by `hatmesh.py`, `shellmesh.py`),
wreaths, circlets and perched caps resting on the hair (`crowns.py`, `circlets.py`, `perched.py`), horns, wings and
tails (`features.py`, `wings.py`, `icewings.py`, `tails.py`, `serpent.py`; roots out through hemmed slits in the
clothes, `wingroot.py`) and bird talons (`talons.py`). Monster faces are sculpted into the head's field
(`monsterface.py`: a cyclops eye, a stitched brute, a grimacing ghoul, a faceless void) with their own parts
(`monsterparts.py`); Medusa wears snakes for hair (`snakehair.py`), and a shaman's mask, headdress, beads, leaf skirt
and paint (`tribal.py`, `tribalmask.py`) and a ghost's wisping hem (`spectre.py`) are put on through
`creaturewear.py`. Ambient occlusion is baked into the vertex alpha
(`occlusion.py`) for the character shader. A 52-bone rig (fingers included) with heat/chain weights; long hair
follows the torso, not the arms. Skeletons are sculpted bone fields on the same rig (`skulls.py` with orbits, teeth
and a hinged jaw; `skeltorso.py`, `skelgirdle.py`, `skellimbs.py` one connected column, ribcage, pelvis and limbs)
weighted to stay joined in every action, and dressed from a slim body when their params give an outfit
(`skeldress.py`, `skelfit.py`). Bodies of other stuff share the rig and actions too (`matter.py`): a granite golem
of chipped, cracked blocks (`golem.py`), a gnarled wooden creature with fissured bark, twig fingers, branches and
leaves (`woodfolk.py`, `twigs.py`), a gingerbread man with icing and gumdrops (`cookie.py`) and an empty suit of
plate armour glowing inside (`plate.py`, `gauntlet.py`); a scarecrow has a stuffed burlap sack for a head, straw
in its cuffs, collar and boots, straw hands, clothes pushed out in stuffing lumps, patched clothes and a rope belt
(`scarecrow.py`, `straw.py`, `stuffing.py`, `patches.py`, `rope.py`). A skeleton hoplite wears Greek bronze: a
crested Corinthian helm, a muscled cuirass, leather pteruges, greaves and a round shield (`hoplite.py`,
`corinthian.py`, `kit: hoplite`), and a goat-legged demon stands on cloven hooves (`hooves.py`, `feet: hooves`).
Params per character live in `assets.json` (documented at the top of `bgen/human/__init__.py`). They export rest-pose UVs and tangents for the character shader.

Machines and aliens (`human/scifi.py`, worn after the clothes) get a grey alien's or an insect's head with big
glossy eyes and antennae (`alienhead.py`, on the head's distance field), armour plates over a body suit (`plates.py`
models each plate as an even shell over a smooth core; `platefit.py` fits the core to the clothes it covers as an
envelope over their folds, presses the clothes in under the plate rather than denting it, and shades the plate from
its field so a reduced mesh still reads as hard and smooth; `armor.py`, `armorparts.py`, `armorsets.py`
lay out the styles with panel seams and glowing strips; `solarblades.py` adds a Selenian's flame blades), a glass
dome or a diver's helmet on a neck ring (its cross-section swept round the neck, so it is true all the way round), a closed helm with lit eye slits, a cyborg's skull implant and a jetpack
(`helmets.py`, `diverhelm.py`, `solarhelm.py`, `implant.py`). Glass uses the `char:glass` slot, drawn see-through by
`shaders/character_glass.gdshader` and left out of the baked occlusion.

Human motion (`human/motion.py`, `human/strafe.py`, `human/rest_poses.py`, `human/actions.py`,
`human/actions_items.py`) is authored as pose functions sampled every frame at 30 fps (exact curves, eased keys,
seamless loops): idle, walk, run, sprint, strafe_left and strafe_right (runs 45° to the side of the hips), jump, fall,
land, swim, climb, glide, hurt, death (plus an undead shamble); the rest poses sit (hip joints at seat height), sleep
(on the back, hips just above the mattress), bound (kneeling, wrists behind the back) and lie (curled on the side);
and the upper-body item actions, one per use style — swing, mine, rapier, spear, whip, place, use (melee, tools and
gestures, plus a greeting wave) and gun, bow, staff, tome, throw, hold_up, drink, eat (ranged and consumables) — and
the looping right-arm clips hold (a carried light), flail and yoyo. The right hand is a closed fist around a handle at
`motion.GRIP`, where the game attaches held items (the use styles' hand frames are measured from it). Attacks have a
wind-up, a coil of the torso with each joint trailing the one above, a wrist snap and a follow-through. Ranged clips
let go in their first moments (the shot leaves as the use starts, as in Terraria). Swing, mine, rapier, spear, whip,
throw and bow take their length and timing from the `clips` of `data/config/use_styles.json` (wind-ups and strike
windows, the chop's impact, the throw's and the bow's release and re-draw), the same windows Core strikes in, and the
swing's arm follows the style's `bladeCurve`, so the blade is drawn where the hits land; after changing them, rebuild
the sets. Characters with the same skeleton (body, height, gait, arm spread) share one animation set,
`assets/models/creatures/anim/<set>.glb`, with `index.json` naming each character's set; rebuild them with
`build_all.py --anims [--shard=I/N]` (seconds each, no character meshes touched).

Bosses and other detailed creatures (`bgen/bosses_pre*.py`, `bosses_hard.py`, `bosses_lunar.py`) build organic
bodies from metaballs (`bgen/detail.py`: sculpted and painted per vertex, branching veins, teeth rings and rows),
translucent parts in `prop:<layer>:clear` slots (slime gel, cornea, wings), and small rigs through
`bosses_pre2.clips` (mandibles, jaws, wings, a stinger, Deerclops' walk and swipe). Free-flying boss hands are joined
to the head by an `armModel` the game stretches between them. `tools/blender/preview.py` renders any creature (or a
raw `generator:params`) as a Workbench contact sheet of views or clip frames, without the game.

Rendering-only settings: `data/config/graphics.json` (presets) and `data/config/grass.json` (grass kinds per grass
block) are validated by `Strata.Tools validate` and at boot, as is `data/config/use_styles.json` (the human clips'
strike, release and re-draw windows, shared by gameplay and presentation); `data/config/lighting.json` (exposure,
glow, fog cap, torch lights, block-light curve) is validated by `Strata.Tools validate`.

### Art overhaul additions (sockets, metals, ores, icons, armour, portraits)
* **Held-item sockets** (`bgen/sockets.py`): every held item GLB carries named empties beside its mesh — `grip`,
  `tip`, `muzzle`, `nock`, `string`, `chain`, `handle_end` — oriented so the node's −Z is the socket's forward and +Y
  its up; `assets/manifest.json` lists them per item (`"x y z > forward"`, Y-up). `PropLibrary.Sockets/Socket/Bounds`
  caches them at model load. The spec's `projectiles` section builds projectile models (yoyo heads, flail balls).
* **Metals**: tinted metal slots `prop:iron:metal` read their layer as neutral grey normalised by the layer's mean
  (`layers.json` `mean`, prop shader `neutral_gain`), so a metal's `metals` hex is the colour seen. `metals` entries may
  carry accents; `metalStyles` (`assets.d/p3-39-metal-styles.json`) gives each tier its own sword/pick/axe/hammer/bow
  silhouette (`bgen/blade_profiles.py`, `bgen/tools_heads.py`); iconic weapons have bespoke generators
  (`bgen/weapons_unique*.py`); guns are side profiles (`bgen/guns.py`).
* **Ores**: `BlockDef.glow` (0–1, shader only, never baked light) reaches the terrain shader through the surface
  table's `.w`; the ore tint (packed `0xRRGGBB`) through the layer table's `.w`. Ore flecks (metallic texels) glow
  and glint out to the preset's `oreGlintDistance`.
* **Icons**: item icons render as outlined sprites (`render_icon(sprite=True)`: double size, denoised, rim light,
  outline in the darkened median colour, downsampled); framing per generator in `bgen/icon_views.py`.
* **Armour** (`bgen/armor_fit.py`, `armor.py`, `armor_build.py`): pieces are shells fitted to the shipped player
  model per bone and built in the bone's local space; `assets/models/armor/<set>_<piece>.glb`, manifest `armor`
  (model, bone, slot, refLength, hidesHair) and `armorRig`. `PlayerAvatar.Armor` attaches them (scaled by bone length),
  hides the hair under helmets and draws shields and balloons (`"worn"` items). What is drawn follows the slot model
  (`PlayerInventory.ShownArmor`/`ShownAccessories`): a vanity piece replaces the armour in its slot, hidden or locked
  accessory slots are not drawn, and a vanity shield or balloon replaces a functional one.
* **Portraits**: `build_all.py --boss-icons` (boss bar heads, `assets/icons/bosses`) and `--portraits` (town NPC head
  shots and bestiary thumbnails, `assets/icons/{npcs,creatures}`) import the shipped creature GLBs, pose them on their
  idle action and outline them (`bgen/portraits.py`); `game/Items/PortraitIcons` loads them. Re-render after
  rebuilding a creature. Fur, chitin and bone prop layers (`tools/textures/gen_creature.py`) serve the creatures.
* **Hair and cloth motion (X-17)**: people within the preset's `swayDistance` run a `SpringSway` (Core) anchored at the
  head; `CreatureVisual.Sway` passes its offset to the character shader (`sway`, `sway_head`), which bends hair below
  the head joint and cloth whose UVSway map marks it loose (skirts, robes, coat tails, sash ends; the third UV map, read
  as `CUSTOM0.x`, written by `skirts.mark_sway` and `human.sway_uvs`; the vertex alpha holds the baked occlusion) and
  adds `strata_wind` flutter. No extra bones or skinning; nothing updates beyond the distance.
* **Character creator models (X-09)**: player bodies `player_<m|f>_<shirt|vest|coat>` are bald with marker colours
  (red shirt/vest/coat, blue undershirt and irises, green trousers, grey hair, belt and boots); hair is a separate model
  per body type and style (`build_all.py --hair`, `bgen/human/hair_styles*.py`) worn on the head bone, built on the
  coat body so draped styles rest on its clothes, with vertex colour and baked occlusion (`bgen/hair_build.py`).

### Palette furniture (dungeon sets)
`bgen/dungeon_furniture.py` holds a palette-driven furniture family (door, chair, table, bookcase, candelabra,
chandelier, floor lamp, vase) registered with `@generator`. Every generator reads `palette` (a name from `PALETTES`
or an inline dict) with per-key overrides (`body`/`bodyTint`, `trim`/`trimTint`, `metal`/`metalTint`, `wax`,
`books`), so other furniture families reuse it with other layers and tints; `resolve_palette`, `plinth` and
`candle_cup` are shared helpers. The blue, green and pink Dungeon sets live in `data/tileobjects/dungeon.json`
(specs in `tools/blender/assets.d/p4-17-dungeon-furniture.json`). A tile object's `flame.also` lists more flame bases
(same size and colour) for multi-candle lights; keep them in sync with the candle tops the generator places.

### Dungeon and boss generators
Besides the palette furniture above, the Dungeon draws on `banner` (the six hall banners: Marching Bones, Necromantic
Sign, Rusted Company Standard, Ragged Brotherhood Sigil, Molten Legion Flag, Diabolic Sigil, 2 x 4 wall hangings
drawn flush, back to the wall), `statue` (angel, armor and skeleton statues), `book` (shelf books), `spike_strip`
(`spike`, one MultiMesh per chunk), `chest` (the six vault biome chests and the locked Gold and Shadow chests),
`bone_welder`, and the bosses' `trophy` generator (3 x 3 wall hangings, front turned away from the wall). Each boss's
treasure bag, masks and Expert items are items in the boss's data file with models from the boss generator modules.
Rebuild only what you add: `python3.13 tools/blender/build_all.py --only=<ids>`, which keeps the rest of
`assets/manifest.json` (when two branches both add assets, take one side's manifest and rebuild the other side's ids).

## UI look
The interface (fonts, theme, layout, HUD, inventory screens, menus) is documented in [`ui.md`](ui.md).
