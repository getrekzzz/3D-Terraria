# Player-feedback overhaul: final summary (2026-10-03)

The overhaul planned in [`_PLAN.md`](_PLAN.md) (waves W0–W11 plus the X-* additions) is done. Four parallel sessions
built it ([PARALLEL.md](PARALLEL.md)) and the integration branch `claude/clever-goodall-kgex45` merged everything at
`394f0e82`. Full headless CI passes on the final tree: every check, all 24 smoke scripts and the menu tour. Each
feature's status, with its evidence, is in [`../AUDIT.md`](../AUDIT.md).

## What shipped

* **The reported bugs (W1).** Exposure no longer flashbangs the desert or a cave exit. Torches light about 10 m of cave
  on every preset. Terrain is smooth (GenVersion 10). Each swing plays its clip once. Hooks hold on natural ground.
  Bows draw and nock. Swords that shoot also swing. The body faces the crosshair. The crosshair names creatures, bosses
  and residents.
* **Lag spikes (W2).** Autosave writes on a background thread. Colliders, tile objects, grass and streaming run to a
  budget. Models load asynchronously with a shader warm-up. Projectiles are pooled. In `perf.cmds`, frames over 33 ms
  dropped from 95 to 25 and the worst frame from 492 ms to 64 ms.
* **Terraria UI (W3, W4, W5i).**
  - Blue theme, Terraria font and rarity colours; hearts, mana stars, buff bar, chat log and stacked boss bars.
  - Inventory, chests, crafting, NPC chat box, grid shop, reforge slot, housing tab, minimap and map icons.
  - Combat text, death screen, Smart Cursor, quick heal/mana/buff keys.
  - Boss title cards and per-boss themes, colour-blind modes, subtitles, autosave spinner.
  - Full controller support with aim assist and an inventory cursor.
  - No overlapping widgets from 1024×600 up.
* **Items and feel (W5, W6).**
  - Weapons and shots: one swing curve drives the hitbox, trail and first-person arc. Shots leave the muzzle. Guns
    flash and burst. Bolts glow and leave trails.
  - New weapon behaviour: real yoyos, flails and whips. Weapons break pots and cut grass. Each grappling hook has its
    own head and chain.
  - Mining and impacts: cracks stage on damaged blocks. Felled trees topple. Gore, and ragdolls for human creatures.
  - Hurt feedback: a red hurt vignette, camera punches and creature hit materials.
  - Loot and economy: 3D dropped items, Hearts and Stars from kills, Terraria's 83 prefixes, Piggy Bank and Safe.
  - World: Fallen Stars, bed sleep that speeds up the night, and Terraria jump heights and boots.
* **Art.**
  - Weapons and tools have their own silhouette per class and tier. Metal colours read apart and ores glow. Icons are
    outlined.
  - Held-item sockets and grips. Worn armour and vanity.
  - New creature bodies and rebuilt event bosses. Boss, NPC and bestiary portraits.
  - A deeper character creator, hair and cloth sway, and photo mode.
* **Bosses and the Dungeon (W7–W9, X-11).**
  - Boss parts that can be killed separately, Expert AI and enrage. Treasure bags and trophies.
  - Skeletron 1:1. The hardmode bosses up to Duke Fishron and the Empress.
  - Mimics (biome Mimics too), the Nymph and the Wyvern.
  - A 1:1 Dungeon (GenVersion 11): winding halls, rooms by role, locked chests and keys, and the vault of biome chests.
* **Town NPCs and the Guide book (W10, W11).**
  - All 29 residents with Terraria's move-in rules, rescues, happiness and services. Shops with tabs and currencies,
    plus the Travelling and Skeleton Merchants.
  - Residents sit and sleep, fight back and come back at dawn. Town pets, pylons, emotes and voices.
  - The Guide book on K: items with their sources, a bestiary with drop rates and a live 3D preview, progress, NPCs,
    tips and 49 achievements. Onboarding hints.
* **World and life (X-*).**
  - Weather: rain, storms, snow, wind and sandstorms. 16 ambience beds and the music variants.
  - Halloween and Christmas. Fishing power, crates and quest fish. Four wire colours with logic gates.
  - Five mounts. 14 furniture families, banners and editable signs.
  - The secret seeds, including Don't Dig Up (GenVersion 14).

## How to play-test the key changes

Make a new world: old worlds generated before GenVersion 14 are marked "old terrain". New characters are fine, and old
characters still load.

| Key | What to try |
|---|---|
| Tab or I (pad Y) | Inventory: Terraria slot clicks, Shift-click quick-move, the crafting list, equipment, and the Housing tab. Esc closes it. With a chest or an NPC chat open, the residents' name tags step aside. |
| E (pad X) | Interact: talk to residents (the chat box and the grid shop), open chests, sleep in a bed (night runs 5× faster). |
| K | The Guide book: Items (sources), Bestiary (kill a zombie, then look at its live model and drops), Progress, NPCs, Tips, Achievements. |
| H / J / B | Quick heal, quick mana and quick buff, which drink from the backpack without changing the held item. Carry a Lesser Healing Potion, take a hit and press H. |
| X (pad RS) | Smart Cursor: a pickaxe snaps to the nearest minable block and an axe to the nearest tree. |
| P | Photo mode: free camera and filters. F12 or Enter saves a picture to `user://screenshots/`. |
| G (pad D-pad up) | Quick mount: equip a Slimy Saddle (from King Slime) or the Fuzzy Carrot in the mount slot. |
| F (pad LT) | Grappling hook, from the Misc hook slot first. Try a Grappling Hook and an Ivy Whip on natural slopes. |
| T, Y, V | Emote wheel, cycle the block shape, first or third person. |
| Controller | Left stick moves and the right stick looks, with gentle aim assist. RT uses the item, A jumps. LB and RB step the hotbar, View opens the map and Menu pauses. On a screen, the D-pad moves a gold cursor: A clicks, X takes one, RB quick-moves and B closes. |

Secret seeds (type them as the world seed when you create a world):
* **For the Worthy:** one difficulty step harder.
* **Don't Dig Up:** the Underworld on top, the surface at the bottom under a rock roof; you spawn on ash.
* **Drunk World**, **Not the Bees**, **No Traps**, **The Constant**, **Celebration**.

Also worth trying:
* Summon a boss: the title card, the stacked boss bar, its theme and the victory sting.
* Walk the Dungeon: the keeper, then Skeletron, then the locked Gold Chests (Golden Key).
* Wait out a rainy night: weather, ambience and Fallen Stars.
* Shoot a gun, then throw a yoyo and a flail.
* Chop a tree.

Headless check of any area: run its smoke script, e.g.
`$GODOT --headless --path . --audio-driver Dummy -- --world=t --size=tiny --new --player=t --fresh-player --view=2 --smoke=res://game/Testing/Scripts/town.cmds`.

## Known gaps

* **Controls.**
  - No default controller button for quick heal/mana/buff, the Guide book, emotes or photo mode. Every standard
    button is taken, and a binding is a single button: there are no hold-modifier combos. Players can bind a spare
    button (Share, paddles, touchpad) in Settings > Controls.
  - Photo mode's free look ignores the look sensitivity and invert-Y settings and has no controller support.
* **Avatar and items.**
  - Dyes are sold and equipped but not drawn on the avatar; seasonal vanity is not drawn either.
  - Held-item hand frames need a tuning pass for these use styles: swing, gun, throw, holdUp, eat, yoyo, flail and
    whip. The first-person holdUp, eat, yoyo and flail poses are first guesses.
  - Armour pieces are rigid per bone and can clip at the waist and knees.
  - Glass-like held blocks draw opaque.
* **Bosses and creatures.**
  - Moon Lord and the event bosses run pattern loops, not Terraria's full AI.
  - Deerclops has no screen-darkening insanity. Brain of Cthulhu illusions are darkened, not translucent.
  - Pillar shields have no model swap.
  - Treasure bags drop one per kill.
  - There are no hardmode variants of old creatures.
  - Missing buffs and debuffs: Darkness, Silenced, Broken Armor and Venom.
* **Town.**
  - Pylons do not enforce the one-of-each limit or the boss/invasion block.
  - Golf has no scoring.
  - Some sold items do not exist yet: Defender sentries and armour, the Clentaminator, the Imbuing Station.
  - The Guide book's preview lighting is a little dark.
* **World.**
  - Don't Dig Up has no weather or sky under its roof, and invasions and meteors land on the bottom surface.
  - The horizon shows no caves, structures or hardmode wedges. Liquids have no pressure.
  - Fishing has no junk catches or hardmode crates. Statues are crafted only. Lamps do not show on/off.
  - Rain does not wet props.
  - Crack decals draw over water and glass in front of a damaged block.
* **Tooling.**
  - Rendered menu tours (lavapipe) stall at the character creator's turntable; headless runs pass.
  - Frame times are not measured on a real GPU.
  - `gen_color_vision`, `build_emotes` and `build_audio` checks run locally only: the CI job lacks numpy, Pillow and
    soundfile.
* **Out of scope ([AUDIT §17](../AUDIT.md)):** Journey mode powers, background walls, co-op multiplayer.
