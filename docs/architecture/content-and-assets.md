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

## Assets
| Asset | Tool | Output |
|---|---|---|
| Terrain texture array (71 layers; height in albedo alpha) + detail array | `tools/textures/build_textures.py --set=terrain` | `assets/textures/terrain/`, `assets/textures/detail/` |
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

People (`generator: humanoid`) are built by `tools/blender/bgen/human/`: a proportioned frame (height, build,
weight, age, sex), metaball body, sculpted head (eyelids, eyeballs with iris and pupil, lips, ears, brows and
lashes), hands with jointed fingers and nails, painted skin (lips, flush, knuckles, stubble), hair styles and
beards as strand shells, clothing cut from body shells (shirts, trousers, dresses, coats, robes, aprons, belts,
boots, hats, masks), and a 52-bone rig (fingers included) with heat/chain weights. Skeletons are separate bones on
the same rig. Params per character live in `assets.json`. They export rest-pose UVs and tangents for the character
shader.

Human motion (`human/motion.py`, `human/strafe.py`, `human/actions.py`) is authored as pose functions sampled every
frame at 30 fps (exact curves, eased keys, seamless loops): idle, walk, run, sprint, strafe_left and strafe_right
(runs 45° to the side of the hips), jump, fall, land, swim, climb, glide, hurt, death (plus an undead shamble), and
upper-body item actions — swing, mine, thrust, shoot, bow, cast, drink, use, hold. Swing, mine, thrust and bow take
their length and timing from the `clips` of `data/config/use_styles.json` (the swing's wind-up and strike window, the
chop's impact, the bow's release and re-draw), the same windows Core strikes in; after changing them, rebuild the
sets. Characters with the same skeleton (body, height, gait) share one animation set,
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
  hides the hair under helmets and draws shields and balloons (`"worn"` items).
* **Portraits**: `build_all.py --boss-icons` (boss bar heads, `assets/icons/bosses`) and `--portraits` (town NPC head
  shots and bestiary thumbnails, `assets/icons/{npcs,creatures}`) import the shipped creature GLBs, pose them on their
  idle action and outline them (`bgen/portraits.py`); `game/Items/PortraitIcons` loads them. Re-render after
  rebuilding a creature. Fur, chitin and bone prop layers (`tools/textures/gen_creature.py`) serve the creatures.
* **Hair and cloth motion (X-17)**: people within the preset's `swayDistance` run a `SpringSway` (Core) anchored at the
  head; `CreatureVisual.Sway` passes its offset to the character shader (`sway`, `sway_head`), which bends hair below
  the head joint and cloth whose vertex alpha marks it loose (skirts, robes, coat tails: alpha = 1 − weight/2, written by
  `garments.skirt`) and adds `strata_wind` flutter. No extra bones or skinning; nothing updates beyond the distance.
* **Character creator models (X-09)**: player bodies `player_<m|f>_<shirt|vest|coat>` are bald with marker colours
  (red shirt/vest/coat, blue undershirt and irises, green trousers, grey hair, belt and boots); hair is a separate model
  per body and style (`build_all.py --hair`, `bgen/human/hair_styles*.py`) worn on the head bone.

## UI look (fonts and theme)
The interface follows Terraria's look: see-through royal-blue panels (`#3F5297` at ~70%), white text with a black
outline in a bold rounded face, slot tints by kind and Terraria's rarity colours.
* **Fonts** (`assets/fonts/`, SIL OFL, licences and `CREDITS.txt` beside them): Comic Neue Bold is the UI face,
  Andika Bold its glyph fallback (set in the `.otf` import settings). `game/UI/UiFonts.cs` holds the size and
  outline constants and pre-rasterises ASCII at boot (`Warmup`, ~40 ms) so screens never rasterise mid-game.
* **Theme**: `game/UI/UiTheme*.cs` builds one `Theme` in code (panels, labels, buttons and the `TextButton`
  variation, toggles, dropdowns, tabs, sliders, scrollbars, progress bars, text fields, tooltips) and is the source of
  truth. It is exported to `assets/ui/strata_theme.tres`, which `project.godot` `[gui] theme/custom` makes the project
  theme; `UiTheme.Install()` (first thing in `GameRoot.Boot`) also sets ThemeDB's fallback font and merges the code
  theme in, so CanvasLayers, Label3D and custom drawing all get it. After changing the builder, re-export with
  `godot --headless --path . -- --export-theme` (or `ui export-theme` in the console); the `ui.theme_synced` probe in
  `ui.cmds` fails while the file is stale. Slot styles are cached per `SlotKind` (`UiTheme.SlotStyle`), so drawing a
  slot allocates nothing.
* **Rarity colours** live in Core (`Strata.Core.Presentation.RarityColors`): tiers -1 and 0–11 with Terraria's RGB,
  Quest (-11) amber, Expert (-12) a cycling rainbow and Master (-13) a red-orange pulse; `UiTheme.Rarity` converts.
* **UI scale**: `Strata.Core.Presentation.UiScale` lowers the UI scale setting so the logical viewport never drops
  below 1024×600; `DisplaySettings.ApplyUiScale` re-applies it whenever the window is resized.
* **Layout**: `Strata.Core.Presentation.UiLayout.Compute(width, height, UiState)` places every HUD and inventory
  region (`UiRegion`: hotbar, buffs, hearts, mana, minimap, info, crosshair, breath, status line, chat, boss and
  event bars, NPC chat, the F3 `debug` overlay, and the inventory's grid, coin/ammo, trash, sort buttons, panel,
  crafting and equipment) on the logical viewport, so nothing ever overlaps or leaves the screen from 1024×600 up.
  Fixed regions keep Terraria's places; chat, info, crafting and the F3 overlay shrink around them; breath, status
  line and bars move to the nearest free spot; the inventory's slots drop to 0.85 and 0.75 (`SlotScale`) and the
  minimap hides (`MinimapHidden`) when the equipment column cannot fit under it. The F3 overlay goes below the hotbar
  and buff rows. A region with no room is left out of `Rects` (the widget hides). Engine code calls it on viewport
  resize and on state changes only; `UiLayoutTests` checks every window size from 1024×600 to 3840×2160 at every UI
  scale step.
