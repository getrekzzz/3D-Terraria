# User interface

The interface follows Terraria's look and layout while the world stays realistic 3D: see-through royal-blue panels,
white text with a black outline in a bold rounded face, slots tinted by kind, Terraria's rarity colours, the hotbar
top-left, hearts and mana top-right, chat bottom-left, and an inventory screen that keeps the player moving. This
page is the map; the per-screen reference (probes and smoke commands included) is the last section.

## Fonts and licence
`assets/fonts/` holds Comic Neue Bold (the UI face) and Andika Bold (its glyph fallback, set in the `.otf` import
settings), both under the SIL Open Font License (`OFL-ComicNeue.txt`, `OFL-Andika.txt`, `CREDITS.txt` beside them).
`game/UI/UiFonts.cs` holds the sizes and outline constants; `UiFonts.Warmup` pre-rasterises ASCII at boot (~40 ms,
probe `ui.warmup_ms`) so no screen rasterises glyphs mid-game. The title logo (`assets/ui/logo.png`) is rendered from
the same font by `tools/textures/ui_logo.py`.

## Palette, slot kinds and rarity
`game/UI/UiTheme.cs` holds every UI colour: `PanelBlue` (#3F5297 at ~70 %), `PanelBorder`, `PanelHover`, `ChatPanel`
(the NPC chat box), text colours (`Text`, `Muted`, `Gold`, `Good`, `Bad`), life and mana, the four coin colours and the
chat-log message colours (boss purple, arrivals blue, deaths red, events green, housing yellow). Slot fills and rims
come from `SlotKind`, cached per kind and hover (`UiTheme.SlotStyle`, no allocation per draw):

| SlotKind | Used for |
|---|---|
| Inventory, HotbarSelected | backpack slots; the selected hotbar slot in gold |
| Chest, Shop, Buyback | an open chest (teal), shop wares, sold stacks (amber) |
| Equip, Vanity, Dye, Misc | the equipment column's three columns and the Misc page |
| Trash, Coin, Ammo | the trash slot and the coin and ammo columns |
| Favorite | a favourited backpack slot (gold rim) |
| Crafting, CraftingSelected | recipe slots; the selected recipe (1.25×, gold rim) |
| Locked | an accessory row the world has not unlocked but which still holds an item |

Rarity colours live in Core (`Strata.Core.Presentation.RarityColors`, `UiTheme.Rarity` converts) with Terraria's RGB:

| Tier | Name | Colour |
|---|---|---|
| -1 | Gray | (130, 130, 130) |
| 0 | White | (255, 255, 255) |
| 1–6 | Blue, Green, Orange, Light Red, Pink, Light Purple | (150, 150, 255), (150, 255, 150), (255, 200, 150), (255, 150, 150), (255, 150, 255), (210, 160, 255) |
| 7–11 | Lime, Yellow, Cyan, Red, Purple | (150, 255, 10), (255, 255, 10), (5, 200, 255), (255, 40, 100), (180, 40, 255) |
| -11 | Quest | (255, 175, 0) |
| -12 | Expert | a rainbow cycling through every hue |
| -13 | Master | fiery red pulsing towards orange |

## The global theme
`game/UI/UiTheme.Build.cs` builds one `Theme` in code for every control type the game uses (panels, labels, framed
buttons and the borderless `TextButton` variation, toggles drawn with the `toggle_on`/`toggle_off` sprites, dropdowns
and popups, tabs, sliders with the gold `slider_grabber` knob, scrollbars, progress bars, text fields, rich text and
tooltips). The code is the source of truth; `assets/ui/strata_theme.tres` is its export and the project theme
(`project.godot` `[gui] theme/custom`). `UiTheme.Install()`, the first thing `GameRoot.Boot` does, makes the UI font
ThemeDB's fallback (so `Label3D`, custom drawing and every CanvasLayer get it), merges the code-built theme into the
project theme and warms the font up. Controls therefore set no theme of their own. After changing the builder,
re-export with `godot --headless --path . -- --export-theme` (or `ui export-theme`); the `ui.theme_synced` probe fails
while the file is stale. Pixel sprites (`assets/ui/*.png`, `tools/textures/build_ui.py` from the ASCII maps in
`ui_sprites*.py`) load through `UiSprites`, which the HUD preloads.

## Layout model
`Strata.Core.Presentation.UiLayout.Compute(width, height, UiState)` places every HUD and inventory region (`UiRegion`)
on the logical viewport (the window over the UI scale; `UiScale` never lets it drop below 1024×600) as pure logic:
fixed regions keep Terraria's places, flexible ones (chat, info, crafting, the F3 overlay) shrink around them,
floating ones (breath, status line, boss and event bars, the inventory's Settings button) move to the nearest free
spot, and when the inventory does not fit its slots drop to 0.85 and then 0.75 (`SlotScale`) and the minimap hides.
`UiLayoutTests.NoVisibleRegionsOverlapOrLeaveTheScreen` checks every window from 1024×600 to 3840×2160 at every UI
scale step (0.75–2) and a dozen HUD and inventory states: no two regions share area, none leaves the screen and the
required ones are present. The engine applies a layout on resize and on state changes only, never per frame; each
placed control joins `LayoutWidgets.Group`, and the `ui.overlaps` probe checks the live tree, where real text and
sprites could outgrow the model.

W4-POLISH added the content check: `layout.overlaps` compares what each widget draws (its visible descendants, a
clipping scroll list counted by its own rectangle) rather than its region, so a slot or field that outgrew its region
is caught. Slots are sized with `ItemSlotView.Resize` (minimum size first; a control never shrinks below its minimum),
the chest's name field sits bottom-aligned on its caption row, and headless runs can lay out for another window with
`uiwindow <w> <h>` (ui.cmds checks 1280×720 and 1920×1080 at UI scales 1 and 1.5 with a chest and a shop open). The
boss bars stack one per boss group (`BossBarModel.Compute(npcs, list)`, at most three, the largest at the bottom):
`UiState.BossBars` sizes the region (as many as fit), and each bar fades in on arrival and out over 0.5 s. The
crosshair's hover name is Terraria's mouse text (hover size, below-right of the cross, bosses in purple), and
`MapSystem` retries a reveal that met ground not streamed in yet, so the minimap is never black at spawn.

## Input gate: overlays and modals
`game/UI/InputGate.cs` decides what the game hears while screens are open. A **Modal** (pause menu and settings, the
console, the world map, a chest's rename field, the inventory with auto-pause) sets `GameplayBlocked`: movement,
hotkeys and item use stop. An **Overlay** (the inventory, the NPC chat box, the chat history) frees the pointer
(`MouseFree`, no mouse look) but the player keeps walking, jumping, fighting and picking hotbar slots; the item is used
only while a button is held on empty screen, which `WorldClickCatcher` (CanvasLayer 9, under every panel) reports as
`WorldPointer`. `TextEntry` (a focused text field) blocks movement and hotkeys too.

## Controller (X-04)
Bindings live in `data/config/input.json` (`InputConfig`, checked by `Strata.Tools validate` and Core tests): every
action has keyboard/mouse bindings (`key:W`, `mouse:1`) and controller ones (`pad:a`, `axis:left_y-`, `axis:rt+`), the
menu actions `ui_*` included; a controller button may serve one gameplay action and one `ui_*` action, never two of
either. `KeyBindings` rebinds and saves the two devices separately (Settings → Controls has a column each, plus look
sensitivity, inverted look, stick dead zone and aim assist). `InputDevice` follows whichever device was used last
(every event passes `PadInput`, a child of the window); prompts (the crosshair's interact hint, drawn by `PadGlyph`)
follow it. On menus `PadInput` focuses the topmost menu's first control on the first D-pad/stick/A press, frames the
focus in gold and turns B into Esc; the engine's focus navigation does the rest. On the gameplay screens `PadCursor`
drives a cursor over every visible slot and button of the inventory (chest, shop, reforge, crafting, equipment) and the
chat box, stepping with `FocusNavigation` (Core: nearest in the direction, same row first); A/X/RB/LB click as the mouse
would (`ItemSlotView.Press` with Shift or Alt), B closes, the held stack and tooltip follow `PadCursor.Pointer`, and
`InputGate.PadNavigating` keeps the world from reacting (the player stands still, as on Terraria's gamepad inventory).
`CameraRig.Pad.cs` turns the right stick into look rates (`PadLook`: radial dead zone, curve) and applies `AimAssist`
(Core: friction near a creature in a small cone, magnetism toward it while steering; never on the mouse). The `pad`
smoke command sends real controller events (`Input.ParseInputEvent`), and `pad.*` probes report the cursor and assist.

## Slots and click routing
A slot is a `SlotRef(SlotArea, index)` over the player's storage (backpack, coins, ammo, trash, armour, accessories,
vanity, dyes, misc, misc dyes, the service slot), an open container or a shop. `ItemSlotView` draws one (tint by
`SlotKind`, icon at 62 %, stack count, ammo count for bows and guns, an empty-slot silhouette) and sends every click
to Core: `GameWorld.ClickSlot(player, slot, right, mods, SlotContext)` routes it as Terraria does (Alt favourites,
Ctrl trashes, Shift quick-moves, sells with a shop open or opens a crate on a right click) and otherwise applies
`SlotActions` (pick up, place, merge, swap, take one, equip) under the slot's `SlotFilters`. The held stack is
`PlayerInventory.Cursor`, drawn at the pointer by `CursorItemLayer`; closing the inventory stows it.

## Tooltip pipeline
Core builds the lines: `ItemTooltip` (Terraria's order and wording: name with prefix and count, adjusted combat stats,
info lines, the description or buff effect, prefix modifiers in green and red, the set bonus and the price), each line
tagged with a `TooltipKind`. `UiContext.Tooltip(stack)` adds the local player's context (class bonuses, worn set,
the town's sell or buy price). `TooltipFollower` (CanvasLayer 95) is the only tooltip: shown at once beside the pointer
(below the held stack), each line coloured by `UiTheme.TooltipColor` (rarity, good/bad, coin colours), rebuilt only
when the hovered stack changes or every 0.25 s for live text, from a fixed pool of 24 labels. `ShowText` shows plain
lines for other controls (the defence shield, buff icons).

## Canvas layers and HUD widgets
| Layer | Node | Contents |
|---|---|---|
| 9 | `ColorBlindFilter` | the colour-blind pass over the world view (game root; hidden when off) |
| 9 | `WorldClicks` | `WorldClickCatcher`: clicks that land on empty screen |
| 9 | `BossIntro` | a boss's title card and thin cinema bars (X-02); takes no clicks, the HUD draws over it |
| 10 | `Hud` | item name, hotbar, buff bar, life text, hearts, mana stars, breath bubbles, crosshair with the hover name and hints, status line, info panel, chat log, subtitles and the autosave spinner (placed around the regions), pickup popups (over the player, not a region), boss bar, invasion bar, NPC chat placement, death screen |
| 10 | `Minimap` | the framed minimap and its zoom buttons (above the click catcher) |
| 20 | `Inventory` | backpack grid, coin/ammo/trash/sort, panel host (chest, shop, reforge), crafting column, equipment column, Settings button |
| 22 | `TownDialog` | the NPC chat box |
| 30 | `WorldMap` | the full map (M); markers (spawn flag, bed, resident and boss heads, the player's arrow) are icons drawn by a `MapMarkerLayer`, named beside the pointer on hover |
| 40 | `PerfOverlay` | the F3 overlay |
| 45 | `PauseMenu` | dim and the pause panel, settings |
| 50 | `DebugConsole`, `MainMenu` | the console; the title screen and its menus |
| 90 | `CursorItem` | the stack held on the cursor |
| 95 | `Tooltip` | `TooltipFollower` |

## Performance rules
* Pool, never create per frame: slot views, tooltip labels, buff icons, recipe slots, shop cells and number labels are
  pooled and rebound; hidden views stop processing (`ItemSlotView.ActiveViews`, probe `ui.slot_views`).
* Redraw on change: widgets keep the last value they drew and call `QueueRedraw` or set text only when it changes
  (hearts, stars, timers on whole seconds, the crafting list on `CraftingEnvironment.Signature`); layouts are
  computed on resize and state changes only.
* No per-frame strings or LINQ on hot paths: text is rebuilt when its inputs change, cached strings are compared by
  reference first, and style boxes are cached (`UiTheme.SlotStyle`).
* Fonts are warmed at boot and sprites preloaded with the HUD, so opening a screen never loads or rasterises.
* Frame metrics (F3 and `perf report`): `gd.hud`, `gd.inventory_ui`, `gd.slot_views` and `gd.tooltip` time the UI's
  `_Process` work; each should stay well under 0.5 ms per frame at 1080p on the medium preset. The UI tour's
  `perf report` at 1920×1080, medium, on software rendering measured `gd.hud` 0.15, `gd.inventory_ui` 0.07 and
  `gd.slot_views` 0.04 ms per frame (`gd.tooltip` only runs while a tooltip shows).

## Screenshot tour
`game/Testing/Tours/ui.cmds` walks the whole UI in a tiny world's stone arena and saves 12 shots: the HUD, buffs, a
chest with the crafting column, crafting, the NPC chat box, the shop, the boss bar, the invasion bar, the death screen,
then the HUD, the inventory and the pause menu at UI scale 1.5; every shot first asserts `ui.overlaps == 0`. Run it
rendered at each resolution (the CI screenshot job runs it at 1280×720):

```
godot --path . --rendering-driver vulkan --resolution 1280x720 -- --world=ui-tour --size=tiny --new --player=tour \
      --fresh-player --view=2 --graphics=medium --shots=/tmp/shots --smoke=res://game/Testing/Tours/ui.cmds
```

## Screens and widgets (reference)
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
  hint), `InfoPanel` (info-accessory rows with the accessory's icon, FPS last, 4 Hz), the vitals (`Hud.Vitals.cs`:
  "Life: X/Y" over `LifeHearts`, `ManaStars` and `BreathBubbles`, each drawn in `_Draw` from the `heart`/`heart_gold`/
  `star`/`bubble` sprites with Core `VitalsDisplay` maths: 20 life per heart, golden per Life Fruit past 400 from the
  permanent maximum, the partial icon at 0.6 + 0.4·fill scale and 0.4 + 0.6·fill alpha, the last full heart beating
  at 15 Hz redraws, bubbles only below full breath and flashing red at 0; `UserSettings.LifeDisplay` "bars" swaps the
  hearts and stars for `VitalsBars`), the `BuffBar` (pooled `BuffIconView`s rebound only on `BuffSet.Changed`, a
  count change, or a change in the minions (`ProjectileSystem.CountMinions`, one icon per kind with its count, the
  staff's icon) or the minecart; countdowns from `BuffDisplay.Timer`, hidden where `ShowsTimer` is false, redrawn
  when the whole second changes, blinking in the last 5 s; while `InputGate.MouseFree` the icons take the mouse:
  hover shows `TooltipFollower.ShowText` with the name and `BuffText.Describe`, a right click goes through
  `BuffBar.Cancel` (`BuffSet.TryCancel` + `Recompute`, refused for debuffs and persistent buffs; minions go home via
  `ProjectileSystem.Unsummon`)), the pickup feed in the chat region and the boss bar. Text widgets draw through `OutlinedText` (no minimum size, clipped), so nothing outgrows its
  region; the minimap has a 9-slice `minimap_frame` and +/− buttons (zoom 0.5–6) on CanvasLayer 10, above the click
  catcher. Every placed control joins `LayoutWidgets.Group`; probe `ui.overlaps` counts intersecting pairs in the live
  tree (and logs them). Smoke: `ui f3`, `ui minimap in|out`; probes `hud.item_name`, `hud.status`,
  `ui.layout_width`, `ui.minimap_zoom`, `hud.hearts`, `hud.golden_hearts`, `hud.stars`, `hud.bubbles`, `hud.buffs`;
  `ui buff <cancel|hover> <id>`.
* **Inventory screen** (`game/UI/InventoryScreen*.cs`, `game/UI/Inventory/`): Terraria's layout on the HUD's
  `UiLayout` (the HUD feeds it `InventoryScreen.Panel` and `AccessoryRows` and calls `InventoryScreen.Place` from
  `LayoutApplied`; `LayoutChanged` lays it out at once on open, close or a panel change). The backpack's 10×5 grid
  sits on the hotbar's origin (first row = hotbar, selected slot gold); `CoinAmmoColumns` holds the coin and ammo
  columns, the trash slot under them (saved with the player) and the `sort`/`quick_stack` buttons; the panel host
  under the grid shows one panel at a time through `ShowPanel(Control, UiPanel)`/`ClosePanel()` (the chest, with 40
  pooled slot views rebound on open, a resident's shop via `OpenShop(TownNpc)`, or their service via
  `OpenService(TownNpc, ServiceMode)`) and sizes its slots by
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
  `ShopRequested` (the inventory's shop panel), Reforge likewise `ServiceRequested` (the reforge slot). Probe `ui.npc_chat_top` (open in the top third, overlapping no HUD
  widget; `LayoutWidgets.CountOverlapsWith`).
* **Shop panel** (`game/UI/Inventory/ShopPanel.cs`): a resident's shop in the panel host, opened by the chat box's
  Shop (`InventoryScreen.OpenShop`): their name over a 10×4 grid of 40 pooled read-only `ItemSlotView`s, the stock
  (`TownCommerce.Stock`, read again every second) in `SlotKind.Shop` cells and then the player's sold stacks
  (`TownCommerce.Buyback`, newest first) in amber `SlotKind.Buyback` cells, paged with `<`/`>` links past 40.
  Tooltips end with 'Buy price' (`UiContext.Tooltip(stack, price)`). Left click buys one onto the cursor
  (`BuyTarget.Cursor`, stacking on the same item), a held right button repeats at `HoldRepeat`'s pace, Shift buys into
  the backpack, a sold stack is bought back whole; holding anything else, a click on a cell or between the cells sells
  it (`SellCursor`), and Shift-clicking a backpack, coin or ammo slot sells through `GameWorld.ClickSlot` with the shop
  as its `SlotContext`. Coins jingle (at most every 50 ms); refusals go to the status line. The resident waits while
  the shop is open; out of `TalkRange` it closes, and putting it away (or talking to a resident) ends the
  conversation. Smoke: `ui shop`, `ui buy <item> [n]`, `ui sell <area> <slot>`, `ui buyback <i>`, `ui hover shop <i>`;
  probes `ui.shop_slots`, `ui.buyback`.
* **Reforge slot** (`game/UI/Inventory/ServicePanel.cs`, `CoinText.cs`): the chat box's Reforge
  (`TownDialog.ServiceRequested`) opens the inventory with the Goblin Tinkerer's service in the panel host
  (`InventoryScreen.OpenService(npc, ServiceMode.Reforge)`, the layout's `UiPanel.Service` region): their name over one
  slot bound to `PlayerInventory.Service[0]` (`SlotFilters` takes only `Prefixes.CanHave` items while
  `PlayerInventory.ServiceMode` is `Reforge`), 'Place an item here to reforge' under it while empty, else the
  `reforge_hammer` button beside it and 'Cost:' with each coin in its colour (`CoinText`; `TownServices.ReforgeCost` on
  the stack, worked out again on a change and once a second for the resident's mood). The hammer calls
  `TownServices.Reforge(npc, player, inv.Service, 0, …)` (paid from the purse; the held-item overloads delegate to it),
  which raises `Reforged`: `SoundEvents` plays the `reforge` anvil clang (`tools/audio/sfx_recipes_reforge.py`), the
  slot pops 1.1× → 1 and the tooltip is read again at once (hovering the hammer shows the item's tooltip too). The
  resident waits; out of `TalkRange` the panel closes, and putting it away (or closing the inventory) stows the item
  (`PlayerInventory.StowService`, dropped at the feet when the backpack is full) and ends the conversation. Another
  service (the Guide's help slot) adds a `ServiceMode`, its slot filter and its content beside the slot. Smoke:
  `ui reforge`, `ui reforge hit`, `invclick service 0 left`, `ui hover service 0`; probes `ui.service_cost`,
  `ui.reforges`, `ui.service_item`.
* **Menus** (`game/Menu/`, `game/UI/PauseMenu.cs`): the title screen shows `MenuLogo`, `assets/ui/logo.png` (rendered
  by `tools/textures/ui_logo.py` from the UI font: grass-to-earth gradient, bevel, 6 px outline, drop shadow; the text
  is the main menu's title, `build_ui.py --logo-text=...`), swaying (rotation 0.03·sin 0.6t, scale 1 + 0.02·sin t)
  and dimmed by `MenuBackground.Daylight`. `MenuKit.Big` words turn gold and grow to 1.08× about their centre under the
  mouse (`MenuKit.HoverGrow`, a 0.08 s tween on `Scale`, so no layout moves). The character and world lists are
  `MenuKit.RowPanel`s (blue, lighter with a gold rim under the mouse) with 32 px `MenuKit.Icon` sprite buttons
  (`menu_favourite`/`menu_favourite_off`, `menu_play`, `menu_delete`); starred saves are listed first and kept in
  `user://favorites.json` (`Strata.Core.Persistence.SaveFavorites`, keyed by file or directory name, as Terraria's
  favorites.json). The list is `MainMenu.CharacterList.cs`; the creator (`MainMenu.Characters.cs`) is a form of
  `MenuKit.Row`s, so new options are further rows. Settings take the global theme: tabs, the `toggle_on`/`toggle_off`
  sprites as `CheckButton` icons and a gold `slider_grabber` knob. The pause menu is a 0.45 dim with a centred blue
  panel of `MenuKit.Big` words (Resume, Settings, Save & Exit to Title, Save & Quit Game), still at
  `WorldHost/PauseMenu`; the inventory's bottom-right 'Settings' `TextButton` (layout region `settings_button`) opens
  it over the screen, and Resume leaves an auto-paused inventory's world paused (`PauseMenu.StayPaused`). Smoke:
  `ui settings`; probes `ui.paused`, `ui.settings_button`; the menu tour (`--menu-tour`) prints `MENU PASS`.
* **Accessibility** (X-12; Settings → Accessibility, `SettingsScreen.Accessibility.cs`): *Colour-blind mode*
  (protanopia, deuteranopia, tritanopia) and *Filter strength* drive `Rendering.ColorBlindFilter`, a layer-9 screen
  pass on the game root that daltonises the world view with `ColorVision.Correction` (Machado 2009 simulation, error
  shifted onto visible channels, one 3×3 matrix on linear RGB; under the HUD so text stays crisp); the interface swaps
  its key colours instead, since a matrix clips saturated UI colours together: `UiTheme.Vision` maps rarity names,
  tooltip bonus/penalty lines, chat lines, the boss banner and captions through `ColorVisionPalette`
  (`data/config/color_vision.json`, generated by `tools/content/gen_color_vision.py`; `validate` checks every group's
  closest pair stays ΔE ≥ 12 as the viewer sees it). *Subtitles* (`CaptionsPanel`, Hud.Accessibility) caption the
  sounds `data/config/captions.json` lists from `Sfx.Heard` (every `Sfx.Play`/`PlayAt`, plus `Sfx.Report` from the
  weather loops once a second and from thunder): Minecraft-style dark rows above the chat log
  (`HudOverlays.CaptionsRect`), coloured by kind, an arrow turned toward positional sounds as the camera moves, a line
  refreshed when heard again and fading 3 s after (`CaptionFeed`). The autosave spinner (`SaveSpinner`,
  `HudOverlays.SaveIconRect`: bottom-right or the nearest free spot) turns while `GameWorld.AutosaveInFlight`, at least
  1.2 s (`SaveIndicator`). Smoke: `access subtitles|colorblind|hear`, `save async`; probes `ui.captions`,
  `ui.caption_arrows`, `ui.saving`, `ui.save_icons`, `ui.colorblind`, `ui.colorblind_pass`, `ui.palette_swapped`.
