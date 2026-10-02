using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Town;

public enum ResidentActivity
{
    Idle,
    Walking,
    GoingHome,
    Talking,
}

/// <summary>A town resident living in the world: identity, home, body and current errand.</summary>
public sealed class TownNpc
{
    public static readonly Vector3 HalfExtents = new(0.35f, 0.9f, 0.35f);

    public TownNpc(TownNpcDef def, string name)
    {
        Def = def;
        Name = name;
        Life = def.Life;
    }

    public long Id { get; init; }
    public TownNpcDef Def { get; }

    /// <summary>Personal name ("Corwin").</summary>
    public string Name { get; }

    public string Title => $"{Name} the {Def.DisplayName}";

    /// <summary>Home navigation node (null = homeless) and the key of its room.</summary>
    public Int3? Home { get; set; }
    public Int3? HomeRoom { get; set; }

    /// <summary>Where the housing banner hangs: the highest room cell above the home node (null until known).</summary>
    public Int3? BannerCell { get; set; }

    /// <summary>Feet position (m).</summary>
    public Vector3 Position;
    public Vector3 Velocity;
    public float Yaw { get; set; }
    public bool OnGround { get; set; }

    public int Life { get; set; }
    public bool Alive => Life > 0;
    /// <summary>Retry delay while a fallen resident's place is not loaded (ticks).</summary>
    public int RespawnTicks { get; set; }

    /// <summary>World tick of the last death or vanishing (−1 = never); the resident returns at the next dawn.</summary>
    public long DiedAtTick { get; set; } = -1;

    /// <summary>Tied up somewhere, waiting for a rescue: does not count as a resident until freed.</summary>
    public bool Bound { get; set; }

    /// <summary>Counts toward the town's population: alive, free, and a housed kind (not a landmark keeper).</summary>
    public bool CountsAsResident => Alive && !Bound && Def.Kind == TownNpcKind.Resident;

    /// <summary>A counting resident with a home ("needs N residents" counts these).</summary>
    public bool IsHoused => CountsAsResident && Home != null;
    public int ImmuneTicks { get; set; }

    public ResidentActivity Activity { get; set; }
    public PlayerEntity? TalkingTo { get; set; }
    public int TalkTicks { get; set; }

    internal List<Int3>? Path { get; set; }
    internal int PathIndex { get; set; }
    internal int IdleTicks { get; set; }
    internal int StuckTicks { get; set; }
    internal float LastDistance { get; set; }
    internal List<Voxels.TileObjects.TileObjectInstance> OpenedDoors { get; } = new();

    public Vector3 Center => Position + new Vector3(0, HalfExtents.Y, 0);

    /// <summary>Animation pose for the renderer: idle, walk, jump.</summary>
    public string Pose => !OnGround ? "jump" : new Vector2(Velocity.X, Velocity.Z).LengthSquared() > 0.05f ? "walk" : "idle";

    public (Vector3 Min, Vector3 Max) CellBox => ((Center - HalfExtents) / Units.CellSize, (Center + HalfExtents) / Units.CellSize);

    public override string ToString() => $"{Title} ({Def.Id})";
}
