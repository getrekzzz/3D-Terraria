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
