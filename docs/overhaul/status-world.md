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
