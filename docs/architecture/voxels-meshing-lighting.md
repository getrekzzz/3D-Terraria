# Voxels, meshing and lighting

## Data
* 32³ chunks; per cell a `ushort` block id, and lazily allocated shape (shape+rotation), liquid (type+level)
  and flag (player-placed, structure) bytes. Uniform chunks store one id.
* Chunks are **copy-on-write**: `EditBatch` clones touched arrays once, publishes them atomically and bumps
  the version; `EditResult` lists changed chunks and every chunk within the 16-cell light/mesh influence.
* Tile objects (trees, furniture, stations, chests…) are multi-cell instances indexed by anchor chunk with a
  cell occupancy map; containers, door state and pending loot are saved with the chunk.

## Lighting (stateless)
Per mesh job, a 64³ neighbourhood (chunk ± 16 cells) is lit by BFS: 4-bit skylight seeded from the sky map
(per-column highest opaque cell) and RGB block light from emissive blocks and tile-object light sources.
Open air under a *floating* roof — a sky island, an arch, a high canopy: the first blocking cell sits above the
terrain surface estimate with at least 12 cells of air below it — is seeded as outdoor shade (sky 12) from the ground
up (`SkyMap.ShadeTop`), so the land under an island is shaded rather than cave-dark; caves and ordinary rooms keep
the old behaviour.
Because light spreads ≤15 cells the result is exact; no global light state exists. The chunk keeps the
result for gameplay (spawn light conditions, `LightProbe` for props/creatures/drops).
Shaders use the baked sky exposure as ambient occlusion for the physical sky's ambient light and reflections,
apply block light as emission, and gate the sun by skylight so caves stay dark beyond shadow range; placed torches
are baked, OmniLights are reserved for moving sources (see [rendering](rendering.md)).

## Meshing
Constrained surface nets: one vertex per dual cell (crossing-edge centroid), Taubin smoothing clamped to
0.45 of the cell so single blocks never vanish; crisp/shaped/transparent cells snap to grid corners so
built geometry stays exact and flush with smooth terrain. Materials: exposure-weighted majority per vertex,
barycentric three-surface blending in the shader (triplanar sampling of the terrain texture array).
Normals come from a tent-filtered density gradient including the padding ring, so chunk borders match.
Quads split along their shorter diagonal, keeping the first one on near ties (flat ground), so the rounding noise of
an edit metres away never flips the triangles (and the grass seeded from them) around it.
Heightfield reconstruction (`HeightfieldSmoothing`, after the relaxation): Taubin keeps long staircases as ripples, so
each free vertex on monotone, up-facing ground takes the tent-weighted mean (radius 4 columns) of the column tops
around it, read in a ±5-cell window that rejects columns with air under the top (ledges, roofs, caves), snapping
cells, or a step over 3 cells (cliffs); it needs its own four columns and 75% of the 8×8 around it valid. The height is
clamped to the dual cell and to the 0.45-cell drift, so colliders, `FeetCell` and object placement still agree with
the voxels; only a fixed local window is read, so chunk seams stay identical. `mesh-bench --roughness` prints the
RMS height deviation of up-facing vertices from a local 3×3 plane fit (seed 1234: 3.2 cm before, 1.7 cm after).
Also produced: liquid surfaces, cutout (leaves, cobweb) and glass surfaces, grass clumps on grass surfaces
(`GrassScatter`), and collision triangles in 16³ buckets (solid + one-way platforms). Colliders exist only near the player and rebuild the same frame
for edits next to it. The mesh job hashes each of the 16 buckets (`CollisionBuilder.ComputeHashes`); `ColliderManager`
keeps one static body per non-empty bucket and rebuilds only buckets whose hash changed, so liquid- and light-only
remeshes cost nothing (`colliders.skipped`). Build order per frame: the focus chunk and never-built neighbours at once,
then player-edited chunks (`Prioritize`), then the rest nearest-first within `FrameBudgets.CollidersMs`; old bucket
bodies stay until replaced. A light placed, broken or switched remeshes its own chunk urgently and its neighbours through
the normal queue (`WorldEditor.LightDirty`).
