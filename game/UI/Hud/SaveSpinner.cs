using Godot;
using Strata.Core.Presentation;
using Strata.Core.Session;

namespace Strata.Game.UI;

/// <summary>
/// The autosave indicator (X-12): a small spinning ring in the HUD's bottom-right corner (or the nearest free spot,
/// <see cref="HudOverlays.SaveIconRect"/>) while a background save (<see cref="GameWorld.BeginAutosave"/>: the two-minute
/// autosave or <c>save async</c>) is being written, shown at least a moment and faded out after
/// (<see cref="SaveIndicator"/>). Chunk unloads saved by the streamer do not show it. Draws only while visible.
/// </summary>
public sealed partial class SaveSpinner : Control
{
    private readonly GameWorld _game;
    private readonly SaveIndicator _state = new();
    private bool _placed;

    public SaveSpinner(GameWorld game)
    {
        _game = game;
        Name = "SaveSpinner";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    /// <summary>Shown now (the <c>ui.saving</c> probe).</summary>
    public bool Showing => Visible;

    /// <summary>Times it appeared (the <c>ui.save_icons</c> probe).</summary>
    public int Shown { get; private set; }

    /// <summary>Takes <paramref name="rect"/> (from the layout); hidden without one.</summary>
    public void Place(UiRect? rect)
    {
        _placed = rect.HasValue;
        if (rect is not { } r) return;
        Position = new Vector2(r.X, r.Y);
        Size = new Vector2(r.W, r.H);
    }

    public override void _Process(double delta)
    {
        _state.Update(_game.AutosaveInFlight, delta);
        bool show = _placed && _state.Visible;
        if (show && !Visible) Shown++;
        if (Visible != show) Visible = show;
        if (!show) return;
        Modulate = new Color(1f, 1f, 1f, _state.Alpha);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var centre = Size / 2;
        float radius = Mathf.Min(Size.X, Size.Y) / 2 - 4f;
        float start = _state.Angle;
        // A faint full ring with a bright three-quarter arc chasing round it, both outlined like the UI's text.
        DrawArc(centre, radius, 0f, Mathf.Tau, 32, UiFonts.OutlineColor, 6f, true);
        DrawArc(centre, radius, 0f, Mathf.Tau, 32, new Color(1f, 1f, 1f, 0.25f), 3f, true);
        DrawArc(centre, radius, start, start + Mathf.Tau * 0.7f, 24, UiTheme.Gold, 3f, true);
    }
}
