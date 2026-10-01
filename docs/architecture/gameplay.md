# Gameplay systems

* **Items** — `ItemUseTable` infers actions from stats (pick/axe/hammer power, place targets, projectiles,
  healing) plus explicit `behaviors`. `ItemUseSystem` enforces use time, auto-reuse and reach, and performs
  mining (brush sized by `mineBrush`, precision mode = 1 cell), chopping, hammer reshaping, block/object
  placement (shape + facing rotation), consumption (healing fatigue, permanent life/mana caps) and shooting
  (ammo classes, mana). A sword that also shoots (Starfury, Terra Blade) swings with every shot and still swings
  when the shot cannot fire; `noMelee` items (Zenith) only animate their swing. `shootProjectile.maxActive` caps
  how many of a weapon's projectiles one player has out (unset: one for returning projectiles, otherwise no cap;
  0: no cap); a capped use is refused before any ammo or mana is spent.
* **Use timing** — as in Terraria, an item has a use *animation* (`useAnimation`; `PlayerEntity.UseAnimation`
  counting down from `UseAnimationMax`) and a use *time* (`useTime`; `UseCooldown`, the action cadence). A new
  animation starts only when the last one has played out (or for another item); it bumps `UseSerial`, which
  presentation keys off to play each clip exactly once, so swings never restart mid-arc. Inside one animation a tool
  digs, and a gun whose use time is shorter fires, whenever the use time runs out; a tool held down chains its next
  swing as soon as the last ends, even while its next hit is still cooling down. Melee weapons strike once per
  animation (`Swung`). Speed prefixes (`Prefixes.UseSpeed`) and melee speed divide both time and animation; mining
  speed divides only a tool's use time (more hits per swing). `PlayerEntity.ResetUse()` ends the current use (the
  `use` command and tests). Clip windows live in `data/config/use_styles.json` (`clips`: length, strike start/end,
  release, bow re-draw; loaded by `Presentation.UseStyleConfig`, read through `Items.UseTiming`): a swing deals no
  damage during its wind-up and sweeps its arc over the strike window (`UseTiming.Sweep`), a thrust connects only
  while `UseTiming.Striking`.
* **Failed-use feedback** — a fresh press that cannot be used raises `ItemUseSystem.Failed` (counted in `Failures`,
  probe `use.failures`) and a message through `Refused`, which the HUD toasts: no ammo names the cheapest obtainable
  item of the weapon's ammo class ("Needs Wooden Arrow", cached per class), no mana says "Not enough mana", and a
  `Blocked` use speaks only when it recorded a reason with `Refuse` (Potion Sickness, a curse's `noItemUse`, a summon's
  `failMessage`). Placement, a boomerang already out and a crystal at its cap stay silent. A held button never
  reports, and the same text is not repeated to a player within 2 s. `SoundEvents` plays `use_fail` or `no_mana` for
  each failed press, and `tink` (recipes in `tools/audio/sfx_recipes_feedback.py`) when a tool is too weak for what it hits.
* **Shot origin** — `ShotGeometry` places a player's shot at the weapon hand (`HandOffset`: 0.22 m right, 0.3 m down,
  0.45 m ahead of the eye in the `Facing` frame) and aims it from there at the crosshair point (`Eye + Aim ·
  AimDistance`, never nearer than `MinConvergence` = 2 m), so arrows, bolts and thrown weapons leave the hand and still
  land on the crosshair. When a solid cell or blocking object lies between the eye and the hand (a wall at the
  shoulder) the shot starts at the eye instead, never inside terrain. `ProjectileSystem.Fire` applies the weapon's
  spread around that line; other spawns (creature volleys) keep their own origins.
* **Grappling hooks** — `GrappleHook` (`PlayerEntity.Hook`) is the hook's state machine (`None`, `Flying`, `Attached`,
  `Retracting`; probe `player.hook` 0–3). A click with a hook held (`ItemUseSystem` grapple branch,
  `UseOutcome.Grappled`) or the grapple key (`GrappleHook.Best`: longest reach, accessories then inventory) throws it
  from `PlayerEntity.Hand`; pressing again lets go. The throw walks the voxel ray from the eye along `Aim`
  (`Targeting.CastWhere`) to the first `Latchable` cell within reach: solid blocks (platforms included), shaped blocks
  with a walkable top and colliding tile objects, a tree's whole box (canopy) too, but never liquids, cobwebs, decor,
  open doors or an object the eye stands inside. It latches to that exact cell and lets go only when the cell stops
  being latchable, the player jumps or presses again, so the offset between smoothed terrain and its cells cannot drop
  it. Core decides anchor, state and `PullVelocity` (towards the anchor at the hook's pull speed, hanging within
  1.1 m); the engine body (`PlayerEntity.HasBody`) advances the hook in `_PhysicsProcess` and applies the pull, while
  `GameWorld` ticks it for bodiless (headless, test) players. Death, teleports and recalls put the hook away; nothing
  is thrown from a minecart, and riding one or phasing lets go of a hook that is out.
* **World editing** — `WorldEditor`: accumulated block damage vs hardness/pick power, drops, loose blocks
  falling, objects losing support, object hit points and loot, container contents, remeshing via the
  streamer. `Targeting` is a voxel DDA that also hits tile objects. A `TargetFilter` decides whether decor
  (grass, bushes, rubble) and tree canopies stop the ray: `ItemUseSystem.FilterFor` gives tools `Decor` and axes
  `TreeCanopy` too; otherwise a tree only counts where the ray crosses `TileObjectInstance.TrunkBox` (an exact
  ray-box test), the same column the body collider uses. `Targeting.CastView` starts the crosshair ray where it
  crosses the eye's plane (nothing between the shoulder camera and the player can be selected) and measures
  `Distance` from the eye; `CastWhere` exposes the raw walk with a custom stop test. `ItemUseSystem.ReachMeters`
  (item reach plus the Reach stat) bounds both the use and the in-reach highlight. `PlayerEntity.ViewDirection`
  (camera forward) feeds `Facing`, which drives `FacingQuarter` and the melee arc yaw; `Aim` (eye to crosshair
  point) keeps the pitch, shots and drops.
* **View ray and facing (engine)** — each physics tick `InteractionController.UpdateTarget` casts the view ray (from
  `CameraRig.ViewOrigin`, the camera without its screenshot orbit, along `CameraRig.Forward`) twice with
  `Targeting.CastView`: reach + 2 m with the held item's `FilterFor` filter gives `Target` (`InReach` when its eye
  distance is within reach), and 60 m with `TargetFilter.None` gives the crosshair point (terrain, trunks and
  furniture, or 60 m out) that sets `Aim` and `AimDistance`, so shots converge on what the crosshair rests on at any
  range. Smart digging may still swap in the cell under the feet. In third person the body always faces the camera's yaw: the chest, arms and head stay on the
  crosshair while the legs strafe or backpedal (`BodyFacing`, see rendering.md, Creature animation), and the swing
  trail, like the melee arc, is centred on `Facing`. Probes: `target.front` (1 when the target is not behind the
  eye along `Facing`), `target.dist`, `player.aim_distance`.
* **Entity picking (crosshair names)** — `EntityPicking.Pick` (Core) finds the creature or town resident the view
  ray enters first between the eye's plane and the terrain the crosshair rests on (so walls hide names), up to 60 m:
  a slab test against each live creature's padded volumes (`Npc.VolumeCount`/`VolumeAt`, the allocation-free form of
  `Volumes()`: the body, then each worm segment, which resolves to its worm) and each live resident's body. It
  allocates nothing per tick. `InteractionController.Hovered` holds the pick; the crosshair point moves onto it (at
  least a metre past the eye's plane), and a hovered resident within `TownConfig.TalkRange` is the `TalkTarget`
  right-click talks to. `EntityPicking.Describe` gives the label: a boss's title, a creature's name or a resident's
  title ("Andrew the Guide") with its life; the HUD's `TargetLabel` shows it at any distance ("Blue Slime 25/25",
  tinted by life, bosses in the boss bar's red), else the block or object in reach, rebuilding its text only when
  that changes. Probes: `hover.npc`, `hover.resident`, `hover.talk`, `hover.<enemy id>`, `hud.target_name`,
  `hud.target_name.<words>` (the label in snake case contains them), `ui.talk_open`.
* **Crafting** — `RecipeBook` (indexed, atomic crafts) + `CraftingEnvironment` (stations within reach,
  nearby liquids, biome/time conditions).
* **Combat** — `DamageModel` (class bonuses, ±15 % variance, defence × difficulty factor, crits, knockback
  resistance). `MeleeSystem` sweeps swing arcs/thrusts over the speed-scaled use animation, inside the clip's
  strike window (one hit per creature per swing); `ProjectileSystem` handles gravity, drag, terrain bounces, pierce and on-hit effects.
* **Creatures** — `NpcSystem` runs brains (`SlimeHop`, `Walker`, `Flyer`, `Swimmer`, `Lunger`, `Idle`, boss
  brains, created from the enemy's `brain` spec), data-driven `ranged` volleys of hostile projectiles (line of
  sight required), `onContact` debuffs and `onDeath` effects (pour liquid, split into creatures), AABB voxel movement, contact damage with player i-frames and knockback, buffs with damage
  over time (debuff stats count: ichor lowers a creature's defense), death loot and coins, despawning far from players. `SpawnDirector` evaluates data-driven spawn
  rules at candidate ground cells (or inside water for `placement: liquid`) with layer/biome/time/light context,
  slowed in towns (tuning: `data/config/spawning.json`). Rules may cap how many are alive (`maxAlive`) or judge
  their conditions where the player stands (`atPlayer`); creatures may leave when `despawnWhen` holds at their target.
* **Zones** — planned features with a `zone` (the Dungeon, spider nests, marble/granite halls, evil hearts) are
  reported as extra biomes by `WorldSession.ContextAt`/`ZonesAt`; a structure's zone is its blueprint volumes. A
  zone biome with `lockedUntil` protects its generated structure cells from mining until that flag is set
  (`WorldEditor.IsLocked`); one with `exclusiveSpawns` only spawns rules that name it, at closer range.
* **Events and bosses** — `EventDirector` rolls data-driven events at dusk/dawn (`data/events.json`) and
  feeds them to spawn-rule conditions; bosses are enemies with a `boss` block (arena radius, day despawn,
  segments for worms, per-part `segmentModels`, `dayEnrage`) driven by `GelSovereign`, `Eye of Cthulhu`, `Worm` and
  `CryptWarden` and `Wall of Flesh` brains (brains that `PositionsSegments` place free parts such as hands or eyes
  themselves; `ranged.fromParts` fires from them in turn, or only from `ranged.parts`). Brains mark a second phase
  with `Npc.Enraged`, which swaps in `ranged.enraged` (the Twins' rapid lasers and flamethrower). A boss may
  bring `companions` that share its flag: the group spawns together, is announced once, shares one boss bar and
  counts as defeated only when its last member falls. Worms can release capped `minion` drones from their segments;
  the warden brain holds any number of hands in pairs (Skeletron Prime's saw, vice, cannon and laser), with only the
  first `swipingHands` swiping. A brain may give its creature a `Barrier` (the Wall of Flesh's
  flesh: tiled `barrierModel`, hurts and throws out players behind it) and tether minions to it (`Leader`: their
  anchor follows it and they die with it). `summonBoss` items, the keeper's `summonBoss` service and
  `incinerationSummon` items (burned in lava in a given layer, sacrificing a resident) spawn bosses.
* **Progression** — `ProgressionSystem` is the only way flags are raised (boss defeats, event completions, the `flag`
  command); raising one raises every `autoSet` flag whose requirements are met (`hardmode` after the Wall of Flesh) and
  fires `Raised` (announcements; the hook for world mutations).
* **World mutations** — `data/mutations.json` defines mutations triggered by flags (`oreVeins`: vein rules with
  `generate: false` placed by the shared `VeinStamper`; `wedge`: a slanted slab converted through a conversion table,
  which is also a zone). `MutationDirector` appends them to the world's `MutationLog` (saved as id + seed);
  `WorldSession.Produce` brings generated or saved chunks up to the log's level on worker threads, and loaded chunks
  are caught up a few per tick as edit batches (lighting, meshing and saves follow). `SpreadSystem` randomly updates
  cells of loaded chunks: sources of an active spread rule convert neighbours through its conversion
  (`data/config/spread.json`).
* **Liquids** — `LiquidSimulator` (Simulation/Liquids) is a cellular automaton over *active* cells only:
  cells woken by edits (`VoxelWorld.Edited`), by a chunk loading next to them (`WorldSession.ChunkInstalled`
  scan that wakes only cells that could move), or by their own flow. Each liquid steps every `flowInterval`
  ticks: react with other liquids (`mixes` → obsidian, honey blocks), fall up to `fallCells`, share sideways
  with neighbours at least `spreadThreshold` levels lower (never thinner than `minLevel`), evaporate thin
  remnants. One edit batch per step keeps saving, sky map, lighting (lava's `light`) and remeshing (border
  cells dirty neighbour chunks) consistent. `LiquidEffects` applies contact damage/buffs (lava burns, honey
  sweetens, water clears `fire` buffs), `liquidImmune` effects, drag/buoyancy on drops and item
  incineration. Buckets are the `bucket` behaviour (scoop from a pool's surface, pour with overflow up).
  All liquid properties live in `data/liquids.json`; the voxel byte stores code (1–7) + level (0–31).
* **Navigation** — `VoxelNavigator` is A* over standing nodes (grid vertex + feet height; the body is the 2×2 cell
  columns around the vertex, 4 cells tall): 8-way walking without corner cutting, ledge steps/jumps, drops,
  doors passable for walkers that open them, harmful liquids avoided, unloaded cells treated as solid.
* **Housing** — `HousingScanner` flood-fills the air reachable from a cell (doors count as wall) up to
  `maxVolume`, then checks floor area, headroom, the share of player-built walls and furniture roles from
  `data/config/housing.json` (door in the walls, light, table, chair) and picks a home node inside.
* **Town** — `TownSystem` places starter residents, moves residents in (data-driven `arrival` conditions such as
  `coins`, `maxLife`, `resident`, `flag`) when a vacant valid home exists (rooms found from chairs), re-validates
  homes, and drives residents: stroll by day, walk home at night along A* paths, opening and closing doors, stop
  and face the player while talking. `TownCommerce` picks dialogue lines by condition (placeholders for names),
  prices shop stock by happiness (liked/disliked biomes and neighbours, crowding, homelessness), buys and sells
  (at `sellRatio`), and runs services (`heal` for coins with debuff removal, `craftHelp`, `summonBoss`). Landmark
  residents (the Dungeon keeper) are placed at a planned landmark once it streams in and stay near it; any resident
  whose `leavesWhen` holds leaves for good (`Departed`). Residents persist in the world metadata. Residents live in
  `data/town.json`, tuning in `data/config/town.json`.
* **Players** — `PlayerState` (inventory, equipment effects, buffs, vitals, spawn points) is saved per
  character; `PlayerEntity` is the in-world body the engine syncs; death → 5 s → respawn at bed or spawn.

## Commands, probes and smoke scripts
`CommandRegistry` (Core) + `EngineCommands` (Godot). Probes are named numbers (`player.life`, `inv.<item>`,
`npcs.<enemy>`, `block.dx.dy.dz`, `liquid.dx.dy.dz`, `near.<block>`, `liquid.active`, `town.<id>`, `town.<id>.home`, `town.<id>.alive`, `zone.<biome>`, `flag.<id>`, `house.valid`, `stream.idle`, `errors`, …); debug commands such as
`god`, `drop`, `slay` and `tp landmark` make fights and summons scriptable used by `assert` and `wait_until`.
`game/Testing/Scripts/*.cmds` run headless in CI with `--smoke=` and print `SMOKE PASS`/`SMOKE FAIL`;
`--exec="cmd;cmd"` runs commands at startup (screenshots, manual testing). Runner directives: `wait`,
`wait_until <probe> <op> <value> [timeout]`, `hold <action> <seconds>`, `repeat <n> <command>`.
Probes come from `StandardProbes` and from `[ProbeSource]` classes (static `Register(CommandContext ctx, …)`):
`CommandContext` registers those in Strata.Core (e.g. `TownProbes`), `GodotCommandHost.CreateContext` those in the game
assembly (e.g. `PlayerProbes`, which also takes the `LaunchedWorld`), in full-type-name order (`ProbeSources`). A
probe name is registered once; new probes go in a new source file (docs/CONVENTIONS.md, *Extension points*).
