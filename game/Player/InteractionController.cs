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

    /// <summary>The creature (a worm segment resolves to its worm) or town resident under the crosshair at any distance, in front of the terrain (null if none).</summary>
    public EntityPick? Hovered { get; private set; }

    /// <summary>The town resident under the crosshair within talking range (null if none).</summary>
    public Strata.Core.Gameplay.Town.TownNpc? TalkTarget { get; private set; }

    /// <summary>The disguised creature (a Mimic's chest, the Lost Girl) under the crosshair, in front of any terrain hit.</summary>
    public Strata.Core.Gameplay.Entities.Npc? DisguiseTarget { get; private set; }

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
        if (interact && DisguiseTarget is { } poser && OpenDisguised(poser)) return;
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

    /// <summary>How far (m) past the eye's plane the crosshair point is looked for: terrain, or this far away.</summary>
    private const float ConvergenceMeters = 60f;

    /// <summary>Direction from the eye to a point; the view direction when the point is at the eye (standing in rope).</summary>
    private System.Numerics.Vector3 AimTowards(System.Numerics.Vector3 point, System.Numerics.Vector3 fallback)
    {
        var d = point - Entity.Eye;
        return d.LengthSquared() > 1e-4f ? System.Numerics.Vector3.Normalize(d) : fallback;
    }

    /// <summary>
    /// The crosshair: the view ray (from the camera along its forward) is walked from where it crosses the eye's plane,
    /// so nothing between the shoulder camera and the player is ever selected. A short cast with the held item's
    /// filter (tools stop at decor, axes at canopies) gives the target; a long one that only terrain, trunks and
    /// furniture stop gives the crosshair point the aim and shots converge on.
    /// </summary>
    private void UpdateTarget()
    {
        var rig = _player.Rig;
        var origin = rig.ViewOrigin;
        var dir = rig.Forward;
        var camera = ToCore(origin);
        var view = System.Numerics.Vector3.Normalize(ToCore(dir));
        var eye = Entity.Eye;
        var world = Game.Session.World;
        var held = Entity.State.Inventory.Held;
        var item = held.IsEmpty ? null : held.Item;
        // The same reach as the use itself, so the Reach stat (Builder Potion) extends the highlight too.
        float reach = ItemUseSystem.ReachMeters(Entity, item);
        var filter = ItemUseSystem.FilterFor(item == null ? null : Game.Uses[item]);
        Entity.ViewDirection = view;
        Target = Targeting.CastView(world, camera, view, eye, reach + 2f, filter, out float startT);
        InReach = Target != null && Target.Distance <= reach;
        float terrainT = startT + ConvergenceMeters;
        if (Targeting.CastView(world, camera, view, eye, ConvergenceMeters, TargetFilter.None, out _) is { } hit)
            terrainT = System.Numerics.Vector3.Dot(hit.HitCells * Strata.Core.Common.Units.CellSize - camera, view);
        // Creatures and residents on the ray in front of that terrain: walls hide them. The crosshair point moves onto
        // the nearer one (at least a metre past the eye's plane, so a body hugging the player does not swing the aim
        // sideways), and a resident within talking range is who right-click talks to.
        Hovered = EntityPicking.Pick(Game, camera, view, startT, terrainT);
        float aimT = Hovered is { } pick ? System.MathF.Min(terrainT, System.MathF.Max(pick.Distance, startT + 1f)) : terrainT;
        var point = camera + view * aimT;
        Entity.Aim = AimTowards(point, view);
        Entity.AimDistance = System.Numerics.Vector3.Distance(point, eye);
        TalkTarget = Hovered?.Resident is { } resident && System.Numerics.Vector3.Distance(resident.Center, Entity.Center) <= Game.Town.Config.TalkRange
            ? resident
            : null;
        if (DigDownTarget(dir) is { } below)
        {
            Target = below;
            InReach = true;
        }
        // Disguised creatures (mimics, Nymph) on the view ray in front of the terrain: right-click opens/greets them.
        DisguiseTarget = ObjectInteraction.DisguisedAlong(Game.Npcs.All, camera, view, terrainT + 0.5f);
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
}
