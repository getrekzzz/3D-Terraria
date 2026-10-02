using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.Player;

public sealed partial class InteractionController
{
    private static readonly StringName SmartCursorAction = "smart_cursor";

    /// <summary>Cosine of 0.5°: a smaller turn of the aim keeps the last smart search.</summary>
    private const float SmartAimCos = 0.99996192f;

    /// <summary>A smart search is reused at most this many ticks (nearby edits are also caught by the world events).</summary>
    private const int SmartMaxAgeTicks = 15;

    private bool _smartKeyDown;
    private bool _smartDirty = true;
    private int _smartAge;
    private ItemDef? _smartItem;
    private System.Numerics.Vector3 _smartEye, _smartAim;
    private Target? _smartFound;

    /// <summary>True when <see cref="Target"/> is the Smart Cursor's pick rather than the cell under the crosshair.</summary>
    public bool SmartTarget { get; private set; }

    /// <summary>Smart Cursor was switched on (true) or off with its key; the HUD says so on the status line.</summary>
    public event System.Action<bool>? SmartCursorToggled;

    /// <summary>
    /// Flips Smart Cursor and saves the choice (Settings → Gameplay shows the same switch; scripted runs keep it in
    /// memory). The pointer shows its smart variant while it is on.
    /// </summary>
    public void ToggleSmartCursor()
    {
        var settings = Settings.UserSettings.Current;
        settings.SmartCursor = !settings.SmartCursor;
        if (!Bootstrap.GameRoot.Automated) settings.Save();
        UI.UiCursor.Apply(settings);
        _smartDirty = true;
        SmartCursorToggled?.Invoke(settings.SmartCursor);
    }

    /// <summary>The Smart Cursor key, polled like the hotbar keys so smoke scripts' pressed actions count too.</summary>
    private void PollSmartCursorKey()
    {
        bool down = Input.IsActionPressed(SmartCursorAction);
        if (Enabled && !UI.InputGate.HotkeysBlocked && ((down && !_smartKeyDown) || Input.IsActionJustPressed(SmartCursorAction))) ToggleSmartCursor();
        _smartKeyDown = down;
    }

    /// <summary>
    /// With Smart Cursor on and a pickaxe, axe or hammer held, the target becomes <see cref="SmartTargeting.Find"/>'s
    /// pick along the aim (always within reach). An object the crosshair itself rests on within reach (a pot, a chest, a
    /// tree) keeps the plain target, so the snap never steals an exact aim. The search reruns only when the item, the
    /// eye (5 cm) or the aim (0.5°) moved, the world was edited, or the last result went stale.
    /// </summary>
    private void ApplySmartCursor(ItemDef? item, float reach)
    {
        SmartTarget = false;
        if (!Settings.UserSettings.Current.SmartCursor || item == null) return;
        var info = Game.Uses[item];
        if (!info.IsTool) return;
        if (InReach && Target?.Object is { Def.Decor: false }) return;
        var eye = Entity.Eye;
        var aim = Entity.Aim;
        bool fresh = _smartDirty || item != _smartItem || ++_smartAge > SmartMaxAgeTicks
            || System.Numerics.Vector3.DistanceSquared(eye, _smartEye) > 0.0025f
            || System.Numerics.Vector3.Dot(aim, _smartAim) < SmartAimCos
            || Stale(_smartFound);
        if (fresh)
        {
            _smartFound = SmartTargeting.Find(Game.Session.World, eye, aim, reach, info, item.PickPower, item.AxePower, item.HammerPower);
            _smartItem = item;
            _smartEye = eye;
            _smartAim = aim;
            _smartAge = 0;
            _smartDirty = false;
        }
        if (_smartFound == null) return;
        Target = _smartFound;
        InReach = true;
        SmartTarget = true;
    }

    /// <summary>The block under a cached pick changed, or its object is gone.</summary>
    private bool Stale(Target? found)
    {
        if (found == null) return false;
        if (found.Object is { } obj) return Game.Session.World.TileObjects.At(found.Cell) != obj;
        return Game.Session.World.GetBlock(found.Cell) != found.Block;
    }

    private void WatchWorldForSmartCursor(bool on)
    {
        var world = Game.Session.World;
        if (on)
        {
            world.Edited += OnWorldEdited;
            world.TileObjects.Added += OnObjectChanged;
            world.TileObjects.Removed += OnObjectChanged;
        }
        else
        {
            world.Edited -= OnWorldEdited;
            world.TileObjects.Added -= OnObjectChanged;
            world.TileObjects.Removed -= OnObjectChanged;
        }
    }

    private void OnWorldEdited(Strata.Core.Voxels.EditResult _) => _smartDirty = true;

    private void OnObjectChanged(TileObjectInstance _) => _smartDirty = true;

    public override void _ExitTree() => WatchWorldForSmartCursor(false);
}
