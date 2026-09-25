using Strata.Core.Common;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Session;

/// <summary>Voxel data produced off the main thread for one chunk (loaded from the save or generated).</summary>
public sealed record ChunkProduct(Int3 Coord, ChunkData Data, IReadOnlyList<TileObjectInstance> Objects, int MutationLevel, bool FromSave);
