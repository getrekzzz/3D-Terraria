using System.Linq;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.WorldGen.Plan;
using Strata.Core.WorldGen.Structures.Dungeon;

namespace Strata.Game.World;

public sealed partial class TileObjectRenderer
{
    /// <summary>Within this distance (m) of the dungeon gate, or anywhere inside the Dungeon, its props preload.</summary>
    private const float DungeonPreloadRange = 64f;
    private const int PreloadCheckInterval = 30;

    private int _preloadTick;
    private bool _dungeonQueued;
    private DungeonFeature? _dungeon;

    /// <summary>
    /// Queues every model the Dungeon uses once the player nears its gate (or stands inside it, e.g. after loading a
    /// save down there), then loads one per frame, so walking in never stalls on first-use GLB loads.
    /// </summary>
    private void PreloadNearDungeon()
    {
        _props.PumpPreload();
        if (_dungeonQueued || ++_preloadTick < PreloadCheckInterval) return;
        _preloadTick = 0;
        var session = _game.Session;
        var player = _game.LocalPlayer;
        if (player == null) return;
        var plan = session.Generator.Plan;
        var feet = player.Position / Units.CellSize;
        var cell = new Int3((int)feet.X, (int)feet.Y, (int)feet.Z);
        bool near = plan.Landmarks.TryGetValue(LandmarkIds.DungeonGate, out var gate)
            && (player.Position - new System.Numerics.Vector3(gate.X, gate.Y, gate.Z) * Units.CellSize).Length() <= DungeonPreloadRange;
        var dungeon = _dungeon ??= plan.Features.All.OfType<DungeonFeature>().FirstOrDefault();
        if (dungeon == null) { _dungeonQueued = true; return; }
        if (!near && !dungeon.InZone(cell)) return;
        _dungeonQueued = true;
        var content = session.Content;
        foreach (var id in dungeon.ObjectIds)
        {
            if (!content.TileObjects.TryGet(id, out var def)) continue;
            for (int v = 0; v < def.Variants; v++) _props.Preload(new[] { AssetPaths.ObjectModel(def, v) });
        }
    }
}
