using System.Numerics;
using Strata.Core.Gameplay.Entities;

namespace Strata.Core.AI;

/// <summary>
/// Where a creature's decoy copies stand (the Brain of Cthulhu's illusions): its offset from
/// <see cref="Npc.MirrorAround"/> turned a quarter turn about +Y per copy, so the body and its copies stand 90° apart
/// around the target at the same distance and height. Quarter turns are sign swaps (no trigonometry).
/// </summary>
public static class Illusions
{
    public static Vector3 Position(Npc npc, int i) => npc.MirrorAround + Turn(npc.Center - npc.MirrorAround, i + 1);

    /// <summary>The copy's facing: the body's, turned with it.</summary>
    public static float Yaw(Npc npc, int i) => npc.Yaw + (i + 1) * (MathF.PI * 0.5f);

    /// <summary>Turns a vector <paramref name="quarters"/> quarter turns about +Y ((x, z) → (z, −x) each).</summary>
    public static Vector3 Turn(Vector3 o, int quarters) => (quarters & 3) switch
    {
        1 => new Vector3(o.Z, o.Y, -o.X),
        2 => new Vector3(-o.X, o.Y, -o.Z),
        3 => new Vector3(-o.Z, o.Y, o.X),
        _ => o,
    };
}
