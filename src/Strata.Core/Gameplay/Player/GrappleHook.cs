using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Content.Specs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Physics;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Player;

/// <summary>Where a grappling hook is in its throw. Smoke scripts probe the number (<c>player.hook</c>).</summary>
public enum HookState
{
    None = 0,
    Flying = 1,
    Attached = 2,
    Retracting = 3,
}

/// <summary>
/// A player's grappling hook, as in Terraria: thrown along the aim (a click with the hook held, or the grapple key), it
/// latches onto the first block, platform, tree or piece of furniture within reach and reels the player towards it. It
/// lets go when that cell is broken, the player jumps or presses again. The anchor is the exact voxel cell the throw
/// struck, so smoothed natural terrain, whose rendered surface sits a little off the cell grid, holds the hook as firmly
/// as placed blocks. Core decides the anchor, the state and the pull; the engine body applies
/// <see cref="PullVelocity"/> and advances the hook in its physics step (without one, <see cref="Session.GameWorld"/>
/// advances it each tick, see <see cref="Session.PlayerEntity.HasBody"/>).
/// </summary>
public sealed class GrappleHook
{
    /// <summary>A latched hook this close (m) to the body centre holds the player still instead of pulling.</summary>
    public const float HangDistance = 1.1f;

    /// <summary>A hook comes back this many times faster than it flies out.</summary>
    public const float RetractFactor = 1.5f;

    /// <summary>The returning head is caught within this distance (m) of the hand.</summary>
    public const float CatchDistance = 0.2f;

    public HookState State { get; private set; }

    /// <summary>The hook head (m).</summary>
    public Vector3 Head { get; private set; }

    /// <summary>Where the throw ends (m): the point struck on <see cref="AnchorCell"/>, or the end of the reach on a miss.</summary>
    public Vector3 Target { get; private set; }

    /// <summary>The throw struck something: the hook latches on arrival (if the cell is still there) instead of coming back.</summary>
    public bool Hit { get; private set; }

    /// <summary>The cell the hook latches to (meaningful while <see cref="Hit"/>).</summary>
    public Int3 AnchorCell { get; private set; }

    /// <summary>The face of <see cref="AnchorCell"/> the throw struck (zero when the throw started inside the cell).</summary>
    public Int3 AnchorNormal { get; private set; }

    /// <summary>Reach, speeds and chain colour of the hook thrown last.</summary>
    public GrappleParams? Params { get; private set; }

    /// <summary>The hook item thrown last (its head and chain are drawn).</summary>
    public ItemDef? Item { get; private set; }

    /// <summary>Out in the world: flying, latched or on its way back.</summary>
    public bool Active => State != HookState.None;

    /// <summary>
    /// Can a hook hold on to cell <paramref name="c"/>? Solid blocks (platforms are solid blocks with a thin shape), a
    /// rendered block shaped with a walkable top, and tile objects that collide, which includes a tree's whole box
    /// (its canopy). Air, liquids, non-solid blocks such as cobwebs, decor and open doors (bodies walk through them)
    /// never hold it. A cell whose chunk is not loaded counts as holding, as for collision.
    /// </summary>
    public static bool Latchable(VoxelWorld w, Int3 c)
    {
        ushort block = w.GetBlock(c);
        if (block == VoxelWorld.Unknown) return true;
        if (block != 0)
        {
            var def = w.Content.Blocks[block];
            if (def.IsSolid) return true;
            byte shape = w.GetShape(c.X, c.Y, c.Z);
            if (def.Render != BlockRender.None && shape != 0 && ShapeCode.IsWalkableTop(ShapeCode.Shape(shape))) return true;
        }
        return w.TileObjects.At(c) is { } o && o.Def.Collision != TileCollision.None && !o.Def.Decor && !(o.IsDoor && o.Active);
    }

    /// <summary>
    /// Throws the hook from <paramref name="hand"/>: the voxel ray from <paramref name="eye"/> along
    /// <paramref name="dir"/> (the crosshair) picks the first latchable cell within reach, ignoring a tree or other
    /// object the eye stands inside. On a miss the hook flies to the end of its reach and comes back.
    /// </summary>
    public void Fire(VoxelWorld w, GrappleParams gp, ItemDef item, Vector3 hand, Vector3 eye, Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-10f) return;
        dir = Vector3.Normalize(dir);
        float reach = gp.Reach * Units.CellSize;
        var inside = w.TileObjects.At(VoxelMotion.CellOf(eye));
        var hit = Targeting.CastWhere(w, eye, dir, reach,
            (cell, _) => Latchable(w, cell) && (inside == null || w.TileObjects.At(cell) != inside));
        Params = gp;
        Item = item;
        Head = hand;
        Hit = hit != null;
        AnchorCell = hit?.Cell ?? Int3.Zero;
        AnchorNormal = hit?.Normal ?? Int3.Zero;
        Target = hit != null ? hit.HitCells * Units.CellSize : hand + dir * reach;
        State = HookState.Flying;
    }

    /// <summary>
    /// Advances the hook by <paramref name="dt"/> seconds: a flying head moves to its target and latches (when it struck a
    /// cell that is still latchable) or turns back; a latched hook lets go once its anchor cell no longer holds; a
    /// returning head moves to <paramref name="hand"/> and is put away there.
    /// </summary>
    public void Tick(VoxelWorld w, float dt, Vector3 hand)
    {
        switch (State)
        {
            case HookState.Flying:
                Head = MoveToward(Head, Target, Params!.Speed * dt);
                if (Head == Target) State = Hit && Latchable(w, AnchorCell) ? HookState.Attached : HookState.Retracting;
                break;
            case HookState.Attached:
                if (!Latchable(w, AnchorCell)) State = HookState.Retracting;
                break;
            case HookState.Retracting:
                Head = MoveToward(Head, hand, Params!.Speed * RetractFactor * dt);
                if (Vector3.DistanceSquared(Head, hand) < CatchDistance * CatchDistance) State = HookState.None;
                break;
        }
    }

    /// <summary>
    /// The velocity (m/s) that reels a body whose centre is at <paramref name="bodyCenter"/> towards the anchor: the
    /// hook's pull speed straight at <see cref="Target"/>, or zero (hanging) within <see cref="HangDistance"/> of it.
    /// Zero unless latched.
    /// </summary>
    public Vector3 PullVelocity(Vector3 bodyCenter)
    {
        if (State != HookState.Attached) return Vector3.Zero;
        var to = Target - bodyCenter;
        float dist = to.Length();
        return dist > HangDistance ? to / dist * Params!.Pull : Vector3.Zero;
    }

    /// <summary>Lets go: a flying or latched hook comes back to the hand.</summary>
    public void Release()
    {
        if (State is HookState.Flying or HookState.Attached) State = HookState.Retracting;
    }

    /// <summary>Puts the hook away at once (death, teleports): nothing to draw or pull.</summary>
    public void Reset()
    {
        State = HookState.None;
        Hit = false;
    }

    /// <summary>
    /// The hook the grapple key throws: the one in the hook slot, otherwise the longest-reaching one carried in the
    /// hotbar and backpack (the first found wins a tie), or null when the player has none.
    /// </summary>
    public static GrappleParams? Best(PlayerState s, ItemUseTable uses, out ItemDef? item)
    {
        if (s.Inventory.EquippedHook is { } worn && uses[worn].Grapple is { } equipped)
        {
            item = worn;
            return equipped;
        }
        GrappleParams? best = null;
        item = null;
        Scan(s.Inventory.Backpack, uses, ref best, ref item);
        return best;
    }

    private static void Scan(Inventory container, ItemUseTable uses, ref GrappleParams? best, ref ItemDef? item)
    {
        for (int i = 0; i < container.Size; i++)
        {
            var s = container[i];
            if (s.IsEmpty || uses[s.Item!].Grapple is not { } g) continue;
            if (best != null && g.Reach <= best.Reach) continue;
            best = g;
            item = s.Item;
        }
    }

    private static Vector3 MoveToward(Vector3 from, Vector3 to, float step)
    {
        var d = to - from;
        float len = d.Length();
        return len <= step || len < 1e-6f ? to : from + d / len * step;
    }
}
