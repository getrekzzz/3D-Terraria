# Overhaul: parallel sessions (2026-10-01)

The player-feedback overhaul (plan: `docs/overhaul/_PLAN.md`, per-work-package specs: `docs/overhaul/specs/<WP>.json`,
cross-phase amendments: `docs/overhaul/_integration.json`, agent rules: `docs/overhaul/_BRIEF.md`) is split across
cloud sessions to cut wall-clock time. Each session owns a disjoint slice and pushes ONLY its own branch; the
integration session merges every branch into `claude/clever-goodall-kgex45` with full CI.

| Session | Branch | Owns (in this order) |
|---|---|---|
| Integration (original) | `claude/clever-goodall-kgex45` | W1b Phase 1 → W3/W4 Terraria UI → X-02 → W5/W6 items & feel (minus the art items below; P3-49 moved to World) → X-16 → X-12 → X-04 → final integration (W2 lag spikes moved to the Art session, except P1b-15 which rides with P3-10/P3-11 in W5) |
| Bosses + Dungeon | `claude/ovh-bosses` | W7: P4-01, P4-02, P4-03', P4-04 (+P3-32 bags), P4-05 … P4-11 (+Clothier Voodoo Doll use) · W8: P4-12, P4-13, P4-14, P4-15, P4-16, P4-17, P4-18, P4-19, P4-20 (+Voodoo Doll item/drop), P4-21, P4-33, P4-34, P4-23 · W9: P4-24, P4-25, P4-26, P4-27, P4-28, P4-29, P4-30, P4-31, P4-32, P4-38 |
| Art | `claude/ovh-art` | (then X-17, then W2 lag spikes P1b-06…P1b-14 + P1b-16, then the X-09 creator menu rows after P2-30) P3-05 (held-item sockets, FIRST), P3-37 metal colours, P3-37b vibrant glowing world ores, P2-24 boss head icons, P5-28 NPC portraits + bestiary thumbnails, P3-38, P3-39, P3-40, P3-41a/b, P3-42, P3-43, P3-44, P3-45, P3-50, P4-35, P4-36, P4-37 |
| NPCs + Guide book (starts after the UI wave lands) | `claude/ovh-npcs` | W10 + W11 minus P5-28, then X-05 emotes + NPC voices, X-10 onboarding hints + loading tips |
| World & life | `claude/ovh-world` | X-01 weather, X-08 ambience + cave audio + music variants (absorbs P3-49), X-06 seasonal events, X-13 fishing depth, X-15 wiring set, X-14 mounts (after the integration session pushes W3), X-03 furniture families + decor + signs (after the Bosses session pushes W8), X-07 secret seeds (after the Bosses W8 Dungeon push) |

### AAA additions (2026-10-01, player: "anything a AAA realistic Terraria from the original creators would need")
Specs `docs/overhaul/specs/X-*.json`. Owners: Integration — X-02 boss intro card / arena framing / victory sting /
per-boss themes (after W4), X-12 photo mode / autosave icon / colour-blind / subtitles, X-16 ragdolls (after P3-20),
X-04 gamepad support (last, after every UI exists). Bosses — X-11 disguised Mimics, Nymph, Wyvern (after W9).
Art — X-09 character creator depth (after the integration UI push), X-17 hair/cloth physics (last).
World & life — see the table. NPCs — X-05, X-10. Co-op multiplayer is NOT in scope unless the player asks.

## Rules for every session
* **Screenshots to the player (standing rule):** after every major completion (each push and each notable feature),
  send the player 1–3 screenshots with the SendUserFile tool (status "proactive", display "render", a one-line
  plain-English caption). One short render per update (lavapipe in-game shot, Blender contact sheet or icon grid);
  never a tuning loop; don't commit the PNGs.
* Follow `docs/overhaul/_BRIEF.md` (LEAN MODE: no software-rendered tuning loops; adversarial review only for risky
  gameplay/Core/save/worldgen code; bundle small WPs; full headless CI before every push).
* Setup (fresh container): `apt-get update && apt-get install -y dotnet-sdk-8.0 xvfb mesa-vulkan-drivers libvulkan1
  fonts-comic-neue fonts-sil-andika unzip`; `bash tools/ci/get-godot.sh /root/godot`;
  `python3.13 -m pip install --break-system-packages bpy==5.2.2 numpy Pillow`; `python3 -m pip install numpy Pillow`;
  `dotnet build Strata.sln -c Debug`; `dotnet build Strata.csproj -c Debug`; `$GODOT --headless --path . --import`.
  Copy the specs where the brief expects them: `mkdir -p /root/.claude/plans/overhaul-specs && cp docs/overhaul/specs/*.json docs/overhaul/_*.* /root/.claude/plans/overhaul-specs/`
  (the brief's paths point there; `docs/overhaul` is the source of truth).
* Stay inside your slice. Do NOT edit `game/UI/Hud*.cs`, `game/UI/InventoryScreen*.cs`, `game/UI/TownDialog*.cs`,
  `game/UI/UiTheme*.cs` unless your slice owns that WP; the integration session is rebuilding the UI concurrently.
  Prefer new partial files over growing shared hubs (`NpcSystem*.cs`, `ItemUseSystem*.cs`, `ProjectileSystem*.cs`,
  `SoundEvents*.cs`, `CombatEffects*.cs`, `EnemyDef.cs`/`BossInfo.cs`), and keep one JSON entry per line in data files,
  so merges stay mechanical.
* Generated files are never hand-merged: regenerate `data/manifest.json` (`Strata.Tools manifest`) and
  `assets/manifest.json` / icons (`tools/blender/build_all.py`) after merging. Only rebuild assets in your own scope
  (`build_all.py --only=GLOB`); never run `--anims` (the human animation sets are owned by the integration session).
* Worldgen: only the Bosses session (P4-15) bumps `GenVersion` and re-pins golden hashes in W8; before W8 it must
  merge the latest `origin/claude/clever-goodall-kgex45` (Phase 1 terrain, GenVersion 10) so the bump is 10 → 11.
* Cross-slice dependencies:
  - Bosses P4-03 must not touch the HUD: emit announcements through the existing Core announcement path with a
    message kind/colour (boss awoken = purple, defeated = purple), use the existing CameraShake API; the integration
    session's ChatLog/BossBar (P2-21/P2-23) will consume them.
  - Integration W5 (P3-06 grips) needs Art's P3-05 sockets: Art does P3-05 first and pushes it early.
  - NPCs P5-09 needs Bosses P4-01's `ICombatTarget`: merge `origin/claude/ovh-bosses` before P5-09.
  - Bosses P4-19 reserves a `bound_npc` room role in the Dungeon plan for the NPC session's bound Mechanic (P5-06b).
* Commit messages: WP id first (`P4-10: ...`), end with your own session's attribution lines. Push your branch after
  each wave with `git push -u origin <your branch>` and update `docs/overhaul/status-<slice>.md` on your branch
  (wave, WPs done, CI result, gaps) so the integration session can follow along.
