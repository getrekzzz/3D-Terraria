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

    /// <summary>The town resident under the crosshair within talking range (null if none).</summary>
    public Strata.Core.Gameplay.Town.TownNpc? TalkTarget { get; private set; }

    /// <summary>The player asked to talk to a resident (the dialog opens).</summary>
    public event System.Action<Strata.Core.Gameplay.Town.TownNpc>? TalkRequested;

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
        // Grid blocks (1 m) by default, single cells when the setting says so; holding the precision key swaps.
        Entity.Precision = Settings.UserSettings.Current.SmallBlocks != (!UI.InputGate.GameplayBlocked && Input.IsActionPressed("precision_mode"));
        UpdateTarget();
        if (!Enabled || _player.Frozen || !Entity.Alive || UI.InputGate.GameplayBlocked) return;

        bool fresh = Input.IsActionJustPressed("use_primary");
        if (fresh || Input.IsActionPressed("use_primary"))
        {
            var outcome = Game.ItemUse.Use(Entity, Target, fresh);
            if (outcome is not (UseOutcome.None or UseOutcome.Cooldown)) Used?.Invoke(outcome);
        }
        bool interact = Input.IsActionJustPressed("use_secondary") || Input.IsActionJustPressed("interact");
        if (interact && TalkTarget is { } resident && Game.Town.BeginTalk(resident, Entity))
        {
            TalkRequested?.Invoke(resident);
            return;
        }
        if (interact && Target?.Object is { } obj && InReach)
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
        if (DigDownTarget(dir) is { } below)
        {
            Target = below;
            InReach = true;
        }
        UpdateTalkTarget(origin, dir);
    }

    /// <summary>
    /// Smart digging: looking steeply down with a pickaxe targets the ground under the middle of the feet (instead of
    /// wherever the over-the-shoulder ray lands), so holding the button digs a shaft straight down that you drop into.
    /// </summary>
    private Target? DigDownTarget(Vector3 dir)
    {
        if (!Settings.UserSettings.Current.SmartDig || dir.Y > -0.8f) return null;
        var held = Entity.State.Inventory.Held;
        if (held.IsEmpty || !Game.Uses[held.Item!].Has(ItemAction.Mine)) return null;
        if (Target != null)
        {
            // Only when the ray lands next to the feet anyway (not at a ledge far below).
            var hit = Target.HitCells * Strata.Core.Common.Units.CellSize;
            var feet = Entity.Position;
            if (new Vector2(hit.X - feet.X, hit.Z - feet.Z).Length() > 1.6f || Target.Object != null) return null;
        }
        return Targeting.UnderFeet(Game.Session.World, Entity.Position);
    }

    /// <summary>The nearest resident whose body the aim ray passes close to, in front of any terrain hit.</summary>
    private void UpdateTalkTarget(Vector3 origin, Vector3 dir)
    {
        TalkTarget = null;
        float limit = Target != null ? origin.DistanceTo(ToGodot(Target.HitCells * Strata.Core.Common.Units.CellSize)) + 0.5f : 40f;
        float best = float.MaxValue;
        foreach (var npc in Game.Town.Residents)
        {
            if (!npc.Alive) continue;
            var center = ToGodot(npc.Center);
            float along = (center - origin).Dot(dir);
            if (along <= 0 || along > limit) continue;
            float off = (origin + dir * along).DistanceTo(center);
            if (off > 0.7f || center.DistanceTo(ToGodot(Entity.Center)) > Game.Town.Config.TalkRange) continue;
            if (along < best)
            {
                best = along;
                TalkTarget = npc;
            }
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
