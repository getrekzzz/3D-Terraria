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
| 9 | P3-46, P3-31, P3-35, X-09 creator menu rows (after P2-30) | pending |

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
