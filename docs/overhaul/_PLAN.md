# Plan — Player-feedback overhaul: fixes, Terraria UI/items/feel, bosses + 1:1 Dungeon, NPCs, Guide book, performance

## Context
Player feedback on `main` (M4, commit 15bf3f4). The problems reported:
- Jagged, unpredictable terrain.
- Tool swings look fast-forwarded.
- Many weapons and tools don't work (grappling hooks), and the ones that do (the bow) have no animation.
- Torches light only a tiny radius.
- The body faces the wrong way when aiming or interacting.
- The desert flashbangs on every preset except low.
- Bosses should look and play like Terraria, with Skeletron and the Dungeon matching it 1:1.
- The NPC system should be complete, as in Terraria.
- The crosshair should name mobs and bosses.
- Lag spikes.
- No guide book.
- The UI, items and moment-to-moment feel must be much more Terraria-like and polished. This is the player's top priority.
The player also asked for a full audit of what works, what doesn't and what's missing. The intended outcome is all of the above, delivered in pushed phases, without new lag.

### Player decisions (2026-09-30)
* Old worlds are disposable: bump `GenVersion`, re-pin the golden hashes, and keep no compatibility paths. A warning appears when loading an old world.
* In third person the **body always faces the crosshair**: A/D strafe, the legs twist toward the movement direction, and new strafe clips are added.
* The UI follows the **faithful Terraria** look: blue translucent panels, a bold outlined font, hearts and stars, rarity colours, Terraria wording, floating combat text and a chat log.
* **Everything is in scope**, committed and pushed to `claude/clever-goodall-kgex45` after each wave.
* World and creature visuals stay realistic 3D; the UI, items and feel read like Terraria.

## Audit summary (what works / what's broken / what's missing)
Sources:
- Four read-only audits, plus a completeness critic (≈110 UI/items/feel findings).
- Detail: `…/subagents/workflows/wf_4c38ab93-446/journal.jsonl` and the audit agents' reports.
- Committed in W0 as **`docs/AUDIT.md`** (a full table with file:line references and status). It is refreshed at every phase gate, together with the stale `docs/3D_TERRARIA_MASTER_PLAN.md` status table.

**Works**:
- The world generation pipeline, streaming, saving, mining and building, liquids, crafting and mana.
- Accessories, set bonuses, invasions and moons, wings, reforge data and paint.
- Magic, guns, thrown weapons, fishing, potions, summons and buffs.
- 29 town residents, 13 boss brains and the rebuilt early/mechanical boss models.
- A music director and 177 sounds, the map and minimap, and the Skeletron/Old Man/Guardian/Clothier flow.

**Root causes of the reported bugs**

| Complaint | Root cause (file) |
|---|---|
| Desert flashbang (medium and above) | Auto-exposure window 85/170 ISO pins the multiplier to 1.65–3.3×, so it can never darken. `autoExposure` is off only on low. Glow threshold is 1.0 with level weights summing to 4.5. The fog start is pushed to about 149 m and the horizon is fully lit (`game/Ambience/EnvironmentController.cs:60-98`, `data/config/graphics.json`). |
| Tiny torch light | 4-bit baked light decays 1 level per 0.5 m (octahedral, about 4 m on diagonals), then the `x*sqrt(x)` curve (`shaders/include/voxel_light.gdshaderinc:26-37`). Placed-light energy is 0.79 and the dynamic-light pool is 0 on low (`game/Rendering/DynamicLights.cs:65,127`). |
| Jagged terrain | 3D overhang density `1.5+rug*11` over 3 octaves (`WorldGen/Fields/TerrainField.cs:33-42`). Ridged 5-octave mountains on a bilinear 8-cell grid. Biome ramps at `Membership*3`. Caves carved from the 2D height. A binary threshold causes stair ripples. |
| Swings fast-forwarded | Clips have a wind-up; the strike lasts about 90 ms. The clip re-plays every `useTime` (< `useAnimation`), so tools never finish a swing (`PlayerAvatar.Motion.cs:45-47`). Speed bonuses shorten only the cooldown. The first-person view model snaps. |
| Hooks don't work | `ItemAction.Grapple` has no branch in `ItemUseSystem.Use`, so clicking does nothing. On F, the anchor check nudges 5 cm into a voxel, but the smooth collider sits up to 0.25 m off, so the hook releases at once on natural terrain (`PlayerController.Grapple.cs:106-111`). |
| Bow has no animation | The arrow spawns at tick 0 from the eye while the clip releases at t 0.6. First person is an 8 cm lift. Failures (no ammo, no mana, blocked) are silent. |
| Swords that shoot never melee | `Shot` fails the melee gate (`ItemUseSystem.cs:154`); Zenith returns `Blocked`. |
| Facing the wrong way | The aim ray starts at the **camera**, 4.2 m behind the player, and accepts hits behind the player (grass, bushes, tree boxes the camera probe ignores). The aim then points backwards and the body turns more than 55° (`InteractionController.cs:97-110`). |
| No mob or boss names | The HUD names only blocks and objects in reach; there is no entity picking (`Hud.cs:211`). |
| Lag spikes | Synchronous autosave and region rewrites in the physics tick every 120 s. Unbudgeted near-player Jolt collider builds. Tile objects rebuilt twice per remesh. Grass created and freed. First-use GLB loads on the main thread. Per-projectile materials and lights. Per-frame LINQ. Large-object-heap allocations cause gen2 GC. Dynamic resolution oscillates. |

**Broken or stand-in**:
- Yoyos, flails and whips are stand-ins.
- Opening the inventory freezes the player while the world keeps running.
- The crafting list rebuilds every 0.5 s, which kills its tooltips.
- Rarity 9–10 items show as yellow.
- Tooltips ignore prefixes.
- Skeletron's hands, Prime's arms and Golem's parts share one life pool. The boss `Parts` mechanism exists but only Moon Lord uses it.
- Eater of Worlds and the Destroyer can't split or lose segments.
- Dungeon chests roll the cave-cabin loot.
- Wrong-archetype creature models (the wolf is a unicorn, the Black Recluse a crab, …).
- All 42 swords share one mesh; metal colours are muddy; icons are unoutlined and guns render edge-on.

**Missing**:
- UI: a UI font, hearts and stars, buff icons, coin and ammo slots, a saved trash slot, sort and quick stack, vanity, dye and misc slots (hook, mount, pet, light pet), a chat log, pickup popups, a housing menu, smart cursor, a grid shop, the NPC chat box, and a custom cursor.
- Effects: projectile trails and muzzle flash, per-creature hit sounds, gore and blood, trees falling, crack overlays on every damaged block, hurt feedback, jump and land dust, and debuff visuals.
- Hearts and mana stars dropped by enemies, Fallen Stars, Terraria prefixes, treasure bags, quick heal/mana/buff keys, and bed sleep.
- Armour drawn on the player, pets and light pets.
- The 1:1 Dungeon: winding halls, furniture, spikes, books and the Water Bolt, locked chests with Golden Keys, dungeon loot items and the bound Mechanic.
- Boss behaviour: Expert behaviour, natural boss spawns, and the Wall of Flesh's tongue and leeches.
- NPC rescues, all shops and services, residents that fight, day-only move-in and dawn respawn, happiness, pylons and town pets.
- Bestiary, guide book and achievements.

## Execution approach
* **Toolchain bootstrap** (W0, then verify on every wave):
  - `apt-get install -y dotnet-sdk-8.0 xvfb mesa-vulkan-drivers libvulkan1 fonts-comic-neue fonts-sil-andika`
  - `tools/ci/get-godot.sh` (Godot 4.7.2 mono)
  - `python3.13 -m pip install bpy==5.2.2` (the wheel is already cached in the scratchpad)
  - `pip install numpy Pillow`
  - Then `godot --headless --import`.
  - I'll suggest the player add these commands to the environment's setup script.
* **Durable specs**: in step 0, the full per-WP designs are extracted into one JSON file per WP under `/root/.claude/plans/overhaul-specs/`, with the integration amendments below merged in. That directory survives container restarts. They come from the design workflow journal (`…/wf_971cbcf1-5d5/journal.jsonl`, 6 planners plus the integration critic, about 160 WPs). Each WP spec lists design steps, files, data, tests, verification and risks.
* **One Workflow run per wave (W0–W11).**
  - Each wave has two lanes. At most two agents run at once, which is the container's CPU cap.
  - Each WP is an implementation agent in its own git worktree, starting from the previous WP branch in its lane. It commits to `ovh/<WP>`; copy `.godot/` in to skip a full re-import.
  - After implementing, an adversarial review agent checks the diff against the spec, the conventions (Core never references Godot; files ≤400 lines; data-driven content; determinism) and allocations on hot paths. That agent fixes or rejects the change.
  - A wave-gate integrator merges both lanes into `claude/clever-goodall-kgex45`, runs the full CI locally and fixes integration breaks. I then push with `git push -u origin claude/clever-goodall-kgex45`.
  - Workflows resume from their journal after a container restart.
* **Serialisation rules** from the integration critic:
  - Hub files (ItemUseSystem, GameWorld, NpcSystem, Hud, InventoryScreen, TownDialog, PlayerAvatar and ViewModel, InteractionController, PlayerController, SoundEvents, CombatEffects, ProjectileSystem and ProjectileRenderer) are edited in the WP order the critic listed.
  - Only one agent at a time touches the Blender pipeline, `WorldGenTests.cs`/`GenVersion.cs`, or the human animation GLBs. There are only two animation rebuilds, P1-12 and P3-03.
  - `data/manifest.json` is always regenerated with `Strata.Tools manifest`, never hand-merged.

### Cross-cutting amendments (from the integration critic; apply to the specs)
* **P0-01** does every shared file split once, so later WPs can grow without breaching the 400-line limit and don't each redo the same split:
  - Move `PlayerEntity` into its own `PlayerEntity.cs`.
  - Split `ItemUseSystem` into `.Shooting.cs` and `.Consume.cs` partials.
  - Split `NpcSystem.Death.cs` out of `NpcSystem`.
  - Move `BossInfo` and related types out of `EnemyDef.cs`.
  - Split `ContentValidator.World.cs` by concern.
  - Create partials for `WorldLauncher`, `PlayerController`, `InteractionController` and `GameWorld.Saving`/`GameWorld.PlayerFiles`.
* **P0-02** adds extension points:
  - `[ProbeSource]` probe discovery.
  - `sfx_recipes_*.py` merging.
  - `tools/blender/assets.d/*.json` plus a `GENERATORS` registry.
  - Per-WP blocks in the smoke scripts.
* **Duplicate designs** are resolved to one owner each:
  - P1 owns: use timing (P1-10, with `data/config/use_styles.json` holding the clip windows), shot origin (P1-13 `ShotGeometry`), sword+projectile items and max-active (P1-16), dry-fire feedback (P1-15), and grapple click and anchors (P1-08/09).
  - P2 owns the slot model (P2-05; favourites are stored per slot, not on `ItemStack`), coins and ammo (P2-06), and the boss bar and icons (P2-23/24).
  - Reduced or merged: P3-01 is gun bursts only; P3-16 is dissolved into P3-18/P3-17'; P3-28 is dropped; P3-32 merges into P4-04; P4-22 merges into P5-06b; P5-07's rest poses fold into the P3-03 rebuild.
* **Correctness fixes**:
  - Mining speed does not scale `useAnimation`; melee speed and prefixes do.
  - `Npc` gains the allocation-free `VolumeCount`/`VolumeAt` accessors.
  - Boss parts get `DiesWithBody` and `InBossBar` (defeat flag and boss bar) in P4-01, which also switches `BrainContext.Target` to an `ICombatTarget` type and fixes Expert gating to `max 2`, so Journey worlds get no Expert drops.
  - `ChunkHash` hashes block string ids, and the golden chunks are re-picked.
  - The inventory overlay uses a separate `OverlayOpen` flag, so `GameplayBlocked` keeps meaning "modal open".
  - The `inv.<item>` probe counts coin and ammo slots.
  - Alias keys may not collide across content categories.
  - Smoke scripts wait with `wait_until assets.pending == 0` after threaded loading lands.
  - `perf.cmds` asserts deterministic counters only.
  - Lighting acceptance is numeric via `tools/ci/shot_stats.py`: desert and snow noon mean luminance in [0.35, 0.6] with under 2% clipped pixels; cave exit checked at +0.5 s and +2 s.
* **New WPs added for coverage**:
  - P3-46: movement feel. Terraria jump height relative to the body, a run-up for boots, and sprint only with a bonus.
  - P3-47: buff and debuff visuals on the player and creatures.
  - P3-48: opening chest lids.
  - P3-49: music variants, ambience and the fishing bite sound.
  - P3-50: a themed-humanoid kit, plus fur, scale, chitin and bone textures in P3-43.
  - P4-38: bevelled furniture.
  - P5-34: pylons.
  - The Clothier Voodoo Doll, added to P4-20 and P4-11.
  - P1b-17: Euclidean light spread, which is optional if torches still feel small.
  - A rarity colour table in Core, done as part of P2-01.
  - An outdated-world warning in the menu, done as part of P1b-02.
* **Recorded out of scope**:
  - Mounts beyond the minecart (Slimy Saddle first). The data model gets a mount slot now.
  - Background walls. Voxel rooms already enclose space, and the Dungeon uses wall panels instead.
  - Journey mode powers and wiring expansion.

## Lean mode (player decision 2026-10-01, after W1a took ~8.5 h, 6+ h of it software-rendered lighting tuning)
* No software-rendered tuning loops (one short verification shot at most, only where a spec requires visuals).
* Adversarial review only for risky gameplay/Core/save code (`review` list per wave); data/docs/art/UI-only WPs skip it.
* Small WPs bundled into one agent; gates still run the full headless CI before every push.
* Execution order after W1b: **W3 → W4 (UI) → W5 → W6 (items/feel, incl. P3-05 sockets, P3-37 metal colours,
  P3-37b vibrant ores) → W2 lag (P1b-06..16; P1b-17 dropped) → W7 bosses (+P2-24 boss icons) → W8 Dungeon → W9
  hardmode bosses (+P4-29 models) → W10 NPCs → W11 Guide book → W12 cosmetic art** (P3-38/39/40 icons and weapon
  silhouettes, P3-41a/b + P3-42 visible armour, P3-43/44/45/50 creature models, P4-35..38). Script:
  `/root/.claude/plans/overhaul-specs/_wave_lean.js`.

## Parallel sessions (player asked for more speed without lower quality, 2026-10-01)
Slices, branches and rules: `docs/overhaul/PARALLEL.md` (pushed, commit 508ff3b).
- **Bosses + Dungeon** (W7–W9, plus P4-29 and P4-38): session_01UZumEtLLjARMvU5vJPAkgE on `claude/ovh-bosses`.
- **Art** (P3-05 first, P3-37, P3-37b ores, P2-24, P5-28, W12): session_01JsSQnzkocPjTeHJVzDc1gE on `claude/ovh-art`.
- **NPCs + Guide book** (W10–W11): a third session, created after the W4 UI push.

- **World & life** (X-01, X-08, X-06, X-13, X-15, X-14, X-03, X-07): session_01Hhweod2WDaG1QE1rou5pQY on `claude/ovh-world`.

AAA additions X-01..X-17 (specs in `docs/overhaul/specs/X-*.json`, owners in PARALLEL.md). This session adds X-02 boss intros (after W4), X-16 ragdolls (after P3-20), X-12 photo mode/accessibility, and X-04 gamepad (last). Co-op multiplayer is not in scope unless the player asks.

This session runs W1b → W3 → W4 → W5/W6 (merge `origin/claude/ovh-art` before P3-06) → W2. It then merges every branch with full CI and pushes.

## Never stall again (2026-10-01)
* **Root cause of the ~9.5 h stall** (11:00 → 20:35 UTC):
  - The cloud kept this session's permission mode as "plan" from its first turn, even after plan mode was exited locally.
  - When the container restarted at ~11:00, the session came back in plan mode and stopped at a plan-approval prompt until the player returned.
  - The player re-saved the mode as Auto; the server record now reads `auto`.
* **Watchdog**: trigger `trig_018u3n44soKiE37wQbq7KBKx` fires hourly (minute 4). Each run it:
  - relaunches interrupted waves from committed lane state;
  - nudges idle sub-sessions;
  - never waits for approval;
  - stays silent while all is well.
  
  Delete it when the overhaul is fully merged.
* Lean wave script: `/root/.claude/projects/-home-user-3D-Terraria/dc0e60bf-573e-514c-9491-842b6315cce6/workflows/scripts/overhaul-wave-lean-wf_08d16710-07f.js`.

## Recovery after the 2nd container restart (W1b interrupted, 2026-10-01)
State on disk (toolchain, worktrees, specs and journals all survived):
- `ovh/laneA` @ da9dc16: P1-06 is committed and reported done. Its review was interrupted, leaving untracked `game/Debug/TmpFacingProbe.cs` and `game/Testing/Scripts/tmpfacing.cmds`.
- `ovh/laneB` @ f948979: P1b-02 is committed but was interrupted before its verification and report.

Steps:
1. Delete the two untracked temp files in laneA. Then finish W1b with the lean script (`_wave_lean.js`, review list `P1-07, P1-09, P1b-03, P1b-04`):
   - Lane A: `[P1-07]` → `[P1-11]` → `[P1-09]` (after P1b-03, merging laneB) → `[P1-14]`. The P1-07 agent first verifies P1-06 (diff a8f910d..da9dc16: build, Core tests, combat/movement smoke, facing probe) and fixes anything broken.
   - Lane B: `[P1b-02 finish, P1b-03]` → `[P1b-04]`. The first agent checks P1b-02 against its spec and finishes whatever is missing: golden hashes re-pinned by string id, the outdated-world guard, and the `--compare` output against `baselines/wg-before`.
2. The gate is also the Phase 1 gate:
   - full headless CI;
   - worldgen `--compare` numbers;
   - one short rendered terrain/hillside shot set (no tuning loop), sent to the player;
   - AUDIT.md and master-plan updates;
   - push "Phase 1".
3. Continue the lean order: W3 → W4 → W5 → W6 → W2 → W7 → W8 → W9 → W10 → W11 → W12.
   - Lane branches persist on disk across restarts. After a restart, inspect `git log` on each lane plus the workflow journal, then relaunch only the unfinished items with a "finish/verify the interrupted WP" note; do not blindly resume.

## Waves (each ends with the full CI and a push)
| Wave | Lane A | Lane B | Push |
|---|---|---|---|
| **W0** prep | P0-01 file splits, P0-02 extension points, toolchain, `docs/AUDIT.md` | P1b-01 terrain metrics baseline, P1b-05 spike instrumentation and `perf.cmds` | "W0 prep". Golden hashes and smoke output identical to HEAD |
| **W1a** reported bugs 1 | P1-05 eye-plane targeting and Reach → P1-16 sword+shot and Zenith → P1-10 use timing → P1-15 failure feedback → P1-13 shots from the hand → P1-08 Core hook | P1-02 lighting config and exposure model → P1-03 exposure, glow and fog fix → P1-04 torches → P1-12 re-authored swing/mine/bow and strafe clips | "Phase 1a" |
| **W1b** reported bugs 2 | P1-06 body faces the crosshair → P1-07 crosshair names (mobs, bosses, segments, residents) → P1-11 swing visuals → P1-09 engine hooks → P1-14 bows and ranged poses | P1b-02 smooth relief (GenVersion 10, outdated-world guard) → P1b-03 tamed overhangs and cave mouths → P1b-04 sub-cell height smoothing | P1-17 gate, then "Phase 1: reported bugs fixed" |
| **W2** lag spikes and item art | P1b-06 → 07 async saves → 08 scheduler and budgets → 09 colliders → 10 tile objects → 11 grass → 12 GC → 13 threaded asset cache → 14 preload and warm-up → 15 projectile pools → 16 dynamic-resolution hysteresis (→ 17 optional) | P3-37 metal colours → P3-37b vibrant glowing shiny world ores → P2-24 boss icons → P3-05 held-item sockets → P3-38 Terraria-readable icons → P3-39 per-tier silhouettes → P3-40 iconic weapons | Two pushes: after P1b-09 and at the end of the wave |
| **W3** UI foundations | P2-04 tooltips and display helpers → P2-05 slot model → P2-06 purse and ammo → P2-07 click semantics → P2-08 container ops → P2-19 combat text | P2-01 theme, font and rarity → P2-02 UI sprites → P2-03 buff icons → P2-09 UiLayout → P5-24a ItemSources → P2-20 enemy health bars | "Phase 2a" |
| **W4** Terraria HUD and inventory | P2-10 slots, cursor and tooltips → P2-11 no freeze plus input review → P2-12 HUD layout → P2-15 inventory → P2-13 hearts, stars and bubbles → P2-14 buff bar → P2-21 chat log → P2-22 pickup popups → P2-23 boss and invasion bars → P2-25 death screen → P2-29 smart cursor | P3-02 use styles and stack migration → P3-03 item clips and rest poses → (after P2-15) P2-16 equipment → P2-17 chest panel → P2-18 crafting → P2-26 NPC chat → P2-27 grid shop → P2-28 reforge slot | P2-30 menus and P2-31 UI tour, then "Phase 2: Terraria UI" |
| **W5** weapons and juice | P3-04 swing curve → P3-06 grips and lit held items → P3-07 first-person poses → P3-10 muzzle flash → P3-11 trails → P3-12 yoyos → P3-13 flails → P3-15 whip visuals → P3-17' hook heads and equipped hook → P3-18 pets | P3-01' bursts → P3-09' muzzles and sky shots → P3-08 EffectKit → P3-14 whips (Core) → P3-19 creature hit sounds and dust → P3-20 gore → P3-21 object hits → P3-22 falling trees → P3-23 swords break pots and cut grass → P3-24 Terraria mining hits and cracks | "Phase 3a" |
| **W6** feel and loops | P3-25 hurt and impact → P3-26 dust and splashes → P3-46 movement → P3-33 quick keys → P3-34 bed sleep → P3-47 debuff visuals → P3-36 3D drops | P3-27 sound sets → P3-29 hearts and stars → P3-30 Fallen Stars → P3-31 Terraria prefixes → P3-35 Piggy Bank and Safe → P3-48 chest lids → P3-49 music and ambience | P3 gate, then "Phase 3: items and feel" |
| **W7** boss framework, early bosses, Skeletron | P4-01 → 02 → 03 → 04 bags and trophies → 05 natural arrivals → 06 King Slime and Eye → 07 Eater splitting → 08 Brain → 09 Queen Bee and Deerclops → 10 Skeletron hands, spin and skulls → 11 Skeletron loot | P3-41a/b wearable armour → P3-42 armour on the body → P3-43/44 fixed creature archetypes → P3-45 common-enemy fidelity → P3-50 themed humanoids → P4-29 Golem and Plantera models | "Phase 4a" |
| **W8** 1:1 Dungeon | P4-12 blueprint index → P4-14 layout planner → P4-15 carving (GenVersion bump) → P4-16 entrance keep → P4-19 decorator → P4-21 locked chests and keys → P4-34 biome chests → P4-23 integration | P4-13 wall panels → P4-17 dungeon furniture → P4-18 banners, statues, books and spikes → P4-20 dungeon loot, keys and Voodoo Doll → P4-33 biome-chest weapons | "Phase 4b: Dungeon" |
| **W9** hardmode bosses | P4-24 Wall of Flesh → 25 → 26 Queen Slime → 27 Destroyer and Twins → 28 Prime arms → 30 Plantera → 31 Golem → 32 Duke and Empress | P4-35/36/37 event-boss rebuilds → P4-38 furniture bevel | "Phase 4: bosses" |
| **W10** town NPCs | P5-01 population rules → 02 housing validity → 03 conditions → 04 move-in rules → 05 housing API → 13 services → 12 happiness → 06a/b rescues (including the Dungeon Mechanic) → 09 enemies target residents → 10 residents fight → 11 sit and sleep → 19 visiting merchants → 20 town pets | P5-28 portraits → 07 animator states → 29 housing menu → 14 Guide crafting help → 21 Stylist → 23 Angler → 15a/b, 16, 18 shops → 08 rescue presentation → 22 Strange Plants (GenVersion bump) → 34 pylons | "Phase 5a: NPCs" |
| **W11** Guide book | P5-24b drop rates → 25 player journal → 26 achievements → 27 progression tips → 17 Zoologist and Golfer shops | P5-30a/b book shell and Items tab → 31 bestiary → 32 bosses, NPCs, tips and achievements tabs | P5-33 final gate, then "Phase 5: NPCs + Guide book" |

Order rationale: the reported bugs come first, then lag. Next the UI, items and feel, which are the player's priority. Then bosses and the Dungeon, NPCs, and the guide book. The Blender/art lane runs alongside, one agent at a time.

## Critical files (hubs, touched in a fixed order)
Core, `src/Strata.Core/`:
- `Session/GameWorld*.cs` and `PlayerEntity.cs`
- `Gameplay/Interaction/ItemUseSystem*.cs`, `Targeting.cs`
- `Gameplay/Entities/NpcSystem*.cs`, `MeleeSystem.cs`, `ProjectileSystem*.cs`
- `Content/Defs/EnemyDef.cs`, `ItemDef.cs`
- `WorldGen/Fields/TerrainField.cs`, `Passes/BiomeRelief.cs`, `Structures/DungeonBuilder.cs`
- `Gameplay/Town/*`

Game, `game/`:
- `Player/InteractionController.cs`, `PlayerController*.cs`, `PlayerAvatar*.cs`, `ViewModel.cs`
- `UI/Hud.cs`, `InventoryScreen*.cs`, `TownDialog*.cs`, `UiTheme.cs`
- `Ambience/EnvironmentController*.cs`, `Rendering/DynamicLights.cs`
- `World/ColliderManager.cs`, `TileObjectRenderer.cs`, `GrassRenderer.cs`
- `Bootstrap/WorldLauncher.cs`

Other:
- `shaders/include/voxel_light.gdshaderinc`
- `tools/blender/build_all.py`, `bgen/human/actions.py`
- `tests/Strata.Core.Tests/WorldGen/WorldGenTests.cs`

Reuse:
- Boss `Parts` (`NpcSystem.Parts.cs`), `finaleEnemy`, homing hostile projectiles (`ProjectileSystem.Special.cs`), `CameraShake`, `CombatEffects`.
- `RecipeBook.RecipesFor/RecipesUsing`, the ContentValidator item-source enumeration (moved into Core `ItemSources`), `ItemIcons`, `CreatureVisual.Create`, `ReferenceCatalog`.
- `GraphicsConfig`/`GrassConfig` config patterns.

## Verification
* **Per WP**, in its worktree:
  - `python3 tools/ci/check_file_length.py`, `dotnet build Strata.sln`, `dotnet test tests/Strata.Core.Tests`
  - `Strata.Tools manifest --check`, `validate`, `coverage`
  - `gen_ore_tiers.py --check`, `gen_reference.py --check`, `build_all.py --check`
  - Godot headless import, plus the WP's smoke scripts (they must print `SMOKE PASS`)
  - The new Core tests listed in the WP spec
* **Per wave gate**, on the merged branch:
  - Every `game/Testing/Scripts/*.cmds` with the CI flags, and `--menu-tour` (`MENU PASS`).
  - Lavapipe tours (xvfb) for visual work: lighting (numeric luminance and clipping thresholds on medium, high and ultra), animation, items, UI (1280×720 and 1920×1080 at UI scales 1.0 and 1.5; the `layout.overlaps` probe must be 0), bosses, and the dungeon.
  - Screenshots are sent to the player at each push.
* **Each complaint maps to a check**:

| Complaint | Check |
|---|---|
| Flashbang | `shot_stats.py` desert and snow noon luminance in [0.35, 0.6], under 2% clipped pixels; exposure probe |
| Torches | A lighting-tour torch room lit to about 8–10 m (luminance at 6 m above a threshold) |
| Facing | `player.facing_error_deg < 3` after `camera orbit 90`; a target behind the player is never selected |
| Names | `hud.target_name` for a slime, a worm segment and a resident |
| Hooks | `movement.cmds` latches on a natural slope and stays latched while reeling in |
| Swings | UseTimingTests: one clip per swing and no restart; mining speed leaves `useAnimation` unchanged |
| Bow | The first-person draw pose probe; a no-ammo shot gives feedback (`msg.last`, `sfx.last`) |
| Terrain | `worldgen-report --compare` against the W0 baseline shows lower roughness and overhang metrics; SurfaceProfile, Relief and HeightfieldSmoothing tests |
| Lag | `perf.cmds` counters: zero main-thread saves, at most 2 colliders rebuilt per edit, 1 object rebuild per remesh, grass within budget, 0 gen2 collections during travel, `assets.pending` reaches 0; before/after `perf spikes` p99 and max |
| Skeletron and Dungeon | SkeletronTests, DungeonGenerationTests (every room reachable), LockedChestTests, and a `dungeon.cmds` route from the entrance to the vault |
| NPCs | `town.cmds`: day-only move-in, rescue, housing query/assign/evict, residents defending, Nurse heal |
| Guide book | `ui.cmds` book tabs; the bestiary unlocks after a kill; the index builds off the main thread |

* **Final**: the full CI list and every tour, `docs/AUDIT.md` and the master plan updated to the final status, and a push. A Windows determinism run through the CI `workflow_dispatch` is suggested to the player; I won't open a PR unless they ask for one.

## Risks
* Scale is about 160 WPs at 2 concurrent agents, so this is many hours of work. Pushing at each wave keeps progress safe, and workflows resume after container restarts.
* Lavapipe cannot run ultra at real speed, so visual thresholds are relative and GPU frame times on the player's machine are unmeasured. Performance counters are deterministic stand-ins.
* The 22 MB animation GLB rebuilds bloat the history; only two rebuilds are allowed.
* A merge conflict in a hub file is caught by the gate integrator; a WP that fails review is re-run rather than pushed.

## W4 amendments (2026-10-02, after the Bosses + Dungeon slice merged into the integration branch at be46b873)
* **P2-15, P2-16, P2-13, P2-14, P2-21, P2-22, P2-23, P2-25, P2-29, P2-26, P2-27, P2-28, P2-18, P2-17, P2-30, P2-31** (every W4 WP that has not started yet): BEFORE coding, check `git merge-base --is-ancestor be46b873 HEAD` in your lane dir; if it fails, run `git merge --no-edit claude/clever-goodall-kgex45` (it brings in the Bosses slice: boss framework, Skeletron, the Dungeon, NpcSystem.Messaged, BossInfo/GroupLife). Resolve conflicts keeping both sides (UI files: keep the W4 lane's version and re-apply the Bosses side's small additions), build + Core tests, commit "Wave W4: merge integration (Bosses slice)" with the attribution lines.
* **P2-21** chat log: consume `NpcSystem.Messaged` with `MessageKind` + `MessageColors` (boss awoken/defeated purple, invasion, resident arrival/death, etc.) and REPLACE the Hud's fixed-colour `npcs.Announced` path (`Hud.cs` ~157) so every announcement shows once, coloured, in the chat log.
* **P2-23** boss bar: sum life with `NpcSystem.GroupLife(BossInfo)` and include parts flagged `InBossBar` (Skeletron's hands, Prime's arms, Golem parts, worm segments) — one bar per boss group with the P2-24 head icon; a defeated part drops out. Keep small floating bars over non-boss parts (Terraria behaviour).
* **P2-22 / P2-10 tooltips**: Expert items use rarity -12 (rainbow); pickup popups and tooltips must animate it each frame (not freeze one colour); Master rarity -13 (fiery red/orange) likewise if present.
* **P2-16** accessory slots, Terraria-accurate: Normal/Journey 5; Expert 5 (+1 when the Demon Heart was consumed: `PlayerState.ExtraAccessorySlot`); Master 6 (+1 with the heart). Change `GameWorld.AccessorySlotsFor(Difficulty)` to also take the heart flag, recompute `Inventory.AccessorySlotsUnlocked` right after the heart is consumed (`ItemUseSystem.Consume.cs`), keep items in a slot that becomes locked (they stay, greyed, and can be taken out — never deleted), Core test for all 3 difficulties × heart.
