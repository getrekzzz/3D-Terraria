# World & life slice — status (`claude/ovh-world`)

Owner: World & life session. Order: X-01 → X-08 (+P3-49) → X-06 → X-13 → X-15 → X-14 → X-03 → X-07.

## Push 1 — weather and ambience
| WP | State | Notes |
|---|---|---|
| X-01 weather | done | Core `Gameplay/Weather` (deterministic hourly rolls per seed, rain/thunderstorm/windy/sandstorm, snow and blizzards over snow biomes, nothing underground), `weather` condition + `ConditionContext.Weather/Windy`, mirrored `phase: "weather"` events (rain/thunderstorm/windy_day/sandstorm), saved in `WorldMeta.Weather`. Engine: GPU rain/snow/sand/splash particles in a camera box cut off by the sky map (`SkyHeights` texture), per-preset budgets in `data/config/weather.json`, terrain wetness + puddles, overcast sky, wind on clouds/grass/foliage/flames, sandstorm/blizzard haze, lightning + thunder, weather loops on the new `Outdoors` bus. |
| X-08 ambience (+P3-49) | done | `data/config/ambience.json` + Core `AmbienceConfig.Select` (tested): 16 synthesised beds (birds, crickets, frogs, surf, wind, caves, cavern, lava, mushroom, evil, hallow, dungeon, sky, hive); cave reverb and outdoor low-pass by enclosure/depth (`AudioEnvironment`); pooled GPU dust/spores/embers/leaves (from real nearby tree canopies)/fireflies per preset; cave drips from real ceilings; music variants (rain, town day/night, night versions, underground jungle/ice/blight/rot/radiant, space); fishing bite sound. |

CI (push 1): GREEN — file length, build, 426 Core tests, ore tiers/manifest/validate/coverage/gen_reference/build_all/audio checks, Godot import, all 15 smoke scripts (incl. new weather.cmds) SMOKE PASS, menu tour MENU PASS.

## Push 2 — fishing, wiring, seasons (+ review fixes)
| WP | State | Notes |
|---|---|---|
| X-13 fishing depth | done | Fishing power (rod + bait + `fishingPower` gear) × rain/storm/time/moon/Blood Moon × pool size (`data/config/fishing.json`); bait consumed by Terraria's chance; 10 biome crates + tier tables; lava fishing via Hotline rod / Lavaproof hook; 22 quest fish (water-only quests need water); bug-net critters are bait; `fishing.cmds` smoke. |
| X-15 wiring set | done | Red/blue/green/yellow wire networks, actuators, 1/3/5 s timers, teleporter pads, mob/bomb statues with caps, logic gates, per-colour wire view; additive save fields (old red-only saves load). Adversarially reviewed: fixes for stale timers after unloads, mid-tick switch-off, statue critters (no netting, no event kills), actuator removal in unloaded chunks. |
| X-06 seasons | done | Halloween (Oct 10–Nov 1) / Christmas (Dec 15–31) from the system date (`data/config/seasons.json`), settings toggle, `season` command/probe/condition; goodie bags and presents, seasonal vanity and decor, NPC hats, Santa at Christmas. Scripted runs (`--smoke`) ignore the calendar unless a script sets `season`. |

CI (push 2, merge): GREEN — 467 Core tests, all 16 smoke scripts (incl. new fishing.cmds) and menu tour pass. Review-fix commit 76f92be: GREEN (470 Core tests, 16 smoke scripts, menu tour).

Gaps: no junk catches or hardmode crates; statues are crafted (no worldgen placement); lamp models do not show on/off;
mining a block does not drop its actuator; no season-only enemies; seasonal vanity not drawn on the avatar yet.

## Push 3 — mounts (X-14, slot hook-up pending) — CI GREEN (478 Core tests, 17 smoke scripts incl. mounts.cmds, menu tour)
| WP | State | Notes |
|---|---|---|
| X-14 mounts | done except the P2-05 slot | Slimy Saddle (King Slime 25%, high bouncy jump, stomps), Fuzzy Carrot (Goodie Bags, fast bunny), Honeyed Goggles (Queen Bee 5%, 2.6 s flight), Cosmic Car Key (Martian Saucer 10%, free hovering flight); mount buffs; `quick_mount` key (G — R is rotate shape); Core `MountMotion`/`MountRules` + `MountTests`; `PlayerController.Mounts.cs`, `PlayerAvatar.Mount.cs` (existing creature models scaled up); item models/icons (`bgen/mount_items.py`); new smoke `mounts.cmds`. Boss drops via new `extra_<enemy>` loot tables, so the bosses session's loot files stay untouched. |

Buff bar: mount buffs are `ActiveBuff.Unlimited` (TicksLeft ≥ `BuffSet.Unlimited`); the rebuilt buff bar (P2-14) should
show no timer for them (the current Hud prints a huge seconds count).

Pending: the equipment mount slot (P2-05 `EquippedMount`) is not on the integration branch yet; when it lands, set
`MountRules.SlotMount = p => <equipped mount item>` (one line) so the quick-mount key prefers it. The rider stands in the
saddle (no sit clip: the human animation sets belong to the integration session).

## Push 4 — furniture families (X-03 part 1) — CI GREEN (481 Core tests, 17 smoke scripts, menu tour)
| WP | State | Notes |
|---|---|---|
| X-03 furniture families | part 1 done | 14 materials (wood, boreal, palm, rich mahogany, ebonwood, shadewood, cactus, mushroom, granite, marble, obsidian, glass, bone, lihzahrd) × 17 pieces (door, chair, table, bed, chest, work bench, bookcase, piano, sofa, bathtub, lamp, lantern, candle, candelabra, chandelier, grandfather clock, platform) from `tools/content/furniture_families.json` via `gen_furniture.py` (generated `data/items/generated/furniture_families.json` + `tools/blender/assets.d/x-03-families.json`); parametric softened-edge models (`bgen/furniture_family.py`), recipes (work bench / sawmill / anvil / hellforge), housing roles, lights and flames; `FurnitureFamilyTests`. |

X-03 part 2 (after the bosses session's W8 push and P2-08): enemy banners (kill tally → banner drop, placed-banner
damage bonus hooked into the reworked combat), editable signs (object meta), and reuse of the dungeon furniture /
banner / statue helpers (P4-17/18) — the family bookcase and models may switch to those helpers then. Living Wood is
left out (no Living Wood item yet; it needs the Living Loom).

## Push 5 — game-feel chain, part 1 (handed over by the integration session): P3-08, P3-19, P3-20
| WP | State | Notes |
|---|---|---|
| P3-08 EffectKit | done | Pooled voxel-lit bursts (chips, dust, sparks, puff, splash, leaves; `particle_lit.gdshader`), budget (effectsScale × count, 8 bursts/frame, ~600 live), `FlashLights` pool (a separate pool, not a DynamicLights provider: those re-rank at 4 Hz and cannot fade a flash), graphics.json `effectsScale`/`flashLights`; mining, steam, burning items, explosions migrated; SoundEvents split into World/Weapons/Creatures/Player partials. |
| P3-19 creature materials | done | `creature_materials.json` + `CreatureMaterialsConfig` (material from the enemy, its model or tags — no enemy files edited); `DamageEvent.NpcId/Kind`, `NpcSystem.Find`; material hit/death sounds (11 new) and hit dust; periodic numbers smaller. |
| P3-20 gore | done | `GoreSystem` (pooled MultiMesh chunks, voxel bounce, lit, fading); death dust burst + gore; `juice.cmds` smoke. |

CI (push 5): GREEN except one mawwall.cmds run (its fills ran while the underworld teleport was still settling: "Filled 0
cells"); it passed on the re-run — a pre-existing timing weakness in that script (bosses session), not these WPs.

## Push 6 — game-feel chain, part 2: P3-21, P3-22, P3-24, P3-26, P3-25
| WP | State | Notes |
|---|---|---|
| P3-21 object hits | done | `WorldEditor.ObjectHit`; per-object hit sounds, dust colour, leaves and `topple` in tile-object data (validated); pots shatter; chips/leaves on every hit; crack progress on objects. |
| P3-22 falling trees | done | `TreeEffects`: the tree instance hides while a pooled copy wobbles ±3° per chop; a felled tree topples away from the player (0.9 s), crashes with leaves, chips, `tree_fall` and a small camera kick, then sinks. |
| P3-24 mining hits and cracks | done | Every block has 100 health; `BlockDef.pickDamageScale` (soft tiles 2×, evil stone/hellstone/dungeon brick ½, hardmode ores ⅓–⅕) — copper: dirt 2 hits, stone 3; `hardness` now only flavours sounds/particles. `BlockCracks`: ≤ 64 depth-reconstructed box decals (`block_crack.gdshader`, atlas from `tools/textures/gen_fx.py`, set `fx`) on every damaged block within 24 m, staged at 0/50/75/90%, fading over the last second; the cursor keeps only its colour ramp. Tink already existed. `MiningHitsTests`. |
| P3-26 movement dust and splashes | done | `PlayerController` events (`Landed`, `Jumped`, `Footstep`, `LiquidEntered/Exited`) and `GroundBlock()`; `PlayerDust`: landing chips in the ground colour, jump/dash/sprint puffs, splash rings + `splash` by plunge speed. |
| P3-25 hurt and impact | done | `GameWorld.HurtPlayerDirect` (debuff ticks report `Periodic`; fall damage goes through Core — `FallDamaged` removed); quieter periodic hurt sound (≤ 1 per 0.8 s); `HurtOverlay` red vignette (`HurtFlash` setting; Camera shake toggle added to Settings too); `CameraShake.Punch` with roll: boss spawn 0.45, defeat 0.7, hits 0.1–0.35, landings over 14 m/s 0.15; player knockback away·(4 + kb·0.6) + 5.5 up (projectiles 3.5/5), scaled by 1 − knockback resistance; grounded creatures hop 2.5–8 m/s (never stacked by several hits in one tick; crits already knock ×1.4 in `DamageModel`); `jump` sound only on real jumps; creatures swell 12% and fade their red tint when hit. `HurtFeelTests`. |

Adversarially reviewed (P3-24/P3-25): fixes for doubled crit knockback, stacking hops, unvalidated `pickDamageScale`,
knockback resistance, drowning via `HurtPlayerDirect`, far cracks appearing as you approach and the newest cracks first
past the cap. Known: crack decals draw over water/glass in front of a damaged block.

CI (push 6): GREEN — 552 Core tests, content checks, Godot import, all 18 smoke scripts, menu tour.

New probes: `effects.cracks`, `effects.tree_falls`, `player.splashes`, `camera.punches`, `hud.hurt_flashes`. Smoke: `juice.cmds` (tree chop,
cracks), `liquids.cmds` (splash), `movement.cmds` (fall damage + hurt flash). Tours: `Tours/juice.cmds` (cracks),
`Tours/feel.cmds` (tree fall, hurt flash).

Notes: P2-19 (combat text) is not on the integration branch yet, so fall/debuff numbers use the existing
`DamageEvent` path (`Kind` = `Player`/`Periodic`); route them through P2-19 when it lands. `CameraShake.Punch` is the
API for X-02/P4-03 (boss spawn/defeat already punch from `SoundEvents.Creatures`). Crack decals need Forward+ (the
project renderer on every preset).

## Push 7 — bosses W8/W9 merged; enemy banners (X-03 part 2a)
| WP | State | Notes |
|---|---|---|
| merge `origin/claude/ovh-bosses` | done | Contact damage lives in `NpcSystem.Contact.cs` (P3-25 knockback and damage kinds ported); segment hits report the creature id; planned Dungeon wires go on the red `WireGrid` layer. One `skeleton_statue` (the wireable statue, 2×4×2, also placed by the Dungeon decorator) with the Dungeon's statue model; the wiring statues' Blender generator is renamed `critter_statue` (generator names must be unique). |
| X-03 enemy banners | done | 162 banners from `tools/content/banners.json` via `gen_banners.py` (one per coin-worthy enemy; slimes, zombies and Angry Bones grouped), models from the Dungeon's `banner` generator; `BannerSystem`: world kill tallies, a banner every 50 kills, ×1.5 damage dealt / ×0.9 contact damage taken near a placed banner, Monster Banner buff; `BannerTests`, `builder.cmds` section, probes `banners.near|dropped`. |
| X-03 editable signs | done (push 9) | See push 9. |

CI (push 7): GREEN — 1123 Core tests, content checks (incl. furniture/banner generators), Godot import, all 18 smoke
scripts, menu tour.

## Push 8 — W3 merged (X-14 slot, P2-19 numbers); secret seeds (X-07)
| WP | State | Notes |
|---|---|---|
| merge `origin/claude/clever-goodall-kgex45` (W3 + Bosses/Dungeon) | done | `DamageEvent` keeps `NpcId`/`Kind` and gains `Player`; hurt, fall and debuff numbers go through P2-19 (periodic ticks at 75% size); `CombatEffects` = P2-19 numbers + EffectKit dust/gore; tree wobble works on the grouped object renderer; `WireGrid` saves through `RefreshMeta`. Item-source index learns tiered fishing tables, `extra_<enemy>`, `enemy_global_drops` and banner drops; an object named like its station names it alone ("Work Bench"). Buff icons for fishing/crate/mount/banner buffs, thunderstorm event icon, worn models for the seasonal vanity. |
| X-14 mount slot | done | Quick-mount prefers `EquippedMount` (P2-05); mount items carry `equipSlot: mount`; the `MountRules.SlotMount` hook is gone. |
| X-07 secret seeds | done (Don't Dig Up: name only) | `SecretSeed` flags from Terraria's names (saved as `WorldMeta.Secrets`); GenVersion 12, ordinary seeds unchanged (golden hashes hold). Drunk World: both evils and both altars; Not the Bees: 1.5× jungle, 4× hives; No Traps: 6× Dungeon dart traps; For the Worthy: one difficulty step harder (`EffectiveDifficulty`); The Constant: dimmer sky light; Celebration: Party without the Party Girl (`secretSeed` condition); found seeds listed under the menu's seed field; `--secret=` for dev starts and `worldgen-report`. `SecretSeedTests`. Don't Dig Up's upside-down world is not generated (it would need its own generator pass set). |

X-07 adversarially reviewed: fixes for the found-seeds hint not persisting (UserSettings saves strings only), For the
Worthy's Demon Heart and extra accessory slot (now `EffectiveDifficulty`), the plan hash (includes generation-shaping
flags), and unknown `secretSeed` ids (validated). Known: a Drunk World's hardmode stripe uses its primary evil only;
composite conditions (`any`/`all`/`not`) do not validate nested specs (pre-existing). The GenVersion 11 → 12 bump
(asked by integration) marks pre-12 worlds "old terrain" though ordinary seeds generate identically.

CI (push 8, merge commit): GREEN — 1310 Core tests, content checks, Godot import, all 20 smoke scripts, menu tour.
Review-fix commit: 1313 Core tests, validate, basic smoke and menu tour pass.

## Push 9 — editable signs (X-03 done)
| WP | State | Notes |
|---|---|---|
| X-03 signs | done | Sign (6 wood, work bench; `signpost` model) with `interaction: read`; `SignText` (6 lines / 240 chars) saved as P2-08 object-meta tag 3; `SignDialog` to edit, the target label shows the text; `sign` command, `signs.text` probe, `SignTests`, `builder.cmds` section. |

## Push 9 (cont.) — ragdolls (X-16)
| WP | State | Notes |
|---|---|---|
| X-16 ragdolls | done | `Ragdoll` (PhysicalBoneSimulator3D, 15 capsules, terrain/object collision only); human creatures ragdoll on death within the preset's `ragdolls` budget (0/3/6/10), thrown by the killing knockback, 3 s then sink; others keep P3-20 gore; the player ragdolls until respawn. `creatures.cmds` and `combat.cmds` assert it (`fx.ragdoll_drop` shows the body really falls); tour `Tours/ragdoll.cmds`. |

CI (push 9, after merging P3-02): GREEN — 1378 Core tests, content checks, Godot import, all smoke scripts, menu tour.

## Weapon and projectile chain (handed over by integration)
| WP | State | Notes |
|---|---|---|
| P3-09' shots leave the weapon | done | Muzzle at the use style's `holdout` (gun, bow, staff, tome, throw; else ShotGeometry's hand) with ShotGeometry's wall fallback; `shootProjectile.from` (`muzzle`/`sky`/`cursor`/`eye`, `skyHeight` 16 m, `skyScatter` 3 m): Starfury, Daedalus Stormbow and Star Wrath rain from the sky onto the crosshair; minions appear at the crosshair (≤ 12 m, 1 m short of surfaces); `ammoSaveChance` (Minishark 0.33, Megashark 0.5, Vortex Beater / Phantasm / S.D.M.G. 0.66; validated); `Projectile.SpawnOrigin` for visuals. Sword+shot, MaxActive and dry fire stay P1-16/P1-15's. `ShotTests`, combat.cmds Starfury section. |
| P3-14 whips (Core) | done | `whip` behaviour (`length` cells, `segments`, `falloff`, `tagBuff`/`tagTicks`, `inflict`) and `ItemAction.Whip`; `WhipGeometry` (Bézier lash: wind-up to 0.35, crack fully extended at 0.6; shared with P3-15's renderer) and `WhipSystem` (each creature once per crack, ×(1 − falloff)^hits, tag buff + `Npc.TaggedBy`); `minionTag` buff effect: minion and minion-shot hits add the tag damage; minions go for their owner's latest tagged creature within 1.5× range. Leather Whip (11 cells, tag 4), Snapthorn (13 cells, tag 6, poison). Whips never repeat while held unless autoReuse (P3-15's autoswing rule). `WhipTests`, probe `whips.hits`, combat.cmds section. No lash is drawn yet (P3-15). |
| P3-23 weapons break pots and grass | done | `breakByAttack` on tile objects (pot, grass tuft, flower, fern, mushroom plants; chairs and other furniture stay put); sword swings smash any such object their blade sweeps through (same arc and stab tests as creature hits) and friendly shots smash what they pass through and fly on, or cut the tuft they land in; hostile shots leave them. `WorldEditor.SmashObject` breaks with drops and loot and raises `ObjectHit` Broken (sound and dust come from the existing break path). `AttackBreaksObjectsTests`, probe `objects.smashed` (command `smashed [reset]`), combat.cmds section. |
| P3-10 shot presentation | done | Use-style flags `muzzleFlash` (gun) and `tipSparks` (staff, tome). For the local player's shots (CombatEffects.Muzzle): a pooled additive star flash (4 billboards, 0.05 s), a 0.06 s flash light, a wisp of smoke, and a camera punch for heavy guns (use time ≥ 30); staves and tomes spark at the tip in the shot's colour; minions pop in with a puff. One flash per tick (shotgun pellets). Effects sit at the held model's `muzzle`/`tip` socket in the active view (new `ViewModel.TrySocket` partial, `HeldSockets`), else where the shot started; sky and cursor shots show none. New shots are drawn leaving that socket and ease onto the simulated path over 0.07 s. First-person recoil stays P3-07's. Probes `fx.muzzle_flashes`, `projectiles.from_muzzle` (combat.cmds asserts both). Review fix: `SpawnOrigin` is set before `Spawned` is raised. |
| P1b-15 projectile rendering | done | Shared glow materials, sphere meshes and the bullet streak. Finished views are pooled per projectile type (32 kept) and reused; creature minions, pets and bobbers are not pooled. No light per projectile: preset `projectileLights` (0/4/8/12) lights go each frame to the glowing shots nearest the camera. Probes `projectiles.nodes_created`/`pooled`/`lights`; combat.cmds asserts ≤ 34 nodes for 40 sparks; perf.cmds `perf.max.gd.projectiles < 6` over a second, warmed volley (measured 0.65 ms, no new nodes; the first-ever shot's model load is ~14 ms and shows in the spike report); GraphicsConfigTests. |
| P3-11 projectile visuals | done | ProjectileDef `spin`/`spinAxis` (tumble, flat, roll) with data for shuriken, boomerangs, Paladin's Hammer, bombs, dynamite and grenades (boomerangs and fused throws not in the data spin by default); `trailWidth`/`trailTime`: ribbon trails (12-point pooled buffers, one shared additive mesh, tapering and fading, outliving the shot, segments capped at 1.5 m); bolts get a ≥ 6 cm core and an additive halo. ProjectileRenderer is split into lifecycle, Visuals, Pool, Lights, Trails and Tethers. Core `ProjectileSystem.HitTerrain` (once per strike): 4 chips of the block's colour, and `arrow_hit` for arrows or the block's dig sound (−6 dB) for anything else (`tools/audio/sfx_recipes_impacts.py`). The fish-bite splash is still sound only (X-13's fish_bite). `ProjectileImpactTests`; probes `projectiles.trails`, `fx.impacts`. Review fixes: a bouncing throw slower than 2 m/s against the ground rests instead of striking (so a lying bomb no longer raises an impact every tick or burns its bounces), and the struck block is looked up along the stopped axis. |
| P3-12 yoyos | done (held-model hiding and the FP arm pose stay with Art) | `tether` on projectiles (`kind` yoyo/flail, `range` cells, `lifetime` ticks with 0 = unlimited, `speed`, `string` colour; validated, no `returns`). `PlayerEntity.UseHeld` is set each physics tick by InteractionController. While held, a yoyo chases the crosshair within range of the eye at up to 60 m/s², slides on terrain instead of ending, and re-strikes every 10 ticks; released, timed out or put away, it reels home. One out at a time (`ProjectileDef.OneAtATime`). Wooden Yoyo 11 cells/3 s, Rally 15/5 s, Amazon 17/7 s, Code 1 19/9 s, Terrarian 35/unlimited; roll spin 25. A coloured string runs from the `string` socket (or the hand). `YoyoTests`, combat.cmds section, probe `projectiles.strings`. While the yoyo is out (and while a flail whirls) the owner's use animation is held open, so both views keep the arm raised; tethered weapons never burst-fire. |
| P3-13 flails | done (Solar Eruption stays a returning projectile) | `tether.kind: flail`. Held, the ball whirls 1.1 m from the hand at 12 rad/s in a tipped plane, re-striking every 12 ticks. Let go, it flies along the aim at the tether speed, drops after 0.3 s, drags on the ground, and is reeled in after 1.5 s, at chain's end, or on a new press. Ball O' Hurt 18 cells, Sunfury 20, Blue Moon 22, Flower Pow 22, Flairon 24. The chain is drawn by the new shared `Rendering.ChainStrip` (also used by GrappleVisual) from the `chain` socket. `FlailTests`, combat.cmds section. |
| P3-15 whips (presentation) + autoswing | done (held-whip hiding stays with Art; the dead-autoReuse warning is skipped because 103 shipped melee items carry the flag) | `WhipRenderer`: a six-sided tube tapering 3 cm → 8 mm along WhipGeometry at sub-tick time, in the whip's `color` (Leather Whip brown, Snapthorn green), from the drawn grip; `whip_swing` as it winds up; sparks and `whip_crack` at the crack (`tools/audio/sfx_recipes_whips.py`). Stat `autoswing`: whips repeat while held only with it; Feral Claws grant it (description updated). WhipTests `FeralClawsLetHoldingRepeatTheWhip`; probes `whips.drawn`/`cracks`; combat.cmds section. |
| P3-17' hook heads and the equipped hook | done | Merged W4 (855d775a). The grapple key already throws the Misc-slot hook first, else the longest-reaching one carried (GrappleHook.Best). GrappleParams gain `head` (the item whose model is the hook head, default the hook item) and `chain` (`links`, `vine`, `gem`; validated). GrappleVisual draws the head model pointing along the throw (the generic claw only when there is no model): metal links for most hooks, a long green vine for the Ivy Whip, and glowing links in the hook's colour around a thin dark core for the six gem hooks. `HookStyleTests`; the weapons tour shows a ruby hook. |
| P3-30 Fallen Stars | done (Core + sound; P3-11 trail styling and the P3-29 mana-star hook stay with their owners) | `StarfallDirector` (data/config/starfall.json: 2.2 %/s per surface player at night ≈ a dozen a night, within 40 m, from up to 45 m, 18 m/s, ≤ 4 at once): the `falling_star` projectile (the Fallen Star item model, warm light and trail, pierces creatures for 25) slants down from loaded, open sky and leaves a Fallen Star where it lands (`dropItem` on-hit spec, `ProjectileSystem.Landed`). `fadesAtDawn` items left on the ground vanish when day breaks (`DroppedItemSystem.RemoveWhere`). Sounds `star_fall` (whoosh within 60 m) and `star_land` (chime), `tools/audio/sfx_recipes_sky.py`. Command `starfall now`; probes `stars.fallen`/`faded`/`active`, `drops.fallen_star`. `StarfallTests`, events.cmds section. |

### Gaps / notes for integration
* The boss-defeat music stinger is left to X-02 (integration owns "victory sting"); `boss_defeated` SFX still plays.
* Props (furniture, trees) do not get wet — only terrain (prop_common is shared with the art session); glass and
  other light-passing blocks do not stop rain (the sky map counts only light-blocking cells).
* One combined commit covers X-01 engine + X-08 (shared audio/particle infrastructure).
* New probes: `weather.*` (kind, local, intensity, wind, windy, sandstorm, particles, budget, wetness, strikes),
  `ambience.loops`, `ambience.particles`, `audio.reverb`, `audio.muffle`; new smoke script `weather.cmds`; tour
  `Tours/weather.cmds`.
* `MusicDirector.cs` got two small hooks (weather events are skipped as event music; `Variant`/`LayerVariant` from
  the new `MusicDirector.Variants.cs`); `PerfOverlay.cs` one F3 line; `WorldRuntime` gained `Weather`/`Ambience`.
