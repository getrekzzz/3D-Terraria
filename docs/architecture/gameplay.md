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
  slowed in towns (tuning: `data/config/spawning.json`). Rules may cap how many are alive (`maxAlive`) or judge
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
  `camera.framing`, `time.scale`, `music.track.<id>`, `music.stings`. `MusicDirector.Stung` reports stings (captions).
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
