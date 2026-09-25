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
Because light spreads ≤15 cells the result is exact; no global light state exists. The chunk keeps the
result for gameplay (spawn light conditions, `LightProbe` for props/creatures/drops).
Shaders apply `ambient(time) × sky + block light` as emission, and gate the sun by skylight so caves stay
dark beyond shadow range; placed torches are baked, OmniLights are reserved for moving sources.

## Meshing
Constrained surface nets: one vertex per dual cell (crossing-edge centroid), Taubin smoothing clamped to
0.45 of the cell so single blocks never vanish; crisp/shaped/transparent cells snap to grid corners so
built geometry stays exact and flush with smooth terrain. Materials: exposure-weighted majority per vertex,
barycentric three-surface blending in the shader (triplanar sampling of the terrain texture array).
Normals come from a tent-filtered density gradient including the padding ring, so chunk borders match.
Also produced: liquid surfaces, cutout (leaves, cobweb) and glass surfaces, and collision triangles in
16³ buckets (solid + one-way platforms). Colliders exist only near the player and rebuild the same frame
for edits next to it.
