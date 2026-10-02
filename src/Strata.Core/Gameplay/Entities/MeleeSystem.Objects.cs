using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Entities;

/// <summary>Swings and thrusts smash what weapons break (pots, grass, flowers: <c>breakByAttack</c>) with the same blade test as creatures (P3-23).</summary>
public sealed partial class MeleeSystem
{
    /// <summary>Half size (cells) of the box searched for breakable objects around the swinging player.</summary>
    private const int ObjectSearch = 8;

    private readonly List<TileObjectInstance> _smash = new();

    /// <summary>The world whose objects swings break (set by <see cref="GameWorld"/>).</summary>
    public GameWorld? World { get; init; }

    private void SmashObjects(Swing s, Func<Vector3, Vector3, bool> connects)
    {
        if (World == null) return;
        var c = s.Player.FeetCell;
        var box = new Aabb3i(c - new Int3(ObjectSearch, ObjectSearch / 2, ObjectSearch), c + new Int3(ObjectSearch, ObjectSearch, ObjectSearch));
        _smash.Clear();
        foreach (var obj in World.Session.World.TileObjects.InBox(box))
        {
            if (!obj.Def.BreakByAttack) continue;
            var b = obj.Bounds;
            var min = new Vector3(b.Min.X, b.Min.Y, b.Min.Z) * Units.CellSize;
            var max = new Vector3(b.Max.X, b.Max.Y, b.Max.Z) * Units.CellSize;
            if (connects(min, max)) _smash.Add(obj);
        }
        foreach (var obj in _smash) World.Editor.SmashObject(obj);
    }
}

[Commands.ProbeSource]
internal static class SmashProbes
{
    public static void Register(Commands.CommandContext ctx) => ctx.AddProbe("objects.smashed", () => ctx.World.Editor.Smashed);
}
