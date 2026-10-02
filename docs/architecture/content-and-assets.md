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
