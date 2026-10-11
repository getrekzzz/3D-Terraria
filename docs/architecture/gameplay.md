# Gameplay systems

* **Items** — `ItemUseTable` infers actions from stats (pick/axe/hammer power, place targets) and the use style
  (below) plus explicit `behaviors`. `ItemUseSystem` enforces use time, auto-reuse and reach, and performs
  mining (brush sized by `mineBrush`, precision mode = 1 cell), chopping, hammer reshaping, block/object
  placement (shape + facing rotation), consumption (healing fatigue, permanent life/mana caps) and shooting
  (ammo classes, mana). A sword that also shoots (Starfury, Terra Blade) swings with every shot and still swings
  when the shot cannot fire; `noMelee` items (Zenith) only animate their swing. `shootProjectile.maxActive` caps
  how many of a weapon's projectiles one player has out (unset: one for returning projectiles, otherwise no cap;
  0: no cap); a capped use is refused before any ammo or mana is spent.
* **Use styles** — `ItemDef.UseStyle` is Terraria's vocabulary: `swing` (swords, tools, summon staves, fishing rods,
  nets, buckets, wiring and paint tools), `rapier` (shortswords), `spear`, `shoot`, `gun` (magic guns too), `bow`,
  `staff`, `tome`, `throw` (boomerangs, shuriken, knives, bombs), `yoyo`, `flail`, `whip`, `holdUp` (boss and event
  summons, crystals, mirrors, crates), `drink`, `eat` and `place`. `UseStyleRules` resolves an item's style
  (`ItemUseInfo.Style`; data without one is inferred: tools swing, placers place, Well Fed food is eaten, other
  healing and buffs are drunk, summons are held up, hooks shoot, arrow weapons are bows, other ammo weapons guns, magic
  weapons with a projectile staffs) and the actions it implies: a swing hits only with a melee weapon, a rapier or
  spear thrusts only then (`noMelee` spears hit with their projectile), every releasing style shoots when the item has
  a projectile or ammo, a whip lashes as melee, and drinking, eating or holding up uses an item that heals or buffs.
  How each style looks is data (`data/config/use_styles.json` `styles`: clip, tool clip, arm aim, optional grip,
  hold-out and swing blade curve, every style required). The validator requires a projectile or ammo on releasing
  styles and damage on yoyos, flails and whips, warns when a weapon, tool or consumable has no style, and holds every
  stackable item but the coins to 9999 (`ItemDef.CommonMaxStack`, Terraria 1.4.4).
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
  while `UseTiming.Striking` in its own clip's window (`UseTiming.ThrustClip`: a rapier's quick jab, a spear's
  later lunge).
* **Failed-use feedback** — a fresh press that cannot be used raises `ItemUseSystem.Failed` (counted in `Failures`,
  probe `use.failures`) and a message through `Refused`, which the HUD toasts: no ammo names the cheapest obtainable
  item of the weapon's ammo class ("Needs Wooden Arrow", cached per class), no mana says "Not enough mana", and a
  `Blocked` use speaks only when it recorded a reason with `Refuse` (Potion Sickness, a curse's `noItemUse`, a summon's
  `failMessage`). Placement, a boomerang already out and a crystal at its cap stay silent. A held button never
  reports, and the same text is not repeated to a player within 2 s. `SoundEvents` plays `use_fail` or `no_mana` for
  each failed press, and `tink` (recipes in `tools/audio/sfx_recipes_feedback.py`) when a tool is too weak for what it hits.
* **Quick heal, mana and buff** — `ItemUseSystem.Quick` (keys `quick_heal` H, `quick_mana` J, `quick_buff` B, polled
  by `InteractionController.Quick`) uses items from anywhere in the backpack through the slot-aware `ConsumeAt`, never
  changing the selected slot. Quick heal/mana take Terraria's pick (`BestFit`): the potion with the smallest
  non-negative overheal, else the strongest; Potion Sickness refuses quick heal with its name, and a mana potion that
  also heals waits it out. Quick buff eats the best food only while no food buff is on (`BuffDef.FoodTier`: Well Fed 1,
  Plenty Satisfied 2, Exquisitely Stuffed 3; eating replaces any other tier) and drinks one of every buff item whose
  buff is not active or immune. The first item used per press starts its own use animation (`PlayerEntity.UseItem`,
  so `PlayerAvatar` plays the drink while the held item stays in hand) and plays its sound through `Used`; every item
  raises `QuickUsed`, which `PickupFeed.AddUsed` floats as "Used Healing Potion". `ConsumeParams.RequiresLife` and
  `RequiresFlag`/`FlagMessage` gate Life Fruit (400 max life from crystals, a mechanical boss down). The `hurt <amount>`
  command takes life directly for scripts.
* **Shot origin** — `ShotGeometry` places a player's shot at the weapon hand (`HandOffset`: 0.22 m right, 0.3 m down,
  0.45 m ahead of the eye in the `Facing` frame) and aims it from there at the crosshair point (`Eye + Aim ·
  AimDistance`, never nearer than `MinConvergence` = 2 m), so arrows, bolts and thrown weapons leave the hand and still
  land on the crosshair. When a solid cell or blocking object lies between the eye and the hand (a wall at the
  shoulder) the shot starts at the eye instead, never inside terrain. `ProjectileSystem.Fire` applies the weapon's
  spread around that line; other spawns (creature volleys) keep their own origins.
* **Grappling hooks** — `GrappleHook` (`PlayerEntity.Hook`) is the hook's state machine (`None`, `Flying`, `Attached`,
  `Retracting`; probe `player.hook` 0–3). A click with a hook held (`ItemUseSystem` grapple branch,
  `UseOutcome.Grappled`) or the grapple key (`GrappleHook.Best`: the hook slot, else the longest reach carried) throws it
  from `PlayerEntity.Hand`; pressing again lets go. The throw walks the voxel ray from the eye along `Aim`
  (`Targeting.CastWhere`) to the first `Latchable` cell within reach: solid blocks (platforms included), shaped blocks
  with a walkable top and colliding tile objects, a tree's whole box (canopy) too, but never liquids, cobwebs, decor,
  open doors or an object the eye stands inside. It latches to that exact cell and lets go only when the cell stops
  being latchable, the player jumps or presses again, so the offset between smoothed terrain and its cells cannot drop
  it. Core decides anchor, state and `PullVelocity` (towards the anchor at the hook's pull speed, hanging within
  1.1 m); the engine body (`PlayerEntity.HasBody`) advances the hook in `_PhysicsProcess` and applies the pull, while
  `GameWorld` ticks it for bodiless (headless, test) players. Death, teleports and recalls put the hook away; nothing
  is thrown from a minecart, and riding one or phasing lets go of a hook that is out. `GrappleVisual` draws the chain
  from the drawn (interpolated) hand and moves the claw from the voxel hit onto the rendered surface: one physics ray
  across the struck face (terrain, platform and object colliders, ±0.6 m) when a throw starts and when it latches, kept
  only within 0.5 m and eased on over the head's last metre of flight (probe `player.hook_snap`, -1 when nothing was
  found, e.g. a canopy). The third-person body flings a clicked hook out along the aim with the `gun` clip (the `shoot` style). `movement.cmds` covers the
  key, the click, a crisp slab, a natural (smooth-meshed, `fill … natural`) sloped ceiling that must stay latched while
  reeling in, a broken anchor and platforms.
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
* **Smart Cursor** — X (`smart_cursor`, saved as `UserSettings.SmartCursor`, also in Settings → Gameplay) toggles it.
  `SmartTargeting.Find` (Core) samples a cone round `Aim` from the eye (12° half-angle, at most 0.75 m wide, every
  0.25 m up to the reach and at most 0.75 m past the first solid cell on the aim line): a pickaxe takes the block it
  can mine (`MiningRules.CanMine`) with a face open toward the eye that is closest along the aim, `ore`/`gem`-tagged
  blocks first; an axe the nearest `BreakTool.Axe` object (trees), a hammer a `BreakTool.Hammer` one. It allocates
  nothing. `InteractionController.ApplySmartCursor` swaps it in as `Target` (always in reach) unless the crosshair
  already rests on a non-decor object in reach, and reruns it only when the item, eye (5 cm) or aim (0.5°) moved, the
  world was edited, the pick went stale or 15 ticks passed; smart digging still wins when looking down. The crosshair
  turns orange with the smart cursor icon, the pointer uses its smart sprite, and the status line says "Smart Cursor
  On/Off". Scripted runs keep the toggle in memory (`GameRoot.Automated`). Probes: `ui.smart_cursor`,
  `ui.smart_target`.
* **Crafting** — `RecipeBook` (indexed, atomic crafts; `CraftInto` crafts onto the cursor, merging into the same
  stackable item and refusing anything else) + `CraftingEnvironment` (stations within reach and the objects providing
  them, nearby liquids, biome/time conditions; `Browse` tallies the slots once; `Signature` is a stable FNV hash of a
  browse so the UI rebuilds only on change) + `RecipeText` (group and condition wording).
* **Combat** — `DamageModel` (class bonuses, ±15 % variance, defence × difficulty factor, crits, knockback
  resistance). `MeleeSystem` sweeps swing arcs/thrusts over the speed-scaled use animation, inside the clip's
  strike window (one hit per creature per swing); `ProjectileSystem` handles gravity, drag, terrain bounces, pierce and on-hit effects.
  On-hit effects include `explode`, `applyBuff`, `lifeSteal {fraction}` (heals the owner by a share of the damage
  dealt) and `spawnProjectiles {projectile, count, speed, homing, damageScale}`; a `latch` projectile sticks to the
  creature it bites and strikes again every `latchInterval` ticks until its lifetime ends (the Piranha Gun).
  Minions with speed 0 are sentries, which have their own single slot apart from the minion slots.
* **Creatures** — `NpcSystem` runs brains (`SlimeHop`, `Walker`, `Flyer`, `Swimmer`, `Lunger`, `Idle`, boss
  brains, created from the enemy's `brain` spec), data-driven `ranged` volleys of hostile projectiles (line of
  sight required), `onContact` debuffs and `onDeath` effects (pour liquid, split into creatures), AABB voxel movement, contact damage with player i-frames and knockback, buffs with damage
  over time (debuff stats count: ichor lowers a creature's defense), death loot and coins, despawning far from players. `SpawnDirector` evaluates data-driven spawn
  rules at candidate ground cells (or inside water for `placement: liquid`) with layer/biome/time/light context,
  slowed in towns, within per-player allowances of hostiles and critters that count every creature until it
  despawns and that a group never overshoots (tuning: `data/config/spawning.json`). Rules may cap how many are alive (`maxAlive`) or judge
  their conditions where the player stands (`atPlayer`); creatures may leave when `despawnWhen` holds at their target.
* **Zones** — planned features with a `zone` (the Dungeon, spider nests, marble/granite halls, evil hearts) are
  reported as extra biomes by `WorldSession.ContextAt`/`ZonesAt`; a structure's zone is its blueprint volumes. A
  zone biome with `lockedUntil` protects its generated structure cells from mining until that flag is set
  (`WorldEditor.IsLocked`); one with `exclusiveSpawns` only spawns rules that name it, at closer range.
* **Events and bosses** — `EventDirector` rolls data-driven events at dusk/dawn (`data/events.json`) and
  feeds them to spawn-rule conditions; bosses are enemies with a `boss` block (arena radius, day despawn,
  segments for worms, per-part `segmentModels`, `parts`, `enrage`) driven by `GelSovereign`, `Eye of Cthulhu`, `Worm` and
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
* **Natural boss arrivals** — an event with `spawnBoss` is a warning plus a countdown (`duration`): "You feel an evil
  presence watching you..." (1/3 of nights for a player with 200 life and 10 defense until the Eye is beaten) calls the
  Eye; in hardmode after the first altar the `mech_night` `group` (one member picked per roll among the eligible, 1/10)
  warns of an unbeaten mechanical boss. When the countdown ends the boss comes unless one lives or it was beaten (fliers
  20 m ahead and 12 m up, walkers drop in from 12 m). `killGoalAfterFlag` lowers a goal once a flag is set
  (`EventDirector.GoalOf`): Slime Rain calls King Slime after 150 gel kills, 75 once he is beaten. The `worldRing`
  condition reads `ConditionContext.CentreDistance` (0 at the centre, 1 at the land radius): King Slime may wander
  in on the outer third of the surface (`king_slime_natural`, about 1 in 300 surface spawns).
* **Boss framework** — `boss.parts` (`BossPart`: offset, `diesWithBody` default true, `inBossBar`, `mirror`) spawn
  fixed parts (`Npc.IsPart`) that skip far-despawn and the respawn clear; parts that die with the body never keep
  `GroupAlive` true, so killing the body raises the defeat flag once; its parts die with it at once (no hits, no second
  defeat when `slay` reaches them) and are removed next tick without loot. A reeling creature keeps its last scales.
  `boss.enrage` (`when` conditions at the target's feet every 30 ticks, `damage`/`speed` multipliers, `setDamage`,
  `setDefense`, `latch`, `message`) sets `Npc.Frenzied`; `SpeedScale` scales the movement step, `DefenseOverride` and
  `DefenseDelta` (zeroed before each think) feed `Npc.Defense`. `lootOnDefeat` rolls loot only for the last of a group.
  `BrainContext.Target` is an `ICombatTarget` (`TargetPlayer` for player-only effects); `Expert` is true only for
  Expert and Master, as is the `expert` condition (Expert-only loot); `defense` reads `ConditionContext.PlayerDefense`.
  Brains may raise `Impact`/`Cue` (`NpcSystem.Impacted`/`Cued`) and `Pulse` buffs to nearby players. Every creature
  announcement also goes out on `NpcSystem.Messaged` with a `MessageKind` (`MessageColors`: boss purple), including
  "<Title> has awoken!" / "has been defeated!". Probes: `boss.<enemyId>.<field>` (life, max, defense, stage, phase,
  enraged, frenzied, illusions, parts, segments).
* **Boss presentation** — `game/Entities/BossEffects` (beside `CombatEffects`): a boss arriving kicks the camera
  (`CameraShake.Kick` 0.45), its defeat kicks 0.7 and `MusicDirector.Silence(3 s)` fades the soundtrack out over a
  second and holds it while `MusicDirector.Sting("victory")` plays the victory sting on the music bus (the
  `boss_defeated` sound is a boom under it, no longer a tune) before the fitting track fades back in.
  `NpcSystem.Impacted` shakes by distance to the camera (none past 40 m), plays `boss_slam` and throws a dust ring in
  the colour of the block under the point (8 pooled emitters); `NpcSystem.Cued` ids are sound ids played at the
  creature. Boss sounds live in `tools/audio/sfx_recipes_bosses.py`. Probes: `camera.trauma`, `music.silent`. The boss
  bar and messages are the HUD's (`NpcSystem.Messaged`).
* **Boss intros (X-02)** — a boss block's optional `intro` (`subtitle`, `color`, `framing` m, `letterbox` s; validated,
  one per defeat-flag group, on the 18 progression bosses) drives `game/UI/BossIntro` on the Core
  `BossIntroModel` timeline (real seconds): on `BossSpawned` a title card (head icon, name settling from 1.12×, a rule
  and diamond in the boss colour, the subtitle) fades in for 0.35 s, holds 2.4 s and fades 0.8 s; thin cinema bars (6.5%
  of the screen height, under the HUD on layer 9) ease in for the letterbox seconds; `CameraRig.Framing` pulls the
  third-person camera back (and widens the view 1.6° per metre) and is fully back within 1.5 s — the view is never
  turned. On `BossDefeated` the name over a gold "Defeated" and a 0.4 s `Engine.TimeScale` dip (0.35, easing back; never
  over a scale the `timescale` command set). The existing spawn/defeat punches and kicks are not doubled. A new press
  of `use_primary` or the player being hurt skips the card (after 0.5 s shown; everything fades in 0.3 s). The card
  waits while the world is paused or photo mode is on (which hides the layer); the HUD's optional awoken banner is left
  out while a card shows; the "Boss intros" setting turns it off. Every major boss has its own theme
  (`tools/audio/music_tracks.py`; composer variations `octave`, `stabs`, `drums="blast"`) and `MusicDirector.Aliases`
  chains a state without a track to its family track (`boss_worm` → `boss_evil` → `boss_1`). Probes:
  `boss.intro_active`, `boss.intro_defeated`, `boss.intro_cards`, `boss.intro_skips`, `boss.intro_bars`,
  `camera.framing`, `engine.time_scale`, `music.track.<id>`, `music.stings`. `MusicDirector.Stung` reports stings (captions).
* **Boss loot** — boss tables live in `data/loot/bosses.json` under the boss's id (`loot` on the enemy). The main
  table always rolls coins, the potions and the 1/10 `<boss>_trophy` (`data/tileobjects/trophies.json`, wall
  hangings drawn flat on the wall face); Classic and Journey roll `<boss>_drops` loose (`expert` `is: false`), Expert
  and Master drop one `<boss>_treasure_bag` instead (`data/items/boss_bags.json`, rarity -12, opened by using it: a
  `consume` behaviour rolling `<boss>_bag` = the drops plus the Expert-only item). A new boss's bag is data only (bag
  item, three tables, trophy, `tools/blender/assets.d/p4-04-boss-loot.json` rows: `trophy` builds the boss's own
  creature generator into the plaque as a bust, `treasure_bag` takes colours; `bgen/trophies.py`).
* **Hardmode boss loot** — every hardmode boss table moved to `data/loot/bosses.json` in the same main/`_drops`/`_bag`
  layout (Queen Slime, the Twins, the Destroyer, Skeletron Prime, Plantera, Golem, Duke Fishron, Empress of Light, Moon
  Lord; the Lunatic Cultist has a trophy but no bag, as in Terraria). The Twins share `the_twins` with `lootOnDefeat`, so
  the last to fall drops one bag (and rolls both twins' trophies). Expert items: Volatile Gelatin, the three mechanical
  pieces (wheel, battery, wagon; `data/items/expert_items.json`) that make the Minecart Upgrade Kit at a Tinkerer's
  Workshop, Spore Sac, Shiny Stone, Shrimpy Truffle and the Wall of Flesh's Demon Heart. `consume` behaviours
  `extraAccessorySlot` (Expert/Master worlds only, once) and `minecartUpgrade` (once) set the saved
  `PlayerState.ExtraAccessorySlot` / `MinecartUpgrade`; an upgraded player's carts get `MinecartRide.SpeedScale` 1.25.
  The sixth accessory slot itself is not drawn or equipped yet (inventory UI gap).
* **King Slime and the Eye** — King Slime (2000 life) shrinks with its life (`Npc.VisualScale` and the collision box,
  `HalfExtents` from `BaseHalfExtents`; 1 → 0.6 as Terraria's 1.25 → 0.75), sheds a slime per 6 % of life lost (in
  Expert every third is a Spiked Slime), teleports when the target is beyond `teleportDistance` or out of its sight for
  `lostSightTicks` (reappearing beside the target, or right above it when that spot is behind a wall; 35-tick wind-up in
  Expert) and raises an impact and the `land` cue when it lands faster than `impactSpeed` (`Npc.LandingSpeed`, set by
  every move). The Eye (2800 life, 12 defense, 15 damage) tears open at half life: `transformTicks` spinning in place,
  untouchable, into its mouth model, then `phase2Defense` 0 and ×`phase2Damage` (23) without servants; in Expert each
  tenth of life lost below half adds a dash to its bursts (up to `expertMaxDashes`) with shorter wind-ups.
* **Eater of Worlds segments** — `boss.segmentLife`/`segmentDefense`/`segmentDamage` ([head, body, tail]; Terraria's
  65/150/220 life, 0/4/8 defense, 22/13/11 damage over 49 segments) give every segment its own life (`Npc.SegmentLife`,
  parallel to `Segments`; `TotalLife`, `WholeMaxLife`, `NpcSystem.GroupLife` for a bar, probe `boss.<id>.total`).
  Hits carry the struck volume (`Npc.FirstVolume`: melee, projectiles, the `damage <enemy> <amount> [segment]`
  command; contact damage per touched volume). `NpcSystem.Segments`: a dead segment rolls `segmentLoot` where it died
  (`eater_segment` in `data/loot/segments.json`) and, with `splits`, the segments behind it crawl off as a new worm of
  the same group (silently; it keeps the swing's immunity); a lone tail behind the cut or a head left with no body dies
  outright. A dead head hands its place and life to the next segment. The boss table rolls once (`lootOnDefeat`) when
  the last piece dies. Each piece is its own `WormBrain`; in Expert a body segment that sees the target spits Vile Spit
  (`eater_spit`, Weak) every `spitInterval`. `NpcRenderer.SyncSegmentViews` returns lost segment visuals to the pool
  and keeps the tail model on the last one.
* **The Destroyer and the Twins** — the Destroyer keeps Terraria's shared life (80000 over 60 segments; no
  `segmentLife`, so nothing breaks off — adding `segmentLife` with `splits: false` is the data switch for a chain that
  closes over dead segments) but `segmentDefense` [0, 30, 35] / `segmentDamage` [70, 50, 35] still apply per struck
  volume (`NpcSystem.Hit` uses `SegmentDefense` whenever the table exists). A worm with a `minion` is a probe carrier:
  `Npc.SegmentFlags` bit `WormBrain.ProbeSpent` marks a body plate that launched its probe (one each, never the tail;
  a capped spawn keeps it), `RangedAttack` with `fromParts` skips spent plates and the tail (`WormBrain.Fires`), and
  `NpcRenderer` tints spent plates 0.35. Retinazer 20000 / Spazmatism 23000 open at 40% (`transformTicks` 60, phase 2
  defense 20 / 28, ×1.5 contact); in Expert a twin whose `sibling` died moves at `expertLoneSpeed` 1.25
  (`Npc.SpeedScale`). Balance note: these pools are ×2.5–4.4 the old ones, Terraria-faithful by player request.
* **Plantera** — phase 1 summons three tethered `plantera_hook`s (`planteraHook` brain, `PlanteraHookParams` in
  `Content/Specs/PlantBrainSpecs.cs`, `AI/PlantBrains.cs`): invulnerable, no contact damage, dying with her. Each casts
  up to 12 rays along random axis combinations from the target, latches on the face of the first solid cell within
  `reach` (fallback: the floor under the target), and moves on every `relocateInterval` ± a quarter (faster in phase 2,
  or at once when its block is mined); the latch point is `Npc.Direction`, `Phase` 1 = latched. The pattern `leash`
  (radius 9, then 12) holds her near the hooks' centroid. At half life `plantera_open` sprouts eight tethered
  `plantera_tentacle` lungers (twelve in Expert, which also speeds her ×1.2). The boss `enrage` (×2 damage, ×1.6 speed)
  holds while her target is outside `underground_jungle`. `NpcRenderer.Tethers` draws a pooled vine cylinder (one
  material per `tetherColor`) from a living leader's centre to every tethered follower with a tether colour, and from
  the anchor for rooted lungers.
* **Golem** — the `golem` body (`golem_body` model, ground pattern of walks and hops) has boss `parts`: `golem_head`
  (an anchored pattern brain firing fireballs, eye beams below half life; it carries the same boss flag, `inBossBar`)
  and two `golem_fist`s (`skeletronHand` style `punch`: the saw's jab, reach 16 m, held `holdTicks` before it
  retracts; chains via `tetherColor`). The body's first phase is `shieldedBy` the head with `untilGuardsFall`, so it is
  sealed until the head dies. The head's `onDeath` `spawnEnemies` with `tether: true` raises `golem_head_free`
  (invulnerable flyer with a laser `ranged`) bound to the body through `Npc.BoundTo`: it moves on its own, is removed
  without loot once the body dies or goes, and holds no boss flag, so the body's death is the one defeat. Outside
  `jungle_temple` Golem and its parts are enraged (×1.5 damage and speed). The old one-piece `golem` creature model is
  retired; its `golem_idol` generator stays for the trophy.
* **Skeletron** — the `skeletron` (CryptWarden) brain hovers over its target and every `spinInterval` ticks spins
  at it through walls for `spinDuration` (`spinDefense` while spinning, `boss_roar` cue; the drawn whirl is the
  `spin` pose, the simulated facing turns slowly so the hands keep their sides); it moves `speedPerLostHand` faster
  per lost hand. Its hands are boss parts (`skeletron_hand`, not in the boss bar, the left `mirror`ed) with the
  `skeletronHand` brain (`HandParams`): rest at the turning offset, wind up, strike through the captured target
  position (`hand_slap` cue), return; `alternate` hands start on different attacks and the right one half an interval
  late, so they slap and swipe in turn. They hold while the skull spins and copy its rage (`Frenzied`, defense
  override, speed; `ContactBase` uses the body's `setDamage`). Expert: `expertDefensePerHand` per living hand while
  hovering, and once a hand falls or life < `skullLife` homing `skull`s every `skullInterval` (half with no hands) when
  in sight (`skull_shot` cue). Day is a data `enrage` (9999 defense, 1000 damage, ×2.5 speed: it never stops
  spinning), shared by Skeletron Prime. Arms are drawn to living parts (`NpcRenderer.Arms`; the legacy segment-hand
  path is gone).
* **Skeletron Prime** — a 28,000-life skull (defense 24, doubled while spinning: `spinDefense` 24) with four boss parts
  on `skeletronHand` brains in Prime styles (`HandBrain.Styles`): `prime_saw` (9000, `saw`: jabs straight at the target
  every `attackInterval`, at most `reach` from its place), `prime_vice` (9000, `vice`: creeps up through its wind-up,
  then lunges), `prime_cannon` (7000) and `prime_laser` (6000), both `hold` (stay in place; their own `ranged` fires
  bombs and death lasers). Arms copy the skull's `Enraged` (below `phase2Life`), which swaps in their faster enraged
  volleys. Killing an arm leaves the fight going; killing the skull ends it and the arms die with it.
* **Wall of Flesh** — 8000 life shared by the mouth (body) and its two eye segments; lasers come from the eyes in turn,
  `ranged.intervalAtZeroLife` easing the interval from 70 to 25 ticks as it weakens (any creature may use it). A target
  more than `tongueDistance` (50 m) ahead of the face is Tongued: `the_tongued` (`noItemUse`) is pulsed each tick and
  `BrainContext.Pull` sets `PlayerEntity.ExternalVelocity` (cleared at the start of every `NpcSystem.Tick`) towards the
  mouth at `tonguePull` m/s until the player reaches it. `PlayerController.Status.UpdateTongue` replaces all movement
  with it after input (hooks let go, minecarts are left; headless players are moved by `GameWorld`). Leeches (a small
  `worm`, not tethered) crawl out every `leechInterval` × (0.4 + 0.6 × life) ticks, `maxLeeches` alive; The Hungry
  (240 life) stay tethered and die with it. Expert sweeps `expertSpeed` (×1.25) faster.
* **Pattern bosses** — `PatternBrain` (+`.Moves`) plays `brain.phases` (`PatternSpecs`, `PatternExtras`): in Expert a
  phase's `expert` block scales move speed, contact damage, volley interval/count (`expertCount`/`expertInterval` per
  volley win), summon caps and wind-ups, `expertBelow` replaces `below`, and `expertOnly` moves join the loop (the
  Classic loop, its RNG order and timings are unchanged). `fly` suspends a ground boss's gravity for the phase; `slam`
  rises above the target, drops and raises `Impacted` (and fires its volley once) on landing (`track` caps how fast it
  follows the target, 0 = a stomp in place); `summon.tether` makes
  minions followers on a ring that die with the boss and `leash` holds the boss near their centroid; `aura` pulses a
  buff (`expertOnly` optional); `illusions` sets `Npc.Illusions` with `MirrorAround` = the target (`AI.Illusions`
  places the copies a quarter turn apart); `defense` overrides defense (`DefenseOverride` is cleared before each think).
* **Queen Slime** — phase one hops three times, then rings crystals while her court (`summon.pool`: Crystal, Bouncy
  and Heavenly Slimes) gathers; below half life (`fly`, `queen_slime_winged`) she hovers 8 m over the target spraying
  bouncing Regal Gel (`queen_slime_gel`), slams down (impact + crystal ring) twice, then dashes. Expert: ×1.2 speed and
  +2 per volley. The `damage <enemy> <amount>` command takes life without killing, to reach later phases in smokes.
* **Duke Fishron and the Empress of Light** — the Duke has a third phase below 15% (×1.7 speed, teleport-dash chains)
  and an `enrage` outside the ocean (×2 damage, ×1.5 speed, defense 100); Expert ×1.15 speed. The Empress no longer
  leaves at dawn: a latched day `enrage` (`setDamage` 9999, ×1.2 speed) makes her lethal for the rest of the fight —
  `PatternBrain` volleys use a rage's `setDamage` too, so her shots kill like her touch. Expert: +2 per volley.
* **Brain of Cthulhu and confusion** — the Brain (1000 life) hides behind 20 Creepers (100 life each); its first phase
  is `untilGuardsFall`, so it is exposed (the next phase) at full life once they fall, as in Terraria: chase, teleport and
  double dash with three illusions (`NpcRenderer.Illusions`: pooled, dimmed copies of its model, no collision) and, in
  Expert, a confusion pulse; its touch may confuse. The `invertControls` effect (`forward: false` would reverse strafe
  only) sets `EffectState.InvertControls`; `PlayerController.Status` reads the movement keys (walking, minecarts,
  dashes) through `EffectState.SteerInput`, never the camera. Death recomputes effects after clearing debuffs, so
  confusion never outlives the player.
* **Queen Bee and Deerclops** — Queen Bee (3400) alternates charges and a stinger hover (volleys of 3 below half life)
  while calling bees; Expert adds speed, two stingers per volley and three more bees, and `boss.enrage` (target outside
  jungle, underground jungle and hive) doubles her damage and speeds her up 1.5×. Deerclops (7000) walks, hurls debris,
  stomps in place with a `slam` (`track: 0`; impact + a ring of ice spikes) and, below half life or in Expert, sends shadow hands that pass
  through walls; Expert also adds speed, denser volleys and an extra stomp.
* **Skeletron loot** — Terraria 1.4 odds (`skeletron_drops`: mask 1/7, Skeletron Hand hook 1/8, Book of Skulls 1/8;
  5–15 Lesser Healing Potions and about 5 gold always); the Expert bag adds the Bone Glove, whose `attackProjectile`
  effect (`ItemUseSystem.Gloves`) throws a `bone_glove_bone` from the hand along the aim after a weapon swing or shot,
  then waits `cooldown` ticks (`PlayerEntity.GloveCooldown`). The Clothier Voodoo Doll is a `summonBoss` item with
  `curse: clothier`: at night, while the Clothier lives and Skeletron does not, it curses the Clothier into Skeletron
  through the keeper's path (`TownCommerce.Curse`: the boss appears above them and they vanish until respawn).
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
  prices shop stock by happiness (liked/disliked biomes and neighbours, crowding, homelessness), buys into the
  backpack or onto the cursor (`BuyTarget`), sells from a slot or the cursor at `sellRatio` (favourites and coins
  refuse; an item of no value goes for nothing), keeps each player's last 40 sold stacks for buyback at what they
  fetched (`TownCommerce.Trade.cs`; not saved), and runs services (`heal` for coins with debuff removal, `craftHelp`, `summonBoss`). Landmark
  residents (the Dungeon keeper) are placed at a planned landmark once it streams in and stay near it; any resident
  whose `leavesWhen` holds leaves for good (`Departed`). Residents persist in the world metadata. Residents live in
  `data/town.json`, tuning in `data/config/town.json`.
* **Weather** — `WeatherSystem` (`Gameplay/Weather`) rolls once per in-game hour from the world seed and the hour
  (deterministic, independent of frame timing): rain spells (a share are thunderstorms), windy days at the roll hour,
  sandstorms on windy days, and the hour's wind; intensity and wind ease every tick. `WeatherState` lives on the session
  (saved as `WorldMeta.Weather`); `LocalAt(biomes, layer)` turns it into the weather of a place (snow and blizzards over
  snow biomes, sandstorms over sand biomes, nothing underground or in space), exposed as `ConditionContext.Weather` /
  `Windy` and the `weather` condition. The `rain`, `thunderstorm`, `windy_day` and `sandstorm` events (phase `weather`)
  start and stop with it, so announcements and `{ "type": "event" }` spawn rules follow. Tuning and presentation
  budgets in `data/config/weather.json`; `weather` command and `weather.*` probes.
* **Fishing** — rods (`fishing` behaviour: `power`, `lava` for the Hotline hook) cast a bobber that floats on water,
  honey and (with a `lava` rod or the `lavaFishing` stat of the Lavaproof Fishing Hook) lava; without that gear lava
  burns the line. Casting needs bait (`bait` behaviour with Terraria's bait power; worms, fireflies, butterflies and
  frogs from the Bug Net are bait too). `Fishing.FishingPower` is the pure math: (rod + bait + `fishingPower` stat) ×
  rain/storm (world-wide) × dawn/dusk/midday hours × moon phase × events (Blood Moon), scaled by the bobber's pool
  (6-neighbour flood fill once when it lands, capped at `fullPoolCells`; below `minPoolCells` nothing bites); bait is
  used up per catch with chance 1 / (1 + power/6 + `baitSaving`). `ProjectileSystem.Fishing` times bites by power and
  on reeling rolls a rarity tier (uncommon … legendary, chance 1 / max(floor, odds/power)), then lands today's Angler
  quest fish (`fishing_quest`, `anglerQuest` condition; only while a living Angler wants it), a crate (`crateChance`
  + the Crate Potion's `crateChance` stat) or a catch; each table is tried as `<table>_<tier>` falling back to commoner
  tiers. Tables: `data/loot/fishing.json` (water/honey via `fishingLiquid`, lava, crates), crate contents in
  `data/loot/crates.json` (biome crates need their biome), tuning in `data/config/fishing.json`. Command `fishing`
  (`bite`, `bait always|normal`), probes `fishing.bobber|power|pool|catches`; smoke `fishing.cmds`.
* **Wiring** — `Wiring.WireGrid` (in `WorldSession.Wires`) keeps a bit per wire colour (red, blue, green, yellow) and an
  actuator bit per cell, plus the blocks actuators have switched off; world.json keeps red in `wires` (older saves load
  unchanged) and the rest in `wiresBlue|Green|Yellow`, `actuators`, `inactiveBlocks`. `WireSystem.Signal` walks each
  colour separately from wire inside or face-sharing the trigger (reused scratch collections, at most 4096 cells per
  colour) and activates what it reaches: doors, lights, dart traps, actuators (toggles gathered per chain and applied in
  one edit; a switched-off block is air, remembered with shape and paint, and drops as an item if something was built in
  its place), timers (`timer` ticks; switched by hand or signal, fired from `WireSystem.Tick` over the placed timers
  only), teleporter pads (the first two on a colour swap their occupants; `Teleported` moves the player's body),
  statues (`statue`: creature or item with per-statue cap, radius and cooldown, at most 10 statue creatures world-wide;
  `Npc.FromStatue` drops nothing) and logic lamps; a gate (`logicGate`) re-reads its lamp stack and, when its output
  changes, fires its own wires (not those through its lamps) once per chain, at most 256 chained signals. Wrenches lay
  their `color`, the Actuator places itself, the Wire Cutter removes everything in the cell. `game/Rendering/WireOverlay`
  draws the wire view per colour (pooled MultiMeshes, rebuilt on change or after 8 cells of movement). Command
  `wire … [colour|actuator|cut]`, probes `wires.<colour>|actuators|inactive|teleports|statue_npcs`.
* **Seasons** — `Seasons.SeasonState` (`WorldSession.Seasons`) names the seasonal event running: Halloween (Oct 10 –
  Nov 1) or Christmas (Dec 15 – 31) from inclusive date windows in `data/config/seasons.json`, read through an
  injectable `Clock` (tests pin it; `TestGame` uses a date outside every season) and rechecked every `recheckSeconds`;
  the player's "Seasonal events" setting (`UserSettings.SeasonalEvents`, Settings > Gameplay) switches it off, and the
  `season halloween|christmas|off|auto` command forces it for the session (probe `season.id`). `ContextAt` fills
  `ConditionContext.Season`, so any spawn rule, loot entry, shop entry or town rule can use
  `{ "type": "season", "is": "halloween" }`. Every slain hostile creature (not critters or statue creatures) also rolls
  `NpcSystem.GlobalDropTable` (`enemy_global_drops` in `data/items/seasonal.json`: goodie bags at Halloween, presents
  at Christmas) with seasonal vanity, candy and decorations inside. Santa Claus moves in at Christmas once the Frost
  Legion is beaten and leaves when it ends; `TownRenderer.Hats` puts witch or Santa hats on the other residents (shared
  procedural meshes on the head bone, toggled on season change).
* **Secret seeds** (X-07) — `World.SecretSeeds.Parse` recognises Terraria's special seed names (any case, spaces and
  punctuation ignored; "get fixed boi" sets all) into `WorldSettings.Secret` (`SecretSeed` flags, saved as
  `WorldMeta.Secrets` ids). Generation reads `Plan.Settings.Has(…)`, so ordinary seeds generate exactly as before:
  Drunk World alternates Corruption and Crimson regions (at least one of each) and both altars; Not the Bees makes the
  jungle 1.5× wider with 4× the hives; No Traps multiplies the Dungeon halls' dart-trap chance by 6. Gameplay: For the
  Worthy plays one difficulty step harder (`WorldSettings.EffectiveDifficulty` drives enemy scaling, defense factors and
  `ConditionContext.Difficulty`); The Constant dims sky light (`EnvironmentController.Dim`); Celebration lets the Party
  start without the Party Girl (`secretSeed` condition). Don't Dig Up is recognised and saved only (no upside-down
  generation yet). The menu picks a random number for a secret name and lists found secret seeds under the seed field
  (`UserSettings.DiscoveredSeeds`); `--secret=` for dev starts and `worldgen-report`.
* **Signs** (X-03) — tile objects with `interaction: "read"` (the Sign: 6 wood at a work bench). Interacting raises
  `ObjectInteraction.SignOpened`; `game/UI/SignDialog` edits the text and `GameWorld.EditSign` stores it in
  `TileObjectInstance.SignText` (up to 6 lines, 240 characters, control characters dropped), saved as chunk object-meta
  tag 3. The target label shows the text under the sign's name. Command `sign [text|clear]`, probe `signs.text`.
* **Enemy banners** — `Banners.BannerSystem` (`GameWorld.Banners`): kills of a banner's creatures (`TileObjectDef.Banner`)
  are tallied world-wide in `Session.Counters["banner:<id>"]` (statue creatures, parts and bosses never count) and every
  `killsPerBanner` (50) drops the banner. Every `refreshTicks` it rescans each player's surroundings (`radius` cells) for
  placed banners: `Dealt` raises player damage to those creatures (×1.5, in `NpcSystem.Hit`), `Taken` lowers their
  contact damage (×0.9, `NpcSystem.Contact`), and the Monster Banner buff shows. Tuning in `data/config/banners.json`;
  probes `banners.near|dropped`. Enemy projectiles carry no source creature, so they are not reduced.
* **Mounts** — the `mount` item behaviour (`MountParams`, data/items/mounts.json) summons a ride: `MountRules` toggles,
  swaps and quick-mounts (the `quick_mount` key; the equipment mount slot plugs in through `MountRules.SlotMount`),
  shows the mount's buff and dismounts on death; `MountMotion` (engine independent, tested) gives each mount its run
  speed, jump (with a light-gravity hold), limited flight (refilled on landing) or free hovering flight, and the
  Slimy Saddle's stomp. `PlayerController.Mounts.cs` drives the body with it (no fall damage while riding);
  `PlayerAvatar.Mount.cs` draws the creature model scaled to the mount's height with the rider in the saddle. Bosses
  drop mounts through `extra_<enemy>` loot tables rolled on top of their own.
* **Players** — `PlayerState` (inventory, equipment effects, buffs, vitals, spawn points) is saved per
  character; `PlayerEntity` is the in-world body the engine syncs; death → 5 s → respawn at bed or spawn.
* **Slot model** — `PlayerInventory` (partials `.Equipment`, `.Storage`) holds Terraria's storages: backpack 50,
  coins 4, ammo 4, a saved trash slot, armor 3, accessories 7 (`AccessorySlotsUnlocked` 5–7, set by
  `GameWorld.AddPlayer` from the world difficulty: Expert 6, Master 7), vanity armor 3 and accessories 7, dyes 10
  (armor 0–2, accessories 3–9), the misc row (`MiscSlot`: pet, light pet, minecart, mount, hook; `EquippedHook`,
  `EquippedMount`…) with its dyes, and a service slot. Only armor and unlocked accessories grant stats
  (`EquippedStacks`); any equipment change raises `EquipmentChanged`, which `PlayerState` answers with `Recompute`.
  `EquipFrom` routes an item by `EquipSlot`/category (vanity row, rotating accessory swap when full, first free
  dye, misc slot); `ArmorFilter`/`AccessoryFilter`/`DyeFilter`/`MiscFilter` gate clicks. Favourites are per slot
  (`Inventory.IsFavorite`, only backpack/coins/ammo keep them; `Transfer` carries them, chests drop them), never on
  `ItemStack`. `AllStorages` is what a Mediumcore death drops (not the trash); `CountAll` plus the trash backs the
  `inv.<item>` probe (`inv.trash`, `inv.cursor`, `inv.coins` too). Content items of category hook/mount/minecart/pet
  must name the matching `equipSlot`.
* **Purse and ammo** — `PlayerInventory.Pickup` (partial `.Purse`) routes like Terraria: coins merge into the coin
  slots and `Coins.Normalize` turns each 100 into 1 of the next (each denomination keeps its slot; overflow to the
  backpack; it needs `PlayerInventory.Content`, set by `CreateNew`, `PlayerSave.ToState` and `AddPlayer`), ammo
  (category ammo with an `ammoClass`; Fallen Stars stay in the backpack) fills the ammo slots, the rest the backpack.
  The purse is `Coins.Total(PlayerInventory)` (coin slots + backpack): `Wallet.TrySpend/Give(PlayerInventory, …)` pay
  from the coin slots first, break backpack coins only when needed and return change to the coin slots; shops,
  services, the Nurse, taxes, the Angler, Classic death and town `coins` conditions use it. Shots take
  `PlayerInventory.FindAmmo` (ammo slots top to bottom, then the backpack); `CountAmmo` is the hotbar count;
  `CanTake` is the pickup-aware room check. Player crafting (`RecipeBook.CanCraft/Craft(PlayerInventory, …)`,
  `CraftingEnvironment.Browse(…, PlayerInventory)`) counts and consumes the backpack, ammo and coin slots (wooden
  arrows for flaming arrows) and picks the output up.
* **Slot clicks** — `GameWorld.ClickSlot(p, SlotRef(area, index), right, ClickMods, SlotContext)` (partial
  `.InventoryActions`) is the one entry point for the inventory screen, `invclick` and tests. With an empty cursor
  Alt toggles the favourite (backpack/coins/ammo), Ctrl (no shop) moves the stack to the trash slot (favourites
  refuse), Shift quick-moves (chest ↔ backpack via `Pickup`, equip from the backpack, unequip, sell with a shop
  open). Right click opens anything whose consume behaviour has a `loot` table (`ItemUseSystem.OpenFromSlot`, event
  `Opened`), else `SlotActions.RightClick`: equip and swap from backpack/chest, armor/accessory ↔ vanity swap at the
  same index, otherwise take ONE onto the cursor (held: `HoldRepeat.IntervalTicks` 30, 22, 17 … 1). Left clicks are
  `SlotActions.LeftClick` with `SlotFilters.For(slot)` (coins only in coin slots, `FitsAmmoSlot` in ammo slots, armor
  kind, accessory without duplicate, dye, misc kind). `ThrowCursor`/`ThrowStack` toss stacks the way the drop key does.
  Favourited slots draw gold with a gold wash on the inventory screen and the hotbar (`ItemSlotView.Favorite`); the
  view itself repeats a held right click, and the screen stops the repeat once a click no longer moves items.
* **Chest and inventory buttons** — `InventoryOps`: `LootAll` (via `Pickup`, coins to the purse), `DepositAll` and
  `QuickStack` (backpack 10–49 only, never favourites or coins; quick stack only items the chest already holds,
  topping up its stacks first), `Restock` (tops up backpack and ammo stacks from the chest), `Sort(inv, from, to)`
  (merge, then group weapons/ammo, tools, armor/accessories, potions, placeables, materials, misc; rarer first; then
  id; favourites keep their slot). `GameWorld.ChestAction(p, chest, ChestOp)` runs a button (rolling world loot
  first) and marks the chunk modified; `QuickStackNearby(p, 12 m)` does it for every unlocked container in range,
  nearest first, returning `(chest, moved)`. `TileObjectInstance.CustomName` (≤ 20 printable characters,
  sanitised on assignment; `DisplayName` falls back to the object's name) is set by `GameWorld.RenameContainer` and
  saved in the chunk's object meta; locked chests (`Locked`, see *Locked chests and keys*) are skipped. Commands: `chest [loot|deposit|
  quickstack|restock|sort|rename <name>]` (the chest opened last), `invsort`, `quickstack`; smoke `inventory.cmds`.
* **Tooltips and display helpers** — `ItemTooltip.Lines(stack, info, TooltipContext)` builds Terraria-ordered
  lines: name with prefix and count, damage/crit/speed/knockback after the prefix and the viewer's class bonuses
  (`DamageModel.DamageMultiplier`/`CritBonus`, the same maths a hit uses), info lines, the description or the buff's
  text (`BuffText.Describe`: data wording, else derived from effects) with `BuffDisplay.Duration`, one green/red
  line per non-zero `PrefixDef` field, the worn set bonus, and the price (`Prefixes.SellValue`, shared with
  `TownCommerce.SaleValue`; kind by `Coins.TopTier`, "No value" at 0). `BuffDisplay` (timer text, cancel/timer
  rules incl. `hideTimer`), `VitalsDisplay` (hearts, golden hearts, stars, bubbles) and `Content.Index.StationNames`
  ("Iron Anvil or Lead Anvil", cached per database) are the Core maths the HUD, crafting and the Guide read.
* **Chat log** — world messages go to `ChatLog` (game/UI/Hud/ChatLog.cs) in the layout's Chat region, never a
  centred banner: `NpcSystem.Messaged` (coloured by `MessageColors.Hex(MessageKind)`; it carries "has awoken!" and
  "has been defeated!" too), `BossLeft`, town arrivals/departures (blue) and deaths (red), events (invasions purple,
  others green), progression and objective lines, and catches in rarity colour — wired in `Hud.Messages.cs`
  (`Hud.Announce(text, colour)` forwards there). `ChatHistory` (Core) keeps the last 50 lines and the fade maths (10 s
  hold, 1 s fade, the last 10 shown); the `chat` action (Enter) opens the history as an overlay gate. The optional
  `BigAnnouncements` setting adds a 24 px banner for "has awoken!" only. Command `ui chat <text>`/`ui history`, probes
  `ui.chat_lines`/`ui.chat_open`.
* **Pickup popups** — `GameWorld.ItemPickedUp` feeds `PickupPopups` (game/UI/Hud/PickupPopups.cs), drawn over the
  player's head (0.9 m above it, projected; a fixed point at W/2, 0.38 H in first person or behind the camera).
  `PickupFeed` (Core) keeps 8 popups keyed by item and prefix (all coins share one, summing copper for
  `Coins.Format`): a pickup within 2 s joins its popup, restarts it and pops it (1.25 → 1 over 0.12 s); each shows
  1.5 s, rising 40 px and fading over the last 0.5 s, the latest at the bottom row (22 px apart). Text in rarity
  colour (Expert/Master animate each frame) or the largest coin's colour. `drop … <delay ticks>` and probe
  `ui.popups` test it.
* **Boss bar and invasion progress** — `BossBarModel.Compute(npcs)` (Core, allocation-free, run each frame) picks
  the strongest living non-minor boss (a body before its parts, by maximum life, a worm counted whole) and sums
  everything sharing its defeat flag: a pair (the Twins) adds up, a cut worm counts its arrival life once, and a
  boss's parts count only when flagged `BossPart.InBossBar` (Golem's head, Moon Lord's head and hands; never
  Skeletron's hands or Golem's fists, which keep their floating mini bars); a fallen part drops out. `BossBar`
  (game/UI/Hud/BossBar.cs, the layout's BossBar region) draws the title, the head icon (`PortraitIcons.Boss`,
  preloaded by the HUD; a disc with the initial if missing), a framed red bar with "life/max" and a pale damage
  chunk that holds 0.4 s, then slides down; it fades in on arrival. `EventProgressPanel` (EventBar region) follows
  `EventDirector.LatestInvasion` (latest event with a kill goal) with its `StatusIcons` icon, name and
  `EventDirector.Progress(a)` (kills over `GoalOf`) as "NN%", and holds its last value 3 s after the event ends, then
  fades. Their `Active` flags feed `UiState.BossBar/EventBar`. Probes `ui.bossbar`, `ui.bossbar_fill`,
  `ui.eventbar`, `ui.eventbar_percent`.
* **Death messages and the death screen** — every hurt records `PlayerEntity.LastHarm` (`Harm(HarmKind, Source,
  Id)`, Gameplay/Combat/Harm.cs): `NpcSystem.ReportPlayerDamage(player, damage, knock, harm)` takes it from creature
  contact and walls (`Harm.By(EnemyDef)`: bosses by name, others "a Zombie"), shots (`Projectile.Shooter`, set for
  creature volleys and brain shots, else the projectile's own name), spikes (`Trap`), lava and drowning
  (`LiquidEffects`); falls set it in `PlayerController.Fall`, damage over time in `GameWorld.TickPlayer` from
  `EffectState.DotSource` (the buff dealing the most), and the `kill` command `Other`. On death `GameWorld.PlayerSlain(p,
  text)` fires next to `PlayerDied` with `DeathMessages.Format(name, harm, deaths − 1)` (Core/Presentation,
  data/config/deaths.json: lines per kind plus per-source lines such as `on_fire`; picked by death count; `Load`
  rejects a kind without a line; loaded by the launcher into `GameWorld.Deaths`). `DeathScreen` (game/UI/Hud) dims the
  view to 55 % over 1 s under "You were slain..." (48 px, `UiTheme.Death`; hardcore: "Name has perished..." and
  "Hardcore characters do not return."), the cause and "Respawning in N", and fades out on respawn; the cause also goes
  to the chat log in red. Probes `ui.death_dim`, `ui.death_cause`.
* **Object hazards** — a tile object with `hazard {damage, knockback}` (Dungeon spikes) hurts a player whose box
  (grown by 0.05 m) overlaps it: `GameWorld.TouchHazards` rolls the hit against defense, respects immunity frames
  and god mode, and reports it with knockback away from the spike and upward. A non-container object's per-instance
  `PendingLoot` replaces its kind's `loot` when broken (the one Water Bolt among the Dungeon's shelf books).
* **Dungeon loot and keys** — dungeon rooms place `dungeon_chest` (one weighted treasure: Muramasa, Cobalt Shield,
  Aqua Scepter, Handgun, Blue Moon, Magic Missile, Valor; Bone Welder 14 %) and one `dungeon_chest_shadow_key` per world
  (`data/loot/dungeon.json`). `NpcSystem.Kill` rolls `global_kill` (`data/loot/global.json`: Golden Key 1/65 in the
  dungeon zone) after a creature's own loot for kills worth coins, never critters or parts. `ProjectileDef.guided`
  (Magic Missile) steers toward the owner's aim ray at the projectile's own distance from the eye while the owner holds
  the item that fired it. A fallen town resident drops table `<id>_death` when it exists; its conditions may test
  `wearing` (an equipped item, the Clothier's Red Hat).

## Bosses and the Dungeon (summary)

Where to look: boss bodies, parts and brains (fixed `parts` such as Skeletron's two hands on bone arms, which fall on
their own while the skull fights on; segmented worms), `boss.enrage` and Expert context (`BrainContext.Expert`, the
`expert` loot condition), treasure bags (`<boss>_bag`, Expert and Master) and natural arrivals are in the creature
notes above; the Dungeon's zone, crypt-wall locks (picks bounce until `defeated_skeletron`), the invulnerable
Guardian (spawn rule `dungeon_guardian_trespass`, only while the flag is unset, despawns outside the zone), the Old
Man at `dungeon_gate` (curses into Skeletron at night, leaves when it falls) and the exclusive roster in
`data/enemies/dungeon.json`; spikes and dart traps under *Object hazards*; locked chests and keys below. The
smoke `dungeon.cmds` drives the whole arc through landmarks (`tp landmark dungeon_shaft`, `dungeon_library`,
`dungeon_spikes`, `dungeon_bottom`, `dungeon_chest`) and checks `near_object.book` (tile objects of an id within 16
cells, `ObjectProbes`), `props.problems` and `errors`.

## Disguised creatures and the Wyvern

* **Disguises** — an enemy with `disguise` (`DisguiseInfo`: `model`, `name`, `wakeRange`, `wakeOnInteract`, `wakeSound`,
  `lunge`) spawns `Npc.Disguised`: `NpcSystem.SitDisguised` only settles it under gravity (no brain, contact damage or
  volleys) and `Npc.ShownModel` draws the disguise. It wakes for good (`NpcSystem.Wake`, `Unmasked` event) when hit
  (`Hit`, damage over time), when a player comes within `wakeRange`, or on interaction:
  `ObjectInteraction.DisguisedAlong` picks the disguised creature under the crosshair (in front of the terrain hit) and
  `ObjectInteraction.Interact(player, npc, reach)` → `NpcSystem.Provoke` returns `InteractOutcome.Unmasked`. The Mimic
  sits as `mimic_chest` (built by the Wooden Chest's own generator, yaw squared to 90°; wakes at 1.4 m, lunges); the
  Nymph stands as the `lost_girl` (wakes at 4.5 m). Mimics spawn from the hardmode cavern spawn rule (no worldgen
  placement). Console: `open_disguised`; probe `boss.<id>.disguised`.
* **Biome Mimics** (`data/enemies/biome_mimics.json`) — Ice, Corrupt, Crimson and Hallowed Mimics: hardmode, rare
  (`maxAlive` 1), in the underground/cavern layers of their biome; the Ice Mimic replaces the plain Mimic under the snow
  (the `mimic_cavern` rule excludes `tundra`). Each sits as `<id>_chest`, the `mimic_chest` generator with the exact
  params of the vault's biome chest (P4-34), so it is indistinguishable from the real one. Loot: the Mimic (and Ice
  Mimic) roll one of six (`mimic_treasure`); the Ice Mimic adds the Toy Sled; the biome Mimics roll one of their four
  plus 5–10 Greater Healing Potions. Accessories (`data/items/mimic_loot.json`): Titan Glove = stat `meleeKnockback`
  (applied to melee hits in `DamageModel.Roll`; every melee weapon already auto-swings here), Philosopher's Stone =
  stat `potionDelay` (−25 %: `ItemUseSystem.PotionSicknessTicks`), Star Cloak = effect `hurtProjectile`
  (`ProjectileSystem.CallHurtProjectiles`, run from `NpcSystem.ReportPlayerDamage` for every non-periodic hurt: three
  stars fall from 16 m onto the ground around the wearer).
* **Wyvern** — the worm brain with `fly` (`WormBrain.Fly`): constant speed and a limited turn rate so it overshoots and
  loops, a vertical `weave`, ×`catchUp` beyond `farRange`, circling its spawn point without a target; never leaps or
  falls and ignores terrain. `boss.segmentPattern` cycles models along the body (legs every eighth piece; tail last) via
  `BossInfo.ChainModel`. Burrowing worms keep `fly: false`.

## Commands, probes and smoke scripts
`CommandRegistry` (Core) + `EngineCommands` (Godot). Probes are named numbers (`player.life`, `inv.<item>`,
`npcs.<enemy>`, `block.dx.dy.dz`, `liquid.dx.dy.dz`, `near.<block>`, `near_object.<object>`, `liquid.active`, `town.<id>`, `town.<id>.home`, `town.<id>.alive`, `zone.<biome>`, `flag.<id>`, `house.valid`, `stream.idle`, `errors`, …); debug commands such as
`god`, `drop`, `slay` and `tp landmark` make fights and summons scriptable used by `assert` and `wait_until`.
`game/Testing/Scripts/*.cmds` run headless in CI with `--smoke=` and print `SMOKE PASS`/`SMOKE FAIL`;
`--exec="cmd;cmd"` runs commands at startup (screenshots, manual testing). Runner directives: `wait`,
`wait_until <probe> <op> <value> [timeout]`, `hold <action> <seconds>`, `repeat <n> <command>`.
Probes come from `StandardProbes` and from `[ProbeSource]` classes (static `Register(CommandContext ctx, …)`):
`CommandContext` registers those in Strata.Core (e.g. `TownProbes`), `GodotCommandHost.CreateContext` those in the game
assembly (e.g. `PlayerProbes`, which also takes the `LaunchedWorld`), in full-type-name order (`ProbeSources`). A
probe name is registered once; new probes go in a new source file (docs/CONVENTIONS.md, *Extension points*).
`god on` (`Vitals.Undying`) stops every damage path (contact, shots, explosions, lava, debuff ticks, falls, drowning)
at 1 life without counting a death (`GodModeTests`); only `kill` gets past it. A script that turns it off clears
debuffs and heals first (`buff clear`, `heal`), or the next debuff tick kills a player left at 1 life.

**CI coverage (P5-33, consolidated at the final integration).** `.github/workflows/ci.yml` runs, per push: the module
size guard, the solution build and every Core test; the generated-content checks (`gen_ore_tiers`, `gen_furniture`,
`gen_banners`, `manifest --check`), `validate`, `gen_reference --check`, `build_all.py --check`, `build_ui.py --check`,
`icon_mipmaps.py --check` (boss, NPC and creature portraits included) and the coverage report; then the Godot import,
every `game/Testing/Scripts/*.cmds` headless and `--menu-tour`. The town and guide systems of Phase 5 are covered by
`town.cmds` (move-in, seating, dawn respawn, corrupted housing, the housing API, services in the chat box, shop tabs
and currencies, talking and trading, name tags, residents fighting back, the Travelling Merchant, town pets, pylons),
`rescue.cmds` (a bound resident's clip and posture, freeing, moving in), `dungeon.cmds` (the bound Mechanic),
`guide.cmds` (journal, bestiary, achievements, the Guide's help, hints and every Guide book tab) and the Core tests in
`tests/Strata.Core.Tests/Gameplay` (town, housing, journal, bestiary and achievement tests). `gen_color_vision.py --check`, `build_emotes.py --check`
(X-12, X-05) and `tools/audio/build_audio.py --check` need numpy, Pillow or soundfile, which the CI job does not
install: run them locally with the rest before a merge (the final integration did).

## Locked chests and keys

A chest definition may carry `lock` (`key`, `consume` default true, `requiresFlag`, `message`); it only applies to
instances with `TileObjectInstance.Locked`, persisted as meta tag 2 of the object record in `ChunkCodec` (beside the
chest name; old chunks read unlocked). Generation places them through `ObjectOp.Locked` (`Blueprint.Object(..., locked: true)`, copied by
`FeatureCarver.Stamp`): every dungeon Gold Chest (Golden Key, used up; one rolls `dungeon_chest_shadow_key`) and the
underworld towers' Shadow Chests (Shadow Key, kept; table `shadow_chest` with Sunfury, Dark Lance or Flamelash,
hellstone bars and potions, which left `cabin_chest_primary`). `ObjectInteraction` (Locks partial) refuses with the
lock's message while `requiresFlag` is unset, then "It's locked." without the key; opening raises `Unlocked`
(`SoundEvents.Locks`: `unlock`; any refusal plays `locked`). `WorldEditor.HitObject` returns `Locked` for a locked
chest, rolls an unopened chest's loot before the not-empty check, `BreakObject` rolls it before spilling, and locked
chests are never broken for lost support, so generated loot is never lost. `interact nearest <objectId> [radius]
[expectedOutcome]` drives it from smoke scripts.

## Player journal and bestiary (P5-25)

`PlayerState.Journal` (`Gameplay/Guide/PlayerJournal.cs`) records per character — not per world, unlike the
reference game — kills per enemy id, creatures seen and caught, residents met, items obtained and crafted, named
counters (`mined`, `mined:<block>`, `mined:#<tag>`, `broken:…`, `trees_chopped`, `purchases`, `reforges`,
`deaths`, `catches`) and unlocked achievements. It implements `IPlayerRecords` for conditions and raises
`Changed(kind, key)`. `JournalSystem` (on `GameWorld.Journal`) feeds it: `Npcs.Killed` credits players within
`creditRadius` (bosses and parts credit everyone), `ItemUse.NetCaught`, `GameWorld.Crafted` (raised by
`GameWorld.Craft`, which the crafting menu and the `craft` command use), `Commerce.Traded`, `Services.Reforged`,
block/object breaks (nearest player), deaths, plus sweeps: creatures within `seenRadius` every 30 ticks, residents
talking to the player (met), and every carried item once a second (obtained). Recording an existing key allocates
nothing; counter keys are cached per def. `Bestiary` lists enemies (minus `bestiaryAs` parts and `no_bestiary`) and
town residents; `Unlock` gives None/Seen/Stats/Full from `data/config/bestiary.json`; parts' kills count for their
entry. `BestiaryPercent` is cached until the journal changes; `JournalSystem.BestBestiaryPercent()` is the
world-wide value for town conditions. Saved as `PlayerSave.Journal` (additive; unknown ids kept). Probes
`bestiary.percent`, `bestiary.<entry>[.unlock]`, `journal.<obtained|crafted|seen|caught|met|kills|counter>.<id>`;
commands `journal [reset|unlock <enemy> [n]]`, `craft <item> [times]`.

## Achievements (P5-26)

`data/achievements.json` (category `achievements`, `AchievementDef`: name, description, icon item, category,
`trigger`) lists ~45 of the reference game's achievements that map to implemented mechanics. Triggers
(`SpecKind.AchievementTrigger`, `Content/Specs/AchievementSpecs.cs`): `flag{id|all}`, `obtainItem` / `craftItem`
`{item|tag|category}`, `killEnemy{enemy|tag,count}`, `mineBlock{block|tag,count}`, `breakObject{object|tag,count}`,
`housed{min}`, `bestiary{min}`, `talkTo{resident?}`, `buy{min}`, `reforge{min}`, `coins{min}`, `reachLayer{layer}`,
`counter{key,min}`; `GuideTrigger` parses one and measures progress allocation-free (the guide's steps reuse it).
`AchievementTracker` (`GameWorld.Achievements`) indexes triggers by the `JournalKind` that can advance them and
evaluates only those on `Journal.Changed`; world-state triggers are swept at 1 Hz, and a player's first sweep
evaluates all. Unlocks are stored in `Journal.Achievements` (Unix seconds) and raise `Unlocked(player, def)`:
`game/UI/AchievementToast.cs` (one pooled panel, queued, 4 s, slide tween) and the `achievement` sound
(`tools/audio/sfx_recipes_guide.py`). Probes `achievement.<id>`, `achievements`; command
`achievement [grant|progress <id>]`. Not yet: `rescued` and `anglerQuests` triggers (no per-player record of
rescues or completed quests on this branch), a chat line (no chat log yet).

## Guide progression and tips (P5-27)

`data/guide/steps.json` (`guideSteps`, `GuideStepDef`: text, order, `after`, `doneWhen`/`showWhen` achievement
triggers, related items/enemies, stage) holds ~30 steps of the reference game's progression; `data/guide/tips.json`
(`guideTips`: text with `{key:<input action>}` placeholders, topic, controls action) ~40 tips. The validator checks
references, trigger shapes and `after` cycles (`ContentValidator.Guide.cs`). `ProgressionAdvisor`
(`GameWorld.Advisor`): `Steps(player)` (done / available), `Next(player, n)`, `Advice(player)` — the Guide's Help
line. A town service type `advice` (`AdviceParams`) is registered for it, but the conversation UI
(`TownDialog.Services`) is not wired yet: the Guide keeps `craftHelp` until the W4 dialog lands. Command `advice
[steps]`; probes `guide.next.<step>`, `guide.done.<step>`.

## Drop rates (P5-24b)

`Gameplay/Loot/DropRates.cs` mirrors `LootRoller`: an each-mode entry drops with its `chance`, a weighted entry with
1 − (1 − weight/total)^rolls over all entries (conditional ones listed with their conditions), nested tables multiply.
`DropRates.For(content, table)` flattens a table into `DropInfo(item, chance, min, max, conditions)`;
`DropRates.Coins(table|enemy)` gives the copper range (table coins, else the enemy's value ±25%). `ItemSourceIndex`
now fills `ItemSource.Chance` for every loot-derived source with the same maths (null for recipes, shops, tags, …).

## Onboarding hints and loading tips (X-10)

`data/hints.json` (`hints`, `HintDef`: text with `{key:action}`, `priority`, `delay`, `duration`, `when`/`unless`
achievement triggers, `time` any/day/night/dusk, `carrying` item, `near` {object|tag|tree|resident, radius m},
`step` = the Guide's next step, `guide` = the Guide book entry, a guide step or tip id) holds ~17 hints: chop the
tree, night is coming, place a torch underground, talk to the Guide, first death, boss nudges per advisor step,
hardmode. `HintDirector` (`GameWorld.Hints`, checked with the milestones once a second per player) shows the
highest-priority due hint (ties by id) once per character, at most one per `CooldownSeconds` (25) and none in the
first `StartSeconds`; timing counts checks, not the world clock, so time skips never stall or flood it. Shown ids
are `PlayerJournal.HintsShown`, saved as `JournalModel.Hints` (additive). `Enabled` off shows and marks nothing.
`game/UI/HintToast.cs` (pooled themed panel above the hotbar, fade, "Guide book: <entry>" line) and
`game/UI/LoadingTips.cs` (cover with "Loading world..." and rotating guide tips while the player is frozen waiting
for the ground; teleports under 0.3 s don't flash it) are added in `WorldLauncher.AddUi`. Settings
`UserSettings.ShowHints` / `LoadingTips` (settings.cfg `showHints` / `loadingTips`); `hint on|off` sets the
former and saves it. Command `hint [list|reset|show <id>|due <id>|on|off]`; probes `hints.shown`,
`hints.enabled`, `hint.<id>[.due]`. Not yet: rows in the settings screen (sequenced with P2-30), the menu's own
"Generating world..." overlay has no tip (MainMenu is integration-owned), the Guide book pages (P5-30).

## Guide crafting help, Core part (P5-14)

`RecipeLookup` (`GameWorld.RecipeLookup`, over the `RecipeBook` indexes): `Making(item)` / `Using(item)` →
`RecipeView` (output, count, `IngredientView`s labelled "Any Wood" for groups, station names from `StationNames`,
condition text, `StationLine` "Required objects: Iron Anvil or Lead Anvil"), `StationFor(item)` (recipes a placed
item serves as station); views are cached per recipe and thread-safe. `ConditionDescriber` turns every condition
type into English ("at night", "after the Eye of Cthulhu is defeated", "during a Blood Moon", "The Merchant lives in
town", "carrying 50 silver"); unknown types read as their name. `TownCommerce.RecipesUsing` now reads the index.
Not yet: the Guide's TownDialog slot panel (GuideCraftPanel, `ui guide_craft`) waits for the W4 TownDialog; the
Guide book's Items tab shows the same views.

## Guide book and Items tab (P5-30)

`game/UI/GuideBook/GuideBookScreen.cs` (CanvasLayer 24, gate `guide`, node `GuideBook`, added in
`WorldLauncher.AddUi`) toggles with `guide_book` (K; G is the quick mount) and closes on Esc. It is built on first
open; each `IGuideTab` (Items, Bestiary, Guide) builds on its first selection. Core `GuideIndex` (items by name with
search keys, `ItemSourceIndex` rows as `SourceView` with `DropRates` chances and `ConditionDescriber` text, its own
`RecipeLookup`) is built by `GuideIndex.BuildAsync` on a worker thread at launch; the Items tab says "Loading…" until
then. Items tab: search (sets `InputGate.TextEntry` while focused), category filter, names at once and icons 16 a
frame; `ItemDetails` shows the tooltip lines, "Obtained from" (rows link to creatures and items), "Crafted from",
"Used in" (8 recipe cards a page; ingredients link) and "Crafting station for N recipes", with Back history; rebuilt
only on selection. Steps and tips moved to the P5-32 tabs. A hint's Guide link (X-10): pressing
K while a hint with a `guide` entry is up opens the book there. Command `book <tab|item|bestiary|entry <id>|close>`;
probes `ui.book_open`, `ui.book_ready`, `ui.book_tab`, `ui.book_items`, `ui.book_sources`, `ui.book_guide`.
Not yet: Bosses/NPCs/Achievements tabs, the inventory's book button (InventoryScreen is W4-owned), a page-turn sound.

## Bestiary tab (P5-31)

`BestiaryTab` lists `Bestiary.Entries` ("???" until seen) with filters All/Unlocked/Bosses/Critters/Town, search over
known names, completion % and portraits (`PortraitIcons`) loaded a few per frame. `BestiaryDetails` by tier: seen →
portrait and habitat (Core `Habitat.For`: each spawn rule of the entry through `ConditionDescriber`, events it ends,
summon items from `summonBoss` behaviours); stats → life, defence, damage (scaled by `NpcSystem.Scale`), knockback
resist, coins, description; full → every drop with its chance. Residents show the description and when they move
in. Probe `ui.bestiary_unlock`. Not yet: the rotating 3D preview (thumbnails stand in), biome filter.

## Town population and housing (P5-01..P5-05)

* **Population** (`TownSystem.FillTownFacts`): a resident counts (`TownNpc.CountsAsResident`) while alive, not `Bound`
  (awaiting rescue) and of kind `Resident` (landmark keepers such as the Old Man never count). The `residents`
  condition reads `ConditionContext.ResidentCount`, the number of counting residents with a home; `resident <id>`
  reads the counting ids. Events (the party) use the same facts.
* **Move-ins** happen only by day (`CheckArrivals(force)`; `town check force` for tests); homeless residents are
  re-housed at any hour. A fallen or vanished resident returns at the first dawn after `DiedAtTick` once
  `minDeadTicks` have passed (`TownSystem.Life.cs`; `respawnAtDawn: false` restores the fixed delay). Setting the
  clock back clamps `DiedAtTick`. `Bound`/`DiedAt` are optional `ResidentModel` fields (old saves load with defaults).
* **Housing validity** (`HousingScanner`): Corruption/Crimson blocks (tag `evil`, not `ore`) within `evilMargin` of a
  room, sampled every other column, make it "This housing is corrupted." at `evilLimit` hits. Problems and
  `Room.Message` use Terraria's query wording from `housing.json` `messages`.
* **Room cache** (`GameWorld.Rooms`, `HousingCache`): rooms are cached by every interior cell and dropped when a block
  edit (liquids ignored) or a tile object add/remove touches their bounds grown by `evilMargin + 1`; open results
  expire after `openRoomTtl`. The town's background checks are budgeted (`maxScansPerTick`, retried next interval)
  and one home per check is re-scanned regardless. Probe `house.scans` counts flood fills.
* **Town conditions** (`ConditionCompiler.Town.cs`): `rescued {id}` (world flag `rescued_<id>`, `TownRescue`),
  `hasItem {category}` (an `ItemCategory`, item-count key `@<category>`), `bestiary {min,max}`, `killed {enemy,min}`,
  `obtained {item}`, `achieved {id}` (read `ConditionContext.Records`, an `IPlayerRecords` the journal supplies
  through `TownSystem.PlayerRecords`; null reads 0/false) and `anglerQuests {min,max}`. Seasons (Santa) use the
  World slice's `season` condition and `SeasonState`.
* **Housing API** (`TownSystem.Housing.cs`): `QueryHousing(cell)` (room, suitable, Terraria message, occupant),
  `AssignHome(npc, cell)` (Assigned / Swapped: the occupant takes the npc's old home or becomes homeless /
  NotSuitable / NotAResident for landmark keepers and bound NPCs / AlreadyHome) and `Evict(npc)`; valid homes are
  kept until the room breaks. `TownNpc.BannerCell` marks the banner spot (found again after load). Console:
  `housing query|assign|evict`; probe `town.<id>.homed`.
* **Town data** lives in `data/town/pre_hardmode.json`, `hardmode.json` and `visitors.json`, one NPC per line, with
  Terraria 1.4.4 move-in rules: rescued NPCs (Angler, Golfer, Stylist, Goblin Tinkerer, Mechanic, Tavernkeep, Wizard,
  Tax Collector) need `rescued`; Demolitionist and Nurse need the Merchant; the Dryad any boss; the Zoologist 10%
  bestiary; the Dye Trader a dye and 4 residents; the Party Girl 14 residents and a 2.5% roll (drawn from the town
  stream); Santa the Frost Legion and Christmas (he leaves after); the Princess 3 residents. Visiting merchants are
  inert (`flag never`) until visits exist.
* **Services** (P5-13): `TownNpcDef.Services` (plus the legacy `service`, merged in `AllServices`/`FindService`).
  Shop entries have a `tab` (default "shop"; `TownCommerce.Tabs/Stock/Buy` take it) and an optional `currency`
  item paid by count, unscaled by happiness. The Nurse's heal is base × the first set progression tier (Golem 200 …
  Eye 3) × the Expert multiplier × happiness. The Dryad's `worldStatus` reads `WorldStatus`, an incremental biome
  sampler (256 samples per tick, approximate, fresh for an hour); `town status <id>` forces it. The chat buttons for
  `services` arrive with the W4 TownDialog.
* **Happiness** (P5-12, `TownHappiness`): Terraria 1.4 multipliers (love 0.88, like 0.94, dislike 1.06, hate 1.12)
  for the home's biome or the underground layer (`mood.layer`) and for neighbours within 25 tiles horizontally
  (vertical cap); the Princess (`lovedByAll`) counts as loved; more than 3 near neighbours +5% each; solitude (≤2 near,
  ≤4 within 120 tiles) 0.95; homeless 1.1; clamped to the price range. `TownCommerce.PriceFactor/Happiness/
  DescribeHappiness`; lines from `town.json` `happiness.lines` or per-NPC `happinessLines`. Mood tables follow
  Terraria's happiness table. The chat's Happiness button arrives with the W4 TownDialog.
* **Rescues** (P5-06a, `TownSystem.Rescues.cs`, `SiteFinder`): an NPC with a `rescue` block appears `Bound` once its
  `requires` hold and its flag `rescued_<id>` is unset, at an unseen standable node in a loaded chunk where `where`
  holds (rare zones are sampled inside plan features carrying them; otherwise a 30–70 m shell around the player) or
  at its planned `landmark`. Bound NPCs do not count, cannot be hurt, only fall, and show the rescue title. Talking
  (`BeginTalk`) frees them (`requiresItem` is used up: the Tortured Soul's Purification Powder); the next move-in
  check houses them. Console: `town bind|rescue <id>`; probe `town.<id>.bound`; smoke `rescue.cmds`.
* **Rescue sites** (P5-06b): the Dungeon plans landmark `dungeon_mechanic` at `DungeonLayout.MechanicCell` (the
  `RoomKind.Mechanic` room); the Mechanic's rescue uses it once Skeletron is down and waits there even in plain view
  (a reserved site). The other rescues search: Goblin and Wizard in the caverns, Stylist in spider nests (sampled
  inside the nest features), Golfer in the underground desert, Angler on the ocean surface, Tavernkeep on the
  surface, the Tortured Soul in the underworld. A `rescued` condition on an NPC without a rescue block is an error.
* **Residents as targets** (P5-09, `NpcSystem.Targeting.cs`): `TownNpc` is an `ICombatTarget`. Ordinary hostile
  creatures go after the nearest counting resident within `residentAggroRange` (14 m) when it is `playerPreference`
  (4 m) nearer than the nearest player, re-chosen every 15 ticks; bosses, parts, critters, passive, harmless,
  invulnerable and disguised creatures stay on players, and bound NPCs are never targets. Despawn, enrage and contact
  damage still follow the nearest player; residents take contact hits through `TownSystem`.
* **Postures** (P5-07): `TownNpc.Posture` (`ResidentPosture` Stand, Sit, Sleep, Bound, Lie) picks the human rest pose
  (`TownRenderer.Clip`: sit, sleep, bound, lie; Stand falls through to walk/jump/idle). A bound NPC takes it from its
  rescue pose (`TownNpc.BoundPosture`: bound/webbed/tortured kneel tied, sleeping/unconscious lie) on bind and load,
  and stands once freed.
* **Rescue presentation** (P5-08): the name tag follows `TownNpc.Title` ("Bound Goblin" until freed; refreshed when
  the bound flag flips, `TownRenderer.Props.cs`), a webbed NPC (the Stylist) hangs in a translucent cocoon and the
  Tortured Soul is tinted ghost-blue without a shadow. Freeing raises `TownSystem.Rescued` (sound `npc_arrive`) and
  posts "<Title> has been rescued!" through `NpcSystem.Broadcast` with `MessageKind.Town` (Terraria's arrival blue),
  the same path boss messages take to the HUD and chat log. `town pose <id> <posture>` and probe
  `town.<id>.posture` serve tours and smokes. The talk-to-free dialog page (thanks / purify) waits for the W4 chat box.
* **Residents fight back** (P5-10, `TownSystem.Defense.cs`): `TownNpcDef.Attack` (`TownAttack`: style throw, shoot,
  magic, melee, heal or ward; projectile, damage ×`hardmodeMultiplier` in hardmode, speed, range, interval, count,
  spread, buff). Every 6 ticks (staggered by id) a free resident picks the nearest live hostile creature within range
  that `NpcSystem.InSight` confirms (critters, passive, harmless, invulnerable and disguised ones ignored), faces it
  and pauses 20 ticks while it attacks: shots are friendly, ownerless projectiles (`Ballistics.Aim` lobs
  gravity-bound ones, shared with creature ranged attacks) that only hit creatures; melee strikes within 1.8 m; the
  Nurse heals the most hurt resident in range (no threat needed; players pay her as in Terraria); the Dryad gives players `dryads_ward`
  and residents `WardDefense`. Residents without an attack flee threats within 5 m. The validator requires attack
  projectiles to be friendly with no player- or terrain-harming blast (`data/town/projectiles.json` holds the npc_*
  shots). Dying clears the fight state. `ResidentAttacked` drives the action clip (throw, bow/gun, tome, hold_up,
  swing) and sound; probe `town.<id>.attacks`; `town defense on|off` (`DefenseEnabled`, off in unit tests).
* **Seating and night routine** (P5-11, `TownSystem.Seating.cs`): at night a housed resident who reached home takes
  a free `chair` of the room (from `BedHour` 22:00 a free `bed`, leaving the chair for it) and by day an idle resident
  at home sits a while with `DaySitChance` 0.3. They walk to the nearest node beside the seat, then are snapped to
  `SeatPoint` (footprint centre at `TileObjectDef.SeatHeight`, or `BedHeight` for beds, in cells; the rest poses put
  the model origin on the floor or mattress) facing out of it (`SeatYaw`: objects face their placer). Posture Sit or
  Sleep; no errands or steering while seated. They get up at dawn plus a personal delay of up to an hour, when talked
  to, when a threat comes into sight, when the seat disappears, or when they die. Residents never share a seat
  (`Taken`). Probe `town.<id>.sitting`.


## Journal feeds town conditions (P5-25/P5-03)

`JournalSystem` sets `TownSystem.PlayerRecords` to the player's journal (`PlayerJournal` also implements
`Conditions.IPlayerRecords`), so the town's `bestiary`, `killed`, `obtained` and `achieved` conditions (the
Zoologist's 10% bestiary arrival) read it. `Town.Rescued` counts `rescued` and `rescued:<resident>` in the rescuer's
journal; achievement/guide trigger `rescued{resident?|min}` reads them.

## Book tabs: progress, NPCs, tips, achievements (P5-32)

`ListTab<T>` (list left, page right, icons a few per frame) backs four tabs: `ProgressTab` (the advisor's steps by
stage, ✓ done / ▶ next in gold / grey not yet; boss portraits; related item and enemy links), `NpcsTab` (every
`TownNpcDef`: status Living here / Homeless / Dead until dawn / Bound somewhere / Not yet, arrival text or the
rescue's pose, `Where` and `Requires` through `ConditionDescriber`, loves/likes/dislikes/hates from `Mood`, current
happiness via `TownCommerce.DescribeHappiness`, services, the whole shop with prices, tabs and conditions),
`TipsTab` (by topic, `{key:}` and the action resolved from the live InputMap) and `AchievementsTab` (gold with
date when unlocked, else a progress bar from `AchievementTracker.Progress`). `OpenEntry` routes guide steps,
tips, achievements, residents, enemies and items to their tab; `ui.book_tab` is the tab index (0 items … 5
achievements). Not yet: the screenshot-tour line (main-only lavapipe tour).

## Angler quest log, milestones and shop (P5-23)

`PlayerState.AnglerQuests` (saved as `PlayerSave.AnglerQuests`, additive) counts a character's completed quests;
`TownServices.TurnInFish` increments it, scales the coins by `coinGrowth` per quest (to `coinQuestCap`), gives every
`AnglerReward` with `everyQuests` on its multiples (from `minQuests`: Crate Potions every 5th, the Golden Fishing Rod
every 30th) and then rolls the chance rewards allowed by `minQuests`, and raises `AnglerQuestDone` (journal counter
`angler_quests`: the reference game's five Angler achievements). `TownContext` fills `AnglerQuests` from the player,
so the Angler's shop sells the fishing decor of `data/tileobjects/angler_decor.json` (seashells, treasure map,
seaweed planter, lifebuoy, ship's wheel, wall anchor, bass/trout/tuna plaques; `bgen/angler_decor.py`) by
`anglerQuests` conditions. Not yet: the quest count in the dialog's quest text and its Shop button (TownDialog, W4).

## Resident shops (P5-15..P5-18)

Terraria 1.4.4 stock lives in each NPC's `shop` (data/town/*.json, one NPC per line), with conditions on time, flags
(hardmode, bosses), the world's evil, the biome at the resident's home, moon, season and bestiary; prices are the
item value unless given, scaled by happiness (currency prices are not). New goods are in
`data/items/town_shop_*.json` and `data/tileobjects/town_decor*.json`; their models come from reused generators and
`tools/blender/bgen/town_shop.py` (specs in `tools/blender/assets.d/p5-1*-shops.json`).
* **Shops I** (P5-15): Merchant (mining clothes, anvil, shuriken, Glowsticks at night), Dryad (powders by evil, grass
  seeds; jungle/mushroom seeds by home biome; corrupt/crimson/hallowed seeds in hardmode; Sunflower; Herb Bag), Arms
  Dealer (Illegal Gun Parts at night, Empty Bullets in hardmode), Clothier (tuxedo), Goblin (Spiky Ball), Witch
  Doctor (Bewitching Table at night). The Nurse has no shop. Behavior `plantGrass {grass, on}`
  (`ItemUseSystem.Garden.cs`): the aimed block, if it is in `on` and uncovered, becomes `grass`; one seed is used.
  The Sunflower's aura gives Happy!, the Bewitching Table's Bewitched (+1 minion).
* **Use styles**: items may name Terraria's 1.4.4 use styles (`gun`, `throw`, `drink`, `holdUp`…); until the use-style
  rework lands on this branch `UseStyles.Family` maps each to the legacy style it behaves as.
* **Shops II** (P5-16): the Painter's Decor tab sells seven wall paintings (`painting` generator; The Truth Is Up There
  after the Martians); the Dye Trader the Dye Vat (a station without recipes yet) and ten basic dyes (category dye,
  cosmetic until dye slots exist; they satisfy his arrival rule); the Party Girl Confetti, the Confetti Gun (both a
  harmless glowing puff), the Party Hat and a decor Bubble Machine (no bubbles yet); the Tavernkeep Ale (Tipsy) for a
  Defender Medal (dropped by the Old One's Army: 50% per soldier, 5/10/20 from Dark Mage/Ogre/Betsy) and a decor
  Eternia Crystal Stand (the crystal item still starts the event). `town buy` finds the tab that lists the item.
* **Shops IV** (P5-18): Wizard (Crystal Ball, an aura giving Clairvoyance; Magic Hat), Steampunker (Cog; wings after
  Golem), Cyborg (Rocket II in a Blood Moon, digs; Rocket III at night or in an eclipse, wider blast; Nanites as a
  material), Truffle (Mushroom Cap; Mushroom Spear and Hammush after a mechanical boss), Pirate outfit, Santa's whole
  stock only at Christmas (plus a Star Topper), Princess outfit and Resplendent Dessert (Sugar Rush). Deferred: the
  Pirate's Cannon (needs an activatable launcher tile), Steampunker solutions and Clentaminator (no conversion
  spray), sentry staves and Defender armour (no sentries yet), the Stylist's hair dyes.
* **Shops III** (P5-17): the Zoologist's stock grows with the bestiary (`bestiary {min}`: Leather Whip at 10%, Critter
  Shampoo at 30%, Fairy Glowstick, a floating light, at 45%; the pet licenses wait for P5-20). The Golfer sells four
  clubs (swung; each knocks a Golf Ball, ammo class `golf_ball`, off along the aim at its own speed: driver fastest,
  putter slowest), the bouncing `golf_ball_shot` (6 bounces, light damage), Golf Cup and Tee decor and the golf
  outfit. Golf scoring is not modelled.

## Visiting merchants (P5-19)

`TownNpcDef.Visit` (`VisitInfo`) makes a town NPC a `TownNpcKind.Visitor`: never counted, never housed, no happiness
(`TownNpc.NeedsHome` is false). `TownSystem.Visitors.cs` runs every `VisitInterval` ticks. Town visitors (the Travelling
Merchant, `data/town/visitors.json`) roll `dawnChance` once a day with Pcg32(seed, day, id) once `visit.requires` holds
(2+ housed residents), appear 20–30 m out from the town centre (the mean of the homes), walk in and stroll there
(`TownNpc.Anchor`), and at `leaveHour` walk out and are removed beyond 40 m (or after a timeout while unseen).
Site visitors (the Skeleton Merchant) roll `hourlyChance` per check while the player is where `visit.where` holds, are
placed out of sight with `SiteFinder`, and vanish after the player has been 100 m away for a minute or at the next dawn.
Arrival and departure raise `Arrived`/`Departed` (HUD announcements) plus `VisitorArrived`/`VisitorLeft`.
`TownCommerce.VisitorStock` adds each `visit.pools[]` entry draw (Pcg32(seed, visit day, id)), stable per visit;
`TownNpcDef.AllShopEntries` feeds item sources and validation. `ResidentModel.VisitDay` persists the visit.
Commands: `town visit <id>`, `town leave <id>`. Gaps: no charged arrows for the Pulse Bow, no Bone Torch, the stock
pools are a functional subset of Terraria's list.

## Town pets (P5-20)

`TownNpcDef.Pet` makes a `TownNpcKind.Pet` (`data/town/pets.json`: town_cat, town_dog, town_bunny). Pets arrive like
residents (a free house, arrival `[{flag license_<pet>}]`), keep and are re-housed into homes (`TownNpc.NeedsHome`), can
be assigned in the housing UI, but never count as residents (so they are never targeted), cannot be hurt and have no
shop or happiness. The `license` item behaviour (`ItemUseSystem.Town.cs`) sets the flag and is used up; a second use is
refused ("This license has already been used.") and kept. The Zoologist sells the licenses (bestiary 25 % / 25 % /
45 %). Interacting with a pet calls `TownSystem.PetAnimal` instead of opening the dialog (InteractionController); the
`Petted` event drives `game/Entities/TownPetHearts` (pooled heart sprites, sfx `pet`). Models from
`tools/blender/bgen/town_pets.py` (one quadruped rig, walk/idle/sit/lie). Gaps: no player petting arm pose; pets use
the humanoid nav agent and box.

## Biome pylons (P5-34)

Eight pylon tile objects (`data/tileobjects/pylons.json`, tag `pylon`, `bgen/pylons.py`). `TownSystem.Pylons`
(`PylonNetwork`, rules in `data/config/town.json` "pylons", defaults in `PylonConfig`) decides: a housed resident whose
happiness price factor is at most `maxFactor` (content or better) and who has another resident within
`neighbourRadius` sells the pylon of their home's biome (rules in order, specific biomes before the forest; the Cavern
Pylon anywhere below the surface) for 10 gold scaled by happiness, appended by `TownCommerce.Stock`. A pylon works
with `minResidents` residents within `activeRadius`; `Teleport(player, pylon)` needs a working pylon within
`useRadius` of the player and a working destination, and puts the player on a standable node beside it. The world
map marks pylons (`game/UI/PylonMap.cs`) and a click on one travels there; commands `town pylons`, `town pylon <n>`;
probes `town.pylons`, `town.pylon_teleports`. Item sources list pylons as "Sold by content residents living in its
biome". Gaps: the one-of-each-type placement limit and Terraria's "no bosses or invasions nearby" rule are not
enforced; no Universal Pylon.

## Emotes and NPC voices (X-05)

`TownSystem.Emotes.cs` checks `residentsPerCheck` residents every `interval` ticks (config `data/config/town.json`
"emotes", `EmoteConfig`); a ready one emotes with `chance`, then cools down. `EmoteRules.Pick` (pure, tested) maps
`EmoteInput` (happiness factor, sleeping at home at night, danger/party/rain events, loved or hated neighbours within
`replyRadius`, a chat partner within `chatRadius`, talking to the player, pets) to an emote id; a "chat" partner
answers with `EmoteRules.Reply`. `PlayerEmote` (the wheel) raises `PlayerEmoted` and nearby ready residents reply;
petting makes the pet show "love". Game side: `game/Entities/TownEmoteBubbles` (pooled billboard sprites, 4 on the low
preset, 12 otherwise, icons `assets/icons/emotes/*.png` from `tools/textures/build_emotes.py`),
`game/Audio/NpcVoice` (voice blips `voice_*` from `tools/audio/sfx_recipes_town.py`, per-NPC sound and pitch in
"emotes.voices", played on `InteractionController.TalkRequested` and softly on chat bubbles), and
`game/UI/EmoteWheel` (hold T, move the mouse, release). The emote wheel takes T; block-shape cycling moved to Y.
Probe `town.emotes_shown`; command `town emote <emote> [id]`.

## Town services and shop tabs in the W4 chat box and grid shop

`TownDialog.Services.cs` shows one button per entry of `TownNpcDef.AllServices` (Heal, Crafting, Help = the advisor's
next step, summon, Reforge, Taxes, Quest with the player's count, Status) and a Shop button plus one per further tab
(the Painter's Decor); residents with only visitor pools or a pylon for sale still get Shop. A tab button sets
`UiContext.ShopTab`, which `ShopPanel.Open` reads (and resets), so the grid shop lists and buys from that tab;
currency wares (Defender Medals) show their cost in the tooltip and `TownCommerce.Buy` takes the medals. Commands
`ui service <type>`, `ui shop [tab]`.

## The Stylist and hair dyes (P5-21)

`TownServices.Haircut(npc, player, style, colorHex)` (the `haircut` service, `HaircutParams` price per new style
plus a colour price, times the happiness price factor) validates the style against `Appearance.StylesFor` and the
hex colour, charges like the other services and raises `PlayerState.LookChanged`. Hair dyes are items with the
`hairDye` behaviour (`HairDyeParams.Mode`: static, life, mana, depth, time, money, speed, rainbow, remove); using one
sets `Appearance.HairDye` (saved with the look) and `HairDyes.ColorFor` computes the colour as a pure function of
life, mana, world layer, hour, coins, speed and tick. `PlayerAvatar.Look` re-applies the look on `LookChanged` (the
hair model swaps in place) and re-tints a dynamic dye twice a second. `HaircutPanel` (preview, styles, swatches,
price, Buy) is opened through `TownPanels`; console `ui haircut <id>`, `town haircut <id> <style> <#rrggbb>`.

## Strange Plants and the Dye Trader (P5-22)

`DecorationPass` grows four natural Strange Plants (`data/tileobjects/strange_plants.json`) on open grass tops (surface
turf and jungle turf, also underground) at `DecorPalette.StrangePlantChance` (1/2000) from their own seed
(`strange_plants`), placed after every other plant so the rest of the decoration is unchanged (GenVersion 13; the
golden hashes differ only where a plant grew). Each drops its `strange_plant_N` item (tag `strange_plant`). The Dye
Trader's `dyeTrade` service (`DyeTradeParams`: accepted tag, loot table `dye_trader_trade`) takes one plant from the
backpack for a weighted roll of nine rare dyes (`TownServices.TradeStrangePlant`); the chat box shows 'Trade' only while
a plant is carried; console `town trade <id>`.

## Housing menu (P5-29)

`HousingPanel` (game/UI) lists a portrait banner per resident (`PortraitIcons.TownNpc`, preloaded on open and pumped
per frame; greyed while dead or homeless). '?' then a click on a room runs `TownSystem.QueryHousing` and shows the
message in Terraria's yellow; a banner then a room runs `AssignHome` (swapping with the occupant); right-clicking a
banner evicts. World clicks are taken by a full-screen catcher while picking and cast through `WorldPicker` (camera
ray → `Targeting.Cast` → the open cell in front). `HousingBannerRenderer` hangs pooled head sprites at each housed
resident's `BannerCell` while the menu is open. Opened by the equipment column's Housing tab (beside it), the Housing
key (unbound by default: H is Terraria's quick heal) or `ui housing`; `ui housing_query dx dy dz`; probes `ui.housing_open`, `ui.housing_banners`,
`ui.housing_room_banners`.
