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
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.interaction.physics");
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
            var at = new Vector3(obj.Anchor.X + 0.5f, obj.Anchor.Y + 0.5f, obj.Anchor.Z + 0.5f) * Strata.Core.Common.Units.CellSize;
            switch (r)
            {
                case InteractOutcome.DoorToggled: Audio.Sfx.PlayAt(obj.Active ? "door_open" : "door_close", at); break;
                case InteractOutcome.ContainerOpened: Audio.Sfx.PlayAt("chest_open", at); break;
                case InteractOutcome.Activated: Audio.Sfx.Play("summon"); break;
                case InteractOutcome.Switched: Audio.Sfx.PlayAt("lever", at); break;
            }
        }
    }

    /// <summary>Direction from the eye to a point; the camera's forward when the point is at the eye (standing in rope).</summary>
    private System.Numerics.Vector3 AimTowards(System.Numerics.Vector3 point, Vector3 fallback)
    {
        var d = point - Entity.Eye;
        return d.LengthSquared() > 1e-4f ? System.Numerics.Vector3.Normalize(d) : System.Numerics.Vector3.Normalize(ToCore(fallback));
    }

    private void UpdateTarget()
    {
        var cam = _player.Rig.Camera;
        var origin = cam.GlobalPosition;
        var dir = _player.Rig.Forward;
        var held = Entity.State.Inventory.Held;
        // The same reach as the use itself, so the Reach stat (Builder Potion) extends the highlight too.
        float reach = ItemUseSystem.ReachMeters(Entity, held.IsEmpty ? null : held.Item);
        Entity.ViewDirection = System.Numerics.Vector3.Normalize(ToCore(dir));
        float camOffset = origin.DistanceTo(ToGodot(Entity.Eye));
        var hit = Targeting.Cast(Game.Session.World, ToCore(origin), ToCore(dir), reach + camOffset + 2f);
        if (hit != null)
        {
            var point = hit.HitCells * Strata.Core.Common.Units.CellSize;
            float fromEye = System.Numerics.Vector3.Distance(point, Entity.Eye);
            Target = hit with { Distance = fromEye };
            InReach = fromEye <= reach;
            Entity.Aim = AimTowards(point, dir);
            Entity.AimDistance = fromEye;
        }
        else
        {
            Target = null;
            InReach = false;
            var far = ToCore(origin + dir * 40f);
            Entity.Aim = AimTowards(far, dir);
            Entity.AimDistance = System.Numerics.Vector3.Distance(far, Entity.Eye);
        }
        if (DigDownTarget(dir) is { } below)
        {
            Target = below;
            InReach = true;
        }
        UpdateTalkTarget(origin, dir);
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
