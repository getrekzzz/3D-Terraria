using Godot;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.Voxels;
using Strata.Game.World;

namespace Strata.Game.Player;

/// <summary>
/// Turns input into gameplay: crosshair targeting, item use (mine, chop, place, consume, attack), object
/// interaction, hotbar selection, build shape and rotation, precision mode and item dropping.
/// </summary>
public sealed partial class InteractionController : Node
{
    private static readonly CellShape[] ShapeCycle =
    {
        CellShape.Full, CellShape.Slope, CellShape.HalfBottom, CellShape.Stair, CellShape.Panel,
        CellShape.Platform, CellShape.Post, CellShape.HalfTop, CellShape.SlopeCeiling, CellShape.StairCeiling,
    };

    private WorldRuntime _runtime = null!;
    private PlayerController _player = null!;

    /// <summary>The current crosshair target (null when pointing at nothing within range).</summary>
    public Target? Target { get; private set; }

    /// <summary>True when the target is within the held item's reach.</summary>
    public bool InReach { get; private set; }

    /// <summary>When false (menus open) gameplay input is ignored.</summary>
    public bool Enabled { get; set; } = true;

    public event System.Action<UseOutcome>? Used;
    public event System.Action<InteractOutcome>? Interacted;

    private GameWorld Game => _runtime.Game;
    private PlayerEntity Entity => _player.Entity;

    public void Initialize(WorldRuntime runtime, PlayerController player)
    {
        _runtime = runtime;
        _player = player;
        Name = "Interaction";
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateTarget();
        if (!Enabled || _player.Frozen || !Entity.Alive || UI.InputGate.GameplayBlocked) return;
        Entity.Precision = Input.IsActionPressed("precision_mode");

        bool fresh = Input.IsActionJustPressed("use_primary");
        if (fresh || Input.IsActionPressed("use_primary"))
        {
            var outcome = Game.ItemUse.Use(Entity, Target, fresh);
            if (outcome is not (UseOutcome.None or UseOutcome.Cooldown)) Used?.Invoke(outcome);
        }
        if ((Input.IsActionJustPressed("use_secondary") || Input.IsActionJustPressed("interact")) && Target?.Object is { } obj && InReach)
        {
            var r = Game.Interaction.Interact(Entity, obj);
            if (r != InteractOutcome.None) Interacted?.Invoke(r);
        }
    }

    private void UpdateTarget()
    {
        var cam = _player.Rig.Camera;
        var origin = cam.GlobalPosition;
        var dir = _player.Rig.Forward;
        var held = Entity.State.Inventory.Held;
        float reach = (held.IsEmpty ? 10 : held.Item!.Reach) * Strata.Core.Common.Units.CellSize;
        float camOffset = origin.DistanceTo(ToGodot(Entity.Eye));
        var hit = Targeting.Cast(Game.Session.World, ToCore(origin), ToCore(dir), reach + camOffset + 2f);
        if (hit != null)
        {
            var point = hit.HitCells * Strata.Core.Common.Units.CellSize;
            float fromEye = System.Numerics.Vector3.Distance(point, Entity.Eye);
            Target = hit with { Distance = fromEye };
            InReach = fromEye <= reach;
            Entity.Aim = System.Numerics.Vector3.Normalize(point - Entity.Eye);
        }
        else
        {
            Target = null;
            InReach = false;
            var far = origin + dir * 40f;
            Entity.Aim = System.Numerics.Vector3.Normalize(ToCore(far) - Entity.Eye);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Enabled || UI.InputGate.GameplayBlocked) return;
        var inv = Entity.State.Inventory;
        for (int i = 0; i < PlayerInventory.HotbarSize; i++)
            if (@event.IsActionPressed($"hotbar_{i + 1}"))
            {
                inv.Select(i);
                return;
            }
        if (@event is InputEventMouseButton { Pressed: true, CtrlPressed: false } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelDown) inv.Select(inv.SelectedHotbar + 1);
            else if (mb.ButtonIndex == MouseButton.WheelUp) inv.Select(inv.SelectedHotbar - 1);
        }
        if (@event.IsActionPressed("cycle_shape"))
        {
            int i = System.Array.IndexOf(ShapeCycle, Entity.BuildShape);
            int step = @event is InputEventKey { ShiftPressed: true } ? -1 : 1;
            Entity.BuildShape = ShapeCycle[((i + step) % ShapeCycle.Length + ShapeCycle.Length) % ShapeCycle.Length];
        }
        if (@event.IsActionPressed("rotate_shape")) Entity.BuildRotation = (Entity.BuildRotation + 1) & 3;
        if (@event.IsActionPressed("drop_item")) DropHeld(@event is InputEventKey { CtrlPressed: true });
    }

    private void DropHeld(bool wholeStack)
    {
        var inv = Entity.State.Inventory;
        var held = inv.Held;
        if (held.IsEmpty) return;
        var stack = inv.Backpack.Take(inv.SelectedHotbar, wholeStack ? held.Count : 1);
        var aim = Entity.Aim;
        var velocity = new System.Numerics.Vector3(aim.X, 0.4f, aim.Z) * 5f;
        Game.Drops.Spawn(stack, Entity.Eye + aim * 0.6f, velocity, pickupDelay: 90);
    }

    private static System.Numerics.Vector3 ToCore(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
