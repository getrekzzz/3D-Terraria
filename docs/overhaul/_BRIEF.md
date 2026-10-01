# Implementation brief for overhaul work-package (WP) agents

You implement ONE work package of the player-feedback overhaul of Strata (a Godot 4.7 .NET C# "3D Terraria").
The approved plan is `/root/.claude/plans/overhaul-specs/_PLAN.md`. Your WP spec is
`/root/.claude/plans/overhaul-specs/<WP-ID>.json` (design steps, files, data, tests, verification, risks). Cross-phase
amendments that override specs are in `_PLAN.md` ("Cross-cutting amendments") and `_integration.json` (conflicts,
wrong_or_risky). Read both before coding. The specs were written against HEAD 15bf3f4; earlier WPs in your lane may have
changed files since — re-read the code, adapt, never blindly apply line numbers.

## Where you work
* You work in a LANE worktree given in your prompt (e.g. `/home/user/wt/laneA`), on the lane branch named in the prompt.
  `cd` there first; never edit `/home/user/3D-Terraria` (the integration checkout) unless your prompt says so.
* Commit your WP on the lane branch as one or more commits whose subject starts with the WP id, e.g.
  `P1-05: eye-plane view ray, target filters, Reach helper`. Every commit message MUST end with the attribution lines your own session specifies (the integration session uses exactly these):
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_013cVXuBXnb4kEQ1xX4FLVB1
  ```
  Do NOT push. Do NOT include model names/ids anywhere in code, comments or docs.

## Hard rules (docs/CONVENTIONS.md)
* `src/Strata.Core` never references Godot. Deterministic/testable logic in Core with tests; Godot adapters in `game/`.
* Every .cs/.py file ≤ 400 lines (`python3 tools/ci/check_file_length.py`); split into partial classes/new files.
* Content is data (JSON under `data/`), validated by `Strata.Tools validate`; no content tables in code; ids snake_case
  with Terraria names; new data files must be registered via `dotnet run --project tools/Strata.Tools -c Debug -- manifest`
  (regenerate `data/manifest.json`, never hand-edit it).
* WorldGen determinism: no Math.Sin/Cos/Pow/Exp/Log in generation code; bump `GenVersion` only if your WP says so;
  re-pin golden hashes in `tests/Strata.Core.Tests/WorldGen/WorldGenTests.cs` only if your WP changes generation.
* Performance matters (the player has lag spikes): no per-frame allocations/LINQ on hot paths, pool nodes/materials,
  budget per-frame work, preload, scale by graphics preset.
* Match the surrounding code style and comment density. No TODO stubs, no fake systems: implement fully or note the
  gap explicitly in your final report.
* Keep old player saves loadable (additive fields; fixtures). Old worlds are disposable (player decision).

## Toolchain (already installed)
* `dotnet` 8 SDK (apt). Build: `dotnet build Strata.sln -c Debug`. Tests: `dotnet test tests/Strata.Core.Tests -c Debug --no-build`
  (filter with `--filter FullyQualifiedName~Name`).
* Godot: `GODOT=/root/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64`.
  Build game assembly: `dotnet build Strata.csproj -c Debug`. Import (needed after adding assets/scenes/shaders):
  `timeout 1500 $GODOT --headless --path . --import`.
* ALWAYS isolate Godot user data per lane: prefix every Godot run with `XDG_DATA_HOME=$PWD/.xdg XDG_CONFIG_HOME=$PWD/.xdgc`
  (both are git-ignored via .git/info/exclude; create them if missing).
* Smoke script (must print `SMOKE PASS`):
  `XDG_DATA_HOME=$PWD/.xdg XDG_CONFIG_HOME=$PWD/.xdgc timeout 900 $GODOT --headless --path . --audio-driver Dummy -- --world=smoke-NAME --size=tiny --new --player=smoke --fresh-player --view=2 --smoke=res://game/Testing/Scripts/NAME.cmds 2>&1 | tee /tmp/smoke-NAME.log | tail -20; grep -q "SMOKE PASS" /tmp/smoke-NAME.log`
* Menu tour: `... $GODOT --headless --path . --audio-driver Dummy -- --menu-tour` → `MENU PASS`.
* Rendered screenshots (slow, lavapipe): `xvfb-run -a -s "-screen 0 1600x900x24" env XDG_DATA_HOME=... $GODOT --path . --rendering-driver vulkan -- --world=... --shots=/tmp/shots --smoke=res://game/Testing/Tours/<tour>.cmds --graphics=medium`.
  Use only when your WP's verification requires visuals; keep runs short.
* Python: `python3` (3.11, numpy/Pillow) for tools/textures, tools/audio, tools/content; `python3.13` has `bpy` 5.2.2 for
  Blender: `python3.13 tools/blender/build_all.py --only=GLOB` (never run `--anims` unless your WP is P1-12 or P3-03).
* Checks: `python3 tools/ci/check_file_length.py`, `python3 tools/content/gen_ore_tiers.py --check`,
  `dotnet run --project tools/Strata.Tools -c Debug --no-build -- manifest --check|validate|coverage`,
  `python3 tools/content/gen_reference.py --check`, `python3 tools/blender/build_all.py --check`.

## LEAN MODE (player decision 2026-10-01: speed matters — the overhaul was taking too long)
* NO software-rendered (lavapipe) tuning loops. Tune from numbers, data and code reasoning. At most ONE short rendered
  verification run per WP, and only if your spec's verification requires visuals; never iterate renders.
* Smoke: run `basic` plus only the smoke scripts that directly exercise what you changed (the gate runs all of them).
* Don't gold-plate: implement the spec's player-visible outcome fully and correctly, skip optional extras, and keep
  reports short. If a spec step is clearly low value or blocked, skip it and say so in `gaps`.
* When given several WP ids in one prompt (a bundle), implement them in order, one commit (or more) per WP id.

## Definition of done for a WP
1. Implemented per spec (+ amendments), adapted to the current code.
2. New Core tests from the spec written and passing; full Core suite passes.
3. All checks above pass; `dotnet build Strata.csproj` passes.
4. The smoke scripts your WP touches or could affect pass (at least `basic` plus the ones named in the spec). If you
   changed shared gameplay (items, player, NPCs, UI input), also run `combat` and `movement`.
5. Docs: append a short note under the relevant heading in `docs/architecture/*.md` when you add a system.
6. Committed on the lane branch. Final report (your final message): WP id, what changed (files), tests/smoke run and
   their results, deviations from the spec and why, known gaps, follow-ups for later WPs.
