# 3D Terraria-Style World — Master Plan (persistent project memory)

> Update this file at the end of every development cycle. It is the single source of truth for what
> exists, what is partial, what is missing, and what comes next.

## 1. Vision
A sophisticated native-3D evolution of Terraria's gameplay philosophy — depth, interconnectivity,
freedom, discovery, progression structure and systemic interaction — not "voxels with Terraria items".
See the master directive summary in §9.

## 2. Decision log
| Date | Decision | Rationale |
|---|---|---|
| 2026-09-25 | Godot 4.7.2 .NET (C#, net8.0) + Blender 5.2 (`bpy`) | User choice. C# gives compiled speed for voxel work and a testable engine-agnostic core. |
| 2026-09-25 | Hybrid terrain: one 0.5 m voxel grid; natural materials smooth (constrained surface nets), built materials crisp | Organic caves + precise building from one data model. |
| 2026-09-25 | Original in-game names; `data/reference/` maps Terraria concepts | Keeps the project distributable; coverage is still measurable. |
| 2026-09-25 | `Strata.Core` has no Godot dependency | Unit-testable, deterministic, reusable by tools/CI. |
| 2026-09-25 | Lighting is a pure function of a 3×3×3 chunk neighbourhood + sky heightmap, computed inside mesh jobs | No global incremental light state; 4-bit light reaches ≤15 cells, so the neighbourhood is exact. |
| 2026-09-25 | Save only modified chunks; regenerate the rest; lazy world mutations with per-chunk level | Small saves; hardmode transformation reaches unexplored chunks for free. |

## 3. System status
Legend: ✅ complete · 🟡 partial · ⬜ missing

| Area | System | Status | Notes |
|---|---|---|---|
| Foundation | Solution/projects/CI | 🟡 | Core tests job live; Godot smoke + screenshot jobs pending |
| Foundation | Conventions / determinism rules | ✅ | `docs/CONVENTIONS.md` |

## 4. Technical debt
* _none recorded yet_

## 5. Dependency map
```
Content ─┬─> Voxels ─> WorldGen ─> Lighting ─> Meshing ─> Godot streaming/render
         ├─> Items/Inventory ─> Crafting ─> UI
         ├─> Combat ─> Enemies/AI ─> Spawning ─> Events ─> Bosses
         └─> Persistence (world, regions, players, migrations)
Progression flags ─> Mutations (hardmode) ─> WorldGen passes (lazy)
```

## 6. Validation status
| Check | Where | Status |
|---|---|---|
| Core unit tests | `tests/Strata.Core.Tests` | 🟡 |
| Module size guard | `tools/ci/check_file_length.py` | ✅ |

## 7. Performance concerns
* Targets: 60 fps @1080p on GTX-1060-class; ~250 LOD0 chunks; mesh ≤2 ms/chunk; gen ≤10 ms/chunk.

## 8. Next development targets
1. Core foundations (determinism, content system)
2. Voxels + persistence core
3. World generation passes + worldgen report

## 9. Directive summary (condensed)
Phases: 1 core loop → 2 world → 3 progression → 4 systemic depth → 5 hardmode → 6 endgame → 7 polish.
Absolute rules: no fake systems, no monoliths, no content tables in code, validate everything, keep
saves compatible, use Blender where custom 3D assets materially help.
