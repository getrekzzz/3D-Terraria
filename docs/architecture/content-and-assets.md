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
weight, age, sex), metaball body, sculpted head (eyelids, eyeballs with iris and pupil, lips, ears, brows and
lashes), hands with jointed fingers and nails, painted skin (lips, flush, knuckles, stubble), hair styles and
beards as strand shells, clothing cut from body shells (shirts, trousers, dresses, coats, robes, aprons, belts,
boots, hats, masks), and a 52-bone rig (fingers included) with heat/chain weights. Skeletons are separate bones on
the same rig. Params per character live in `assets.json`. They export rest-pose UVs and tangents for the character
shader.

Human motion (`human/motion.py`, `human/strafe.py`, `human/rest_poses.py`, `human/actions.py`,
`human/actions_items.py`) is authored as pose functions sampled every frame at 30 fps (exact curves, eased keys,
seamless loops): idle, walk, run, sprint, strafe_left and strafe_right (runs 45° to the side of the hips), jump, fall,
land, swim, climb, glide, hurt, death (plus an undead shamble); the rest poses sit (hip joints at seat height), sleep
(on the back, hips just above the mattress), bound (kneeling, wrists behind the back) and lie (curled on the side);
and the upper-body item actions, one per use style — swing, mine, rapier, spear, whip, place, use (melee, tools and
gestures, plus a greeting wave) and gun, bow, staff, tome, throw, hold_up, drink, eat (ranged and consumables) — and
the looping right-arm clips hold (a carried light), flail and yoyo. Ranged clips let go in their first moments (the
shot leaves as the use starts, as in Terraria). Swing, mine, rapier, spear, whip, throw and bow take their length and
timing from the `clips` of `data/config/use_styles.json` (wind-ups and strike windows, the chop's impact, the throw's
and the bow's release and re-draw), the same windows Core strikes in, and the swing's arm follows the style's
`bladeCurve`; after changing them, rebuild the sets. Characters with the same skeleton (body, height, gait) share one animation set,
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
  the head joint and cloth whose vertex alpha marks it loose (skirts, robes, coat tails: alpha = 1 − weight/2, written by
  `garments.skirt`) and adds `strata_wind` flutter. No extra bones or skinning; nothing updates beyond the distance.
* **Character creator models (X-09)**: player bodies `player_<m|f>_<shirt|vest|coat>` are bald with marker colours
  (red shirt/vest/coat, blue undershirt and irises, green trousers, grey hair, belt and boots); hair is a separate model
  per body and style (`build_all.py --hair`, `bgen/human/hair_styles*.py`) worn on the head bone.

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
* **Slots, tooltips and the pointer**: `ItemSlotView` (drawing in `ItemSlotView.Draw.cs`) tints each slot by
  `SlotKind` (gold when selected or favourited, dark when locked), centres the icon at 62% of the slot, writes the
  stack count bottom-left in outlined white (a bow's or gun's rounds from `PlayerInventory.CountAmmo` in the same
  corner, red at 0) and a faint native-size silhouette in empty slots; a held right button repeats the click at
  `HoldRepeat` pace; hidden views stop processing (probe `ui.slot_views`). `TooltipFollower` (CanvasLayer 95) is
  the only item tooltip: shown instantly on hover beside the pointer, lines coloured by `UiTheme.TooltipColor`,
  rebuilt only when the stack changes or every 0.25 s. `CursorItemLayer` (CanvasLayer 90) draws the held cursor
  stack at the pointer over every screen. `UiCursor` makes the generated `assets/ui/cursor.png` the system pointer,
  tinted by the cursor colour setting (Terraria's `#ff325f` by default; settings files without a `version` key move
  from the old white default) and scaled with the UI. Moving items plays `grab` (`tools/audio/sfx_recipes_ui.py`).
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
* **Input gate** (`game/UI/InputGate.cs`): screens register as `GateKind.Modal` (pause menu and settings, console,
  world map: `GameplayBlocked`, everything stops) or `GateKind.Overlay` (inventory with its chest, town dialog with
  its shop: `OverlayOpen`, the pointer is free but `MovementBlocked`/`HotkeysBlocked` stay false, so the player walks,
  jumps, grapples, picks hotbar slots and fights). `MouseFree` (either kind) shows the pointer and stops mouse look
  (`LookBlocked`); `TextEntry` blocks movement and hotkeys too. With an overlay open the item is used only while the
  button is held on empty screen: `WorldClickCatcher`, a full-screen control on CanvasLayer 9 under the HUD and every
  overlay, takes exactly the clicks no panel or slot took and sets `WorldPointer` (left: use toward the crosshair, or throw the cursor
  stack; right: throw one, or drag to turn the camera through `CameraRig.AddLook` with `CameraDrag`). The number keys
  are polled (`InteractionController.PollHotbarKeys`), the wheel reads the rebindable `hotbar_next`/`hotbar_prev`
  actions and is left to a panel under the pointer (`InputGate.PointerOverPanel`). Smoke: `ui click`/`ui drag`
  press on empty screen through the GUI (`PointerInput`); probes `ui.inventory`, `ui.overlay`, `ui.modal`,
  `ui.world_clicks`, `camera.yaw`, `player.hotbar`.
* **HUD** (`game/UI/Hud.cs`, widgets in `game/UI/Hud/`): `Hud.ApplyLayout` builds a `UiState` and places every widget
  on its `UiLayout` region whenever the logical viewport, the UI scale or what is shown changes (inventory, NPC chat,
  boss, buff count, info rows, minimap setting, F3), never per frame; `Hud.LayoutApplied` moves the `Minimap` and the
  `PerfOverlay` on their own canvas layers. A headless run lays out for the project window over the UI scale
  (`LayoutWidgets.LogicalSize`). Widgets: `ItemNameLabel` (held stack's `ItemTooltip.NameLine` in its rarity colour,
  "Inventory" while open), `HotbarBar` (top-left, unselected slots ease to 0.78 scale, `hotbar_tick` on every
  switch, hidden while the inventory is open), `Crosshair` (cursor colour; `talk_hint`/`open_hint` with the interact
  key; holds the `TargetLabel`), `StatusLine` (refusals and notices for 2 s under the crosshair, else the build-shape
  hint), `InfoPanel` (info-accessory rows with the accessory's icon, FPS last, 4 Hz), `VitalsBars` (life, mana and
  breath bars until the hearts, stars and bubbles), the buff row (`Hud.Buffs.cs`), the pickup feed in the chat region
  and the boss bar. Text widgets draw through `OutlinedText` (no minimum size, clipped), so nothing outgrows its
  region; the minimap has a 9-slice `minimap_frame` and +/− buttons (zoom 0.5–6) on CanvasLayer 10, above the click
  catcher. Every placed control joins `LayoutWidgets.Group`; probe `ui.overlaps` counts intersecting pairs in the live
  tree (and logs them). Smoke: `ui f3`, `ui minimap in|out`; probes `hud.item_name`, `hud.status`,
  `ui.layout_width`, `ui.minimap_zoom`.
* **Inventory screen** (`game/UI/InventoryScreen*.cs`, `game/UI/Inventory/`): Terraria's layout on the HUD's
  `UiLayout` (the HUD feeds it `InventoryScreen.Panel` and `AccessoryRows` and calls `InventoryScreen.Place` from
  `LayoutApplied`; `LayoutChanged` lays it out at once on open, close or a panel change). The backpack's 10×5 grid
  sits on the hotbar's origin (first row = hotbar, selected slot gold); `CoinAmmoColumns` holds the coin and ammo
  columns, the trash slot under them (saved with the player) and the `sort`/`quick_stack` buttons; the panel host
  under the grid shows one panel at a time through `ShowPanel(Control, UiPanel)`/`ClosePanel()` (the chest today,
  with 40 pooled slot views rebound on open; the shop and service panels plug in here) and sizes its slots by
  `SlotScale`; `CraftingPanel` takes the Crafting region (below); the equipment column takes the
  Equipment region under the minimap. Every part is a registered layout widget made of plain controls placed by hand,
  so none outgrows its region. Closing stows the cursor and the service slot and closes the panel. Partials: `.Layout`
  (build and place), `.Slots` (views), `.Clicks` (`GameWorld.ClickSlot`, sounds, sort and quick stack).
  Smoke: `ui sort`, `ui quickstack`; probes `ui.grid_on_hotbar`, `ui.panel`, `ui.slot_scale`.
* **Chest panel** (`game/UI/Inventory/ChestPanel*.cs`): the open chest in the panel host, its name (custom, else the
  chest's) over 40 pooled teal slots and Terraria's white-to-gold text links Loot All, Deposit All, Quick Stack,
  Restock, Sort Items (`GameWorld.ChestAction`: favourites and the hotbar stay, Core marks the chunk) and Rename. Rename
  turns the title into a field that sets `InputGate.TextEntry` and opens a modal `rename` gate while it has the
  keyboard (typing never moves the player); Enter or a click elsewhere keeps the name (`GameWorld.RenameContainer`),
  Esc cancels. The panel owns opening and putting the chest away: `Shown` drives the lid (`TileObjectRenderer`), the
  6 m reach check closes it, and putting it away plays `chest_close` at the chest (`tools/audio/sfx_recipes_chests.py`;
  `chest_open` plays on interact). Smoke: `ui chest <loot|deposit|quickstack|restock|sort|rename|name <text>|cancel>`;
  probes `ui.renaming`, `ui.chest_named`, `ui.chests_closed`.
* **Equipment column** (`game/UI/Inventory/EquipmentPanel*.cs`, `DefenseBadge.cs`): tabs `tab_equipment`,
  `tab_misc` and a disabled `tab_housing` for the town phase over two pages. Equipment: Dye | Vanity | Equip for the
  helmet, shirt and pants and one row per accessory slot (`GameWorld.AccessorySlotsFor(difficulty, demonHeart)`: 5 in
  Classic/Journey and Expert, 6 in Master, +1 from a consumed Demon Heart in Expert/Master, set again the moment the
  heart is eaten); a locked row shows only while it still holds something (greyed, `SlotFilters` refuse new items, the
  old one can be taken out) and the rows pack from the top. Each accessory row has an eye (`eye_open`/`eye_closed`)
  that flips its `PlayerInventory.HiddenAccessories` bit, which `ShownAccessories` (the avatar) skips. Misc: pet, light
  pet, minecart, mount and hook with their dyes. Empty slots name themselves ('Helmet', 'Social Shirt', 'Dye') through
  the tooltip's empty-slot text, and `DefenseBadge` shows the defence on the shield with '{n} Defense' and the set bonus
  in `TooltipFollower.ShowText`. Smoke: `ui tab <equipment|misc>`, `ui eye <row>`; probes `ui.equip_rows`,
  `ui.defense_badge`, `ui.misc_tab`, `inv.accessory_slots`, `inv.hidden_accessories`.
* **Crafting column** (`game/UI/CraftingPanel*.cs`, `RecipeDetail.cs`): Terraria's bottom-left column of recipe slots
  (pooled `ItemSlotView`s, `SlotKind.Crafting`; the selected one `CraftingSelected` at 1.25×) in a wheel-scrolled
  `ScrollContainer` only as tall as its slots, so the rest of the region leaves clicks to the world. Beside it
  `RecipeDetail` (public, for the Guide's help slot) shows the name, 'Materials' as 32 px slots (`ItemSlotView.Short`
  draws the count red; a group reads 'Any Wood' via `RecipeText.Ingredient`), 'Required Objects: …' from `StationNames`
  and `RecipeText.Conditions` ('Near Water'). The 'Near:' line names the station objects and liquids in reach
  (`CraftingEnvironment.ObjectNames`). Clicking a recipe selects it; on the selected one left crafts to the cursor
  (`RecipeBook.CraftInto`), Shift crafts a stack into the backpack, a held right button repeats at `HoldRepeat`'s pace
  (craft sound at most every 0.1 s, stopping if the selection changes); refusals go to the status line. The hammer
  toggle lists known but uncraftable recipes greyed (`ItemSlotView.Dimmed`). No timer rebuilds: backpack/coin/ammo
  `Changed` events dirty the list (once a frame at most), a 0.5 s poll dirties it only when the station or liquid sets
  differ, and the column is rebuilt only when `CraftingEnvironment.Signature` changes (else the counts update in place).
  Smoke: `ui craft <item> [n]`; probes `ui.recipes`, `ui.recipe_rebuilds` (since the inventory opened).
* **NPC chat box** (`game/UI/TownDialog*.cs`): Terraria's dark-blue chat box (`UiTheme.ChatPanel`, light rim) on the
  layout's `NpcChat` region at the top centre (the HUD lays out again on `TownDialog.LayoutChanged` and places it from
  `LayoutApplied`): a region-sized pass-through host shrinks the box to the resident's name (14 px, muted), their line
  (16 px, wrapped, cut to the rows that fit) and a flow of `TextButton` words that turn gold and grow to 1.1× under
  the mouse (`ui_hover`/`ui_click`). The buttons come from the resident's definition: Shop when `shop` is not empty,
  the `service` (`TownDialog.Services.cs`: label with its live price and handler; extend `ServiceLabel`/`RunService`
  for more buttons) and Close. An overlay gate; the resident waits facing the player while it is open
  (`TownSystem.HoldTalk`), and walking out of `TalkRange` or Esc closes it. Shop hides the box and raises
  `ShopRequested` (the inventory's shop panel). Probe `ui.npc_chat_top` (open in the top third, overlapping no HUD
  widget; `LayoutWidgets.CountOverlapsWith`).
