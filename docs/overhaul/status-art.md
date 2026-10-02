# Art slice status (`claude/ovh-art`)

| Push | WPs | CI |
|---|---|---|
| 1 | P3-05 | full headless CI green (416 Core tests, all checks, 14/14 smoke, MENU PASS) |
| 2 | P3-37, P3-37b, P2-24, P5-28, P3-38, P3-39, P3-40, P3-41a/b, P3-42, P3-43, P3-44, P3-45, P3-50 | full headless CI green (422 Core tests, all checks, 14/14 smoke, MENU PASS) |
| 3 | P4-35, P4-36, P4-37, X-09 (all but the creator menu rows) | full headless CI green (426 Core tests, all checks, 14/14 smoke, MENU PASS) |
| 4 | X-17 hair/cloth motion | full headless CI green (430 Core tests, all checks, 14/14 smoke, MENU PASS) |
| 5 | P1b-06, P1b-07 (async saves, reviewed + fixed), P1b-08, P1b-09 (scheduler, budgets, colliders) | full headless CI green (502 Core tests, 14/14 smoke, MENU PASS), all review findings fixed |
| 6 | P1b-10, P1b-11, P1b-12 (tile objects, grass, GC) | full headless CI green (507 Core tests, 14/14 smoke, MENU PASS) |
| 7 | P1b-13, P1b-14, P1b-16 (threaded asset cache, preloading + warm-up, dynamic resolution; reviewed + fixed) | full headless CI green (518 Core tests, 14/14 smoke, MENU PASS) |
| 8 | P3-48 chest lids | full headless CI green (518 Core tests, 14/14 smoke, MENU PASS) |
| 9 | P3-46 movement feel | full headless CI green (14/14 smoke, MENU PASS) |
| 10 | P3-31 Terraria prefixes (reviewed + fixed) | full headless CI green (533 Core tests, 14/14 smoke, MENU PASS) |
| 11 | W3 merge (P2-05..08) | full headless CI green (640 Core tests, 16/16 smoke, MENU PASS) |
| 12 | P3-35 Piggy Bank and Safe | full headless CI green (642 Core tests, 16/16 smoke, MENU PASS) |
| 13 | P3-01' bursts and reuseDelay | full headless CI green (654 Core tests, 16/16 smoke, MENU PASS) |
| 14 | P3-18 pets and light pets, burst test isolation fix | full headless CI green (658 Core tests, 16/16 smoke, MENU PASS) |
| 15 | X-12 photo mode, Bosses + Dungeon integration merge | full headless CI green (1234 Core tests, 17/17 smoke, MENU PASS) |
| 16 | P3-04 swing curve (reviewed + fixed) | full headless CI green (1246 Core tests, 17/17 smoke, MENU PASS) |
| 17 | World integration merge | full headless CI green (1358 Core tests, 21/21 smoke, MENU PASS) |
| 18 | P3-47 buff and debuff visuals | full headless CI green (1358 Core tests, 21/21 smoke, MENU PASS) |
| 19 | P3-06 part 1 (voxel-lit held items and wings, real block cubes in hand) | full headless CI green (1358 Core tests, 21/21 smoke, MENU PASS) |
| 20 | P3-02/P3-03 integration merge | full headless CI green (1388 Core tests, 21/21 smoke, MENU PASS) |
| 21 | P3-06 grip per use style, P3-07 first-person poses, swing curve from `bladeCurve`, rapier melee prefixes, mounted `sit` | full headless CI green (1390 Core tests, 21/21 smoke, MENU PASS) |
| 22 | P3-29 hearts and mana stars (reviewed) | full headless CI green (1396 Core tests, 21/21 smoke, MENU PASS) |
| 23 | P3-29 review fixes, P3-36 3D dropped items | full headless CI green (1398 Core tests, 21/21 smoke, MENU PASS) |
| 24 | P3-27 sound sets and SFX voice pool | full headless CI green (1403 Core tests, 21/21 smoke, MENU PASS) |
| 25 | W4 integration merge | full headless CI green (1458 Core tests, 21/21 smoke, MENU PASS) |
| 26 | P3-34 bed sleep (reviewed + fixed) | full headless CI green (juice smoke re-run alone after a concurrent-run flake) |
| 27 | X-09 creator menu rows, World slice merge, held model hidden while a yoyo/flail/whip is out | full headless CI green (1493 Core tests, 21/21 smoke, MENU PASS) |
| 28 | P5-30 Guide book shell + Items tab, then P5-31, P5-32; per-style hand frames (needs a tuning pass) | pending |

Push 2 CI: full headless CI on the pushed head (file length, build, all Core tests, manifest/validate/coverage,
gen_reference, gen_ore_tiers, build_all --check, Godot import, every smoke script SMOKE PASS, --menu-tour MENU PASS).

## For the integration session
* **Sockets (P3-05)**: `PropLibrary.Socket(path, "grip"|"muzzle"|"tip"|"nock"|"string"|"chain"|"handle_end")`
  returns the mesh-relative `Transform3D` (−Z forward, +Y up). Guns are modelled in the item's YZ plane (+Y = top
  of the gun), so a held gun keeps its old orientation; the muzzle socket is at the barrel end.
* **Boss icons (P2-24)**: `AssetPaths.BossIcon(enemy)` / `PortraitIcons.Boss(enemy)` for the boss bar (64 px,
  outlined). Re-render with `build_all.py --boss-icons` after any boss model rebuild (the bosses session too).
* **Portraits (P5-28)**: `AssetPaths.TownNpcHead(npc)` / `CreatureIcon(enemy)`, `PortraitIcons` (lazy +
  `Preload`/`Pump`). Re-render with `--portraits [--only=ids]` after a creature rebuild.
* **Armour (P3-42)**: `PlayerAvatar.Armor.cs` reads `Inventory.Armor`/`Accessories` (`Changed` events). If the P2-05
  slot model changes those inventories (vanity), point `RebuildWorn` at the visible slots. Probe `avatar.armor_pieces`.
* **Ore glow (P3-37b)**: `BlockDef.glow` (data/blocks), graphics preset `oreGlintDistance`. Gem/hardmode/hellstone/
  luminite/meteorite `light` values were lowered (they still light caves, less like lamps).
* `tools/blender/build_all.py` planning moved to `tools/blender/asset_plan.py`; new spec sections `projectiles`,
  `armor`, `metalStyles`.

## Decisions and gaps
* No texture-array growth: P3-37's "silver layer" became a neutral-normalised `prop:iron:metal` slot; P3-43's fur,
  chitin and bone layers replaced the barely used `leaves`, `red_cap` and `copper` layers (their 6 users rebuilt).
* Armour sets are 1.5k–4k tris (the spec's 2k cap is exceeded by the scale/ornate presets; only the player wears
  armour). Pieces are rigid per bone, so very bendy joints (waist, knees) can show small gaps or clipping; female
  rigs are scaled per bone, not refitted.
* Torches and candles held in hand reuse tile-object GLBs, which have no sockets (`Flames` places the flame).
* P3-38 icon sharding was not needed (a full rebuild takes ~9 minutes).
* P3-44: giant worm / devourer / digger keep the Eater of Worlds parts (tinted); P3-50: skeleton casters keep human
  bodies in robes (the skeleton anatomy has no robe support).
* The demon eye / Eye of Cthulhu cornea is a clear layer the Blender icon renderer draws opaque, so their thumbnails
  show a pale iris (in game the iris shows through).
* The ores verification shots (dark room and torch-lit, medium preset) are in the session's scratch; numbers in
  `/root/.claude/plans/overhaul-specs/baselines/ores.txt` (dark mean 0.039, torch-lit 0.511, no clipping).

## Push 3 notes
* **Event bosses (P4-35/36/37)**: Mourning Wood, Pumpking, Everscream, Santa-NK1, Betsy, Mothron, Martian Saucer, Flying
  Dutchman and the four Celestial Pillars rebuilt (6k–12k tris; bgen/bosses_moons*.py, bosses_events*.py) with
  walk/fly/idle/attack clips; boss icons and portraits re-rendered. `pumpking_blade` exists as a part model for a
  future HandBrain swipe (then set pumpking `{"hands": false}`). Pillar shields: the renderer has no per-part shield
  toggle, so the generator's `shield: true` variant is available but not used (needs a ModelOverride swap while
  ShieldUp — a bosses/integration follow-up). Turret/cannon part generators exist but are not built.
* **Character creator (X-09)**: `Appearance` has 22 unisex hair styles (`HairStyles`), `ClothingStyles` (shirt, vest,
  coat), `EyeColor`, `UndershirtColor`, `Appearance.Random(rng)`; old saves default the new fields. Bodies are
  `player_<m|f>_<clothing>` (bald); hair is `assets/models/hair/<m|f>_<style>.glb`, worn on the head bone by
  `CreatureVisual.SetAppearance` (so `CharacterPreview` already shows it). The old per-hair bodies are gone;
  `anim/index.json` maps the new bodies to the same sets. Remaining: creator UI rows (clothing style, eye and
  undershirt colour pickers, Randomize) in game/Menu after P2-30.

## Push 4 notes
* **Hair and cloth motion (X-17)**: one damped spring per person (`Strata.Core.Presentation.SpringSway`, tested)
  anchored at the head; the character shader bends hair hanging below the head joint and loose cloth (vertex alpha
  marks robe skirts and coat tails; 34 humanoids rebuilt) by it, with wind flutter from `strata_wind`. Budget: graphics
  preset `swayDistance` (low 0 = off, medium 20 m, high 35 m, ultra 50 m). Deviation from the spec: no dangle bones or
  per-strand chains (one spring per character, applied per vertex by weight), so long hair swings as one piece; capes
  and banners do not exist yet. No rendered verification (a still shot cannot show motion).

## Lag-spike wave (taken over from the integration session)
* Baseline before any change (headless perf.cmds, small world, view 6): 95 frames >=33 ms of ~11,600, p99 30.5 ms,
  max 492.6 ms, 30 gen-2 GCs (1,217 ms pauses). Saved in `/root/.claude/plans/overhaul-specs/baselines/perf-before.txt`.
* After the wave (same flags, push 7 head; perf.cmds now also waits for `assets.pending == 0` and the warm-up, so the
  frame count differs): 25 frames >=33 ms of 8,455 (>=50 ms 5, >=100 ms 0), p95 11.0 ms (was 15.5), p99 16.0 ms (was
  30.5), max 64.2 ms (was 492.6), 7 gen-2 GCs (649.7 ms total pauses, was 1,217). Worst subsystem peaks: world_runtime
  50.9 ms (was 283), objects 4.7 (was 199), grass 7.5 (was 196), colliders 24.2 (was 93.7), music gone from the list
  (was 271). Most remaining spikes are ~30–35 ms gen-0/1 GC pauses or a single culling/streaming frame.
* `tick.liquids` peaked at 97.7 ms in the baseline (follow-up outside the P1b list, not changed directly); after the wave
  its peak is 13.4 ms in this run, so it is no longer a spike source here, but large liquid events were not stress-tested.
* P1b-14/16 review fixes: without GPU timing the resolution governor never steps down (a vsync-pinned frame could never
  step back up); the warm-up probe resets per world and its boxes follow the camera. Open (low): materials of models
  that are preloaded but not yet instantiated are not in the warm-up set.
* P1b-08/09 review fixes: budgets use main-thread CPU time (not vsync delta), stay at loading level until chunks
  within 2 of the focus have meshes, and a large backlog overrides the slow-frame shrink; collider priority clears by
  mesh version; light-edit neighbours remesh ahead of distance order.

## Push 8 notes
* **Chest lids (P3-48)**: `MeshBuilder.part(name, pivot)` exports a hinged child node (`lid`, origin at the back top
  edge); `PropLibrary.Part` returns its mesh and hinge; `TileObjectRenderer.Lids.cs` draws lids as a second MultiMesh
  per group and rewrites only swinging lids (100° over 0.15 s). `InventoryScreen.ContainerShown` drives it; probe
  `objects.open_lids` (builder.cmds). Gaps: no `chest_close` sound exists on this branch (P2-17 not here); only
  `chest`-generator objects have lids.

## Push 9-10 notes
* **Movement (P3-46)**: full held jump ~5.4 cells (probe `player.jump_apex_cells`, movement.cmds asserts 5-6); boots use
  a new `runSpeed` stat that builds up over `runUpTime` 1.2 s (holds in the air, resets on stopping); Sprint multiplies
  only that bonus. Run-up covered by Core tests only (no smoke).
* **Prefixes (P3-31)**: data/prefixes.json (83 Terraria 1.4.4 prefixes, groups per Terraria), old ids in
  data/config/aliases.json, validator rule against alias keys that are live ids. Rolled per unit on loot (enemy, boss,
  crate, chest) and shop buys; prefixes that no longer fit their item are dropped on player and chest load (item kept).
  Use time = base × (1 − useSpeed). Review fixes: chest-load fit check, Deadly/Celestial stats, use-time math, per-unit
  rolls. Shop prefix rolls consume the commerce RNG, so shopkeeper dialogue order shifts.

## Push 12 notes
* **Piggy Bank and Safe (P3-35)**: `TileObjectDef.SharedStorage`, `PlayerState.Banks` (40 slots per kind, saved in
  `PlayerSave.Banks`), `GameWorld.Storage(obj, owner)` used by the W3 container ops; `Wallet.TrySpend` pays purse →
  piggy bank → safe (shops, Nurse, reforge). Merchant sells the Piggy Bank, Mechanic the Safe. Gaps: the piggy bank is
  2×1×1 on the floor anchor (not table-top yet); town NPC arrival checks count purse coins only; no Defender's Forge /
  Void Vault.

## Push 13-14 notes
* **Bursts (P3-01')**: `ItemDef.ReuseDelay` (ticks, not prefix-scaled) and `AmmoPerBurst`; `ItemUse.Continue` fires
  the rest of a burst without input. Clockwork Assault Rifle: 3 shots 4 ticks apart, one bullet per burst, 26-tick
  period. The only burst item in data; a burst shot without ammo/mana waits out its timing.
* **Pets (P3-18)**: pets are projectiles bound to the Pet / Light Pet misc slots (`ProjectileSystem.Pets.cs`), fly or
  walk-hop after the owner, teleport past 30 m. Light pets take lights from the DynamicLights pool, capped by the
  preset's `petLights` (1/2/3/4). Batch: Shadow Orb, Crimson Heart, Fairy Bell, Magic Lantern, Wisp in a Bottle (light);
  Baby Penguin (Fish), Zephyr Fish, Baby Truffle. No Pet Bunny (no Goodie Bag) or Eater's Bone yet; Fairy Bell 4% gold
  chest and Wisp 2% are estimates; no pet buffs or visibility toggle.
* Fix found by the pets screenshot: projectiles created while their model was loading stayed a glow for life (P1b-13);
  `ProjectileRenderer` now rebuilds them a few times a second until the model is ready.

## Push 15 notes
* **Photo mode (X-12, photo part only)**: `game/UI/PhotoMode/` (controller, overlay, filter layer, commands) over
  `FreeCamera` (now tetherable). P toggles (rebindable `photo_mode`), F12/Enter take a picture (`photo_shot`) to
  `user://screenshots/<timestamp>.png`; pauses the simulation, hides every CanvasLayer from outside (no Hud files
  touched), lens maths in `Strata.Core.Presentation.PhotoLens`. Smoke `photo.cmds`. Gaps: Tab still opens the
  inventory in photo mode; free look ignores sensitivity/invert-Y; no gamepad; warm/cool filters are subtle.
  Autosave indicator, colour-blind filters and subtitles left to the integration session.

## Push 16 notes
* **Swing curve (P3-04)**: `SwingCurve` / `SwingPlane` in Core drive the melee hitbox (`MeleeSystem.Curve.cs`, tick-to-tick
  sweep in 15° steps, damaging angles capped at 80° above level so wide arcs never hit behind), the trail
  (`SwingTrail`) and the first-person arc (`ViewModel.Swing.cs`); thrusts hit only at extension ≥ 0.6. Review fix:
  behind-the-player hits from the scaled 165° wind-up. Gaps: the third-person body clip still lags the curve (needs
  P3-03's clips); curve constants are code, not use_styles.json (P3-02 owns that file); reforge while held does not
  rescale the third-person blade.

## Push 18 notes
* **Status visuals (P3-47)**: `BuffDef.visual` (tint, tintStrength, particle = EffectKit recipe, color, rate, rise,
  light). `StatusVisuals` multiplies status tints into the instance `tint` uniform (NPCs, player via
  `PlayerAvatar.Status.cs`) and runs pooled looping emitters (`EffectKit.Status.cs`), scaled by `effectsScale`, capped by
  presets `statusEmitters` (8/16/32/48, first come first served) and `statusLights` (0/1/2/3). 13 buffs covered; new
  Shadowflame debuff (applied by `shadowflame_bolt`). Command `buff <id|clear> [seconds] [player|npcs]`, probe
  `fx.status_emitters`. Gaps: no venom/electrified buffs exist; only the main body of multi-part creatures is tinted;
  night visibility of fire sparks untuned.

## Push 19 notes
* **Held items (P3-06 part 1)**: `CreatureVisual.Lit.cs` lit attachments (held item and wings take the body's eased
  voxel light and tint); `BlockItemMeshes` builds one cached 0.2 m cube per block id with the terrain layers
  (`shaders/block_item.gdshader` samples the terrain arrays, no new arrays; in the shader warm-up); third person
  `PlayerAvatar.Held.cs`, first person `ViewModel.Block.cs`. Probes `avatar.held_lit`, `avatar.wings_lit`,
  `avatar.held_block_cube`, `view.held_block_cube`. Gaps: glass-like blocks render opaque; model-less non-block items
  still use an unlit sprite card; grip per use style waits for P3-02.

## Push 21 notes
* **Swing curve**: `SwingCurve.Blade` now reads `use_styles.json` `styles.swing.bladeCurve` (converted −90°), the same
  curve the P3-03 body clips use; hitbox, trail and first-person arc share it; 80° damaging cap kept.
* **Grip (P3-06)**: held pose = style `hand` frame × inverse of the model's `grip` socket (`PlayerAvatar.Held.cs`); bow
  and staff frames tuned in data, probe `avatar.grip_error_cm`. Gap: swing/gun/throw/holdUp/eat/yoyo/flail/whip frames
  use the default grip and still need a screenshot-tuning pass.
* **First person (P3-07)**: per-style `fp` blocks in `use_styles.json` (rest, keys, tool keys, curve blade/extend,
  aimAtCrosshair, raiseToFace, recoil), `Strata.Core.Presentation.UseStyleFp`, `ViewModel.Poses.cs`; recoil per
  `ItemUse.Shot` (bursts kick per round). Probes `fp.kicks`, `fp.recoil`, `fp.depth`. Gaps: holdUp/eat/yoyo/flail poses
  are first guesses; yoyo/flail arm extension is not tied to the tether; aim uses the aim-distance approach (≤3°), not
  the muzzle socket at 20 m.
* `Prefixes.Groups`: `rapier` (shortswords) take melee prefixes; mounted players play `sit` when the clip exists.

## Push 22-23 notes
* **Hearts and stars (P3-29)**: Terraria 1.4.4 drop rules (last-hitting player, 1/6 roll, star if mana not full else
  heart if life not full, magic bonus star, 5-9 hearts on boss defeat; own RNG stream; `data/config/pickups.json`).
  Magnet pulls only what fits (`GameWorld.CanCollect/Collect`); `pickup` items heal on touch and never take a slot.
  API for P3-30: `GameWorld.SpawnManaStar/SpawnHeart/SpawnPickup/ApplyPickup`. Review fixes: coins fit when tidied
  (`Coins.FitsWhenTidy`); `PlayerInventory.Pickup` never stores pickups. Gaps: worm segment kills roll nothing; reuses
  heal/mana sounds.
* **3D drops (P3-36)**: `DroppedItemRenderer(.Visuals)`: real item models (async, one build per frame), block cubes,
  billboard only as a fallback; spin + bob; additive rarity halo (`shaders/drop_halo.gdshader`); lights from the
  DynamicLights pool capped by preset `dropLights` (0/2/4/6). Probes `dropvis.*`. Perf (headless, 150 drops spread 2 cells apart, 108 drawn after stack merging: 92 models + 16 cubes, 23 halos, 2 lights): `gd.drops` 0.172 ms/frame avg, 1.89 ms max (target < 0.5 ms met); `tick.drops` peaks 2.8 ms; two 160-200 ms frames in that run are not attributed to drops (largest listed counter 2.8 ms; not investigated). Gaps:
  drops cast no shadows; thin swords read narrow edge-on.

## Push 24 notes
* **Sound sets (P3-27)**: `Strata.Core.Presentation.ItemSounds` (item `soundSet` > use-style `sound`/`heavySound` >
  old fallback), `SfxVoicePool` (3 voices per sound id, steal oldest, 24 voices per 2D/3D bus, far 3D sounds skipped),
  validator checks sound ids. 12 new synthesised sounds (`tools/audio/sfx_recipes_items.py`). Probes `sfx.*`. Gaps: no
  listening pass; buff potions now play `potion` (louder than the old `heal`); stars/snore/piggy-bank sounds left to
  their WPs.

## Push 26-27 notes
* **Bed sleep (P3-34)**: `GameWorld.Sleep.cs`: using a bed sets spawn and lies down; while every living player sleeps
  safely (no boss, no kill-goal event, no hostile within 15 m) the clock runs 5x (extra ticks in multiples of 5).
  Wakes on hurt, death, mount, grapple, bed removed or input. `GameWorld.RealTick` (clock minus sleep ticks) drives
  per-entity staggers (liquid contact) and the once-per-second debuff damage. Held item hidden and camera at the lying
  head while asleep. Gaps: no snore sound; puff is a plain cloud.
* **Character creator (X-09)**: rows for all 22 hair styles, clothing (shirt/vest/coat), eye and undershirt colour, and
  Randomize (`Appearance.Random`); the menu tour randomises and checks the saved clothing. Gap: accessories at creation.
* **Tethered weapons**: `HeldTether.Out` hides the held model (third and first person) while a yoyo or flail is out or a
  whip lash is drawn; probes `avatar.held_hidden`, `view.held_hidden`.
