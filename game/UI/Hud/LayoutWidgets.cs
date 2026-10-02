using System.Collections.Generic;
using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The controls placed in a <see cref="UiLayout"/> region (the HUD's widgets, the minimap, the F3 overlay): each joins
/// the <see cref="Group"/> so the <c>ui.overlaps</c> probe can check the live tree, where real text and sprites may
/// outgrow the modelled sizes.
/// </summary>
public static class LayoutWidgets
{
    public const string Group = "ui_layout";

    private static Vector2? _projectWindow;

    /// <summary>
    /// The window size a headless run lays out for (the <c>uiwindow</c> command), or null for the project's window size;
    /// a real window always uses its own size.
    /// </summary>
    public static Vector2I? SimulatedWindow { get; set; }

    /// <summary>The window a headless run pretends to have: <see cref="SimulatedWindow"/>, else the project's window size.</summary>
    public static Vector2 HeadlessWindow => SimulatedWindow is { } sim ? new Vector2(sim.X, sim.Y)
        : _projectWindow ??= new Vector2((int)ProjectSettings.GetSetting("display/window/size/viewport_width"),
            (int)ProjectSettings.GetSetting("display/window/size/viewport_height"));

    /// <summary>
    /// The logical viewport the layout is computed for: the visible rectangle (the window over the UI scale). A headless
    /// run has no real window (its viewport is a token 64 px), so it lays out for <see cref="HeadlessWindow"/> over the
    /// UI scale, as that window would.
    /// </summary>
    public static Vector2 LogicalSize(Viewport viewport)
    {
        if (DisplayServer.GetName() != "headless") return viewport.GetVisibleRect().Size;
        float scale = viewport is Window w ? w.ContentScaleFactor : 1f;
        return HeadlessWindow / Mathf.Max(scale, 0.01f);
    }

    /// <summary>Puts <paramref name="control"/> in the overlap check.</summary>
    public static T Register<T>(T control) where T : Control
    {
        control.AddToGroup(Group);
        return control;
    }

    /// <summary>
    /// Moves <paramref name="control"/> onto <paramref name="region"/>'s rectangle; false (and the control untouched) when
    /// the layout does not show that region.
    /// </summary>
    public static bool Place(Control control, UiLayoutResult layout, string region)
    {
        if (!layout.TryGet(region, out var r)) return false;
        control.Position = new Vector2(r.X, r.Y);
        control.Size = new Vector2(r.W, r.H);
        return true;
    }

    /// <summary>Visible registered controls whose screen rectangles share area with <paramref name="control"/>'s.</summary>
    public static int CountOverlapsWith(SceneTree tree, Control control)
    {
        var g = control.GetGlobalRect();
        var rect = new UiRect(g.Position.X, g.Position.Y, g.Size.X, g.Size.Y);
        int count = 0;
        foreach (var node in tree.GetNodesInGroup(Group))
        {
            if (node == control || node is not Control { } c || !c.IsVisibleInTree()) continue;
            var o = c.GetGlobalRect();
            if (rect.Intersects(new UiRect(o.Position.X, o.Position.Y, o.Size.X, o.Size.Y))) count++;
        }
        return count;
    }

    /// <summary>
    /// Pairs of visible registered controls whose screen rectangles share area (touching edges do not count); each pair
    /// is added to <paramref name="pairs"/> as "A (rect) × B (rect)" when given. With <paramref name="content"/> each
    /// control counts with everything drawn inside it (<see cref="ContentRect"/>: slots, captions, a rename field that
    /// grew past its region), the <c>layout.overlaps</c> probe; otherwise only its region (<c>ui.overlaps</c>).
    /// </summary>
    public static int CountOverlaps(SceneTree tree, List<string>? pairs = null, bool content = false)
    {
        var shown = new List<(Control Control, UiRect Rect)>();
        foreach (var node in tree.GetNodesInGroup(Group))
        {
            if (node is not Control { } c || !c.IsVisibleInTree()) continue;
            var g = content ? ContentRect(c) : c.GetGlobalRect();
            shown.Add((c, new UiRect(g.Position.X, g.Position.Y, g.Size.X, g.Size.Y)));
        }
        int count = 0;
        for (int i = 0; i < shown.Count; i++)
            for (int j = i + 1; j < shown.Count; j++)
            {
                if (!shown[i].Rect.Intersects(shown[j].Rect)) continue;
                count++;
                pairs?.Add($"{shown[i].Control.Name} {shown[i].Rect} × {shown[j].Control.Name} {shown[j].Rect}");
            }
        return count;
    }

    /// <summary>
    /// The screen rectangle <paramref name="control"/> and its visible descendant controls cover (a probe's reading,
    /// not for frames): what the player sees of the widget, even where a child outgrew the region. A clipping child
    /// (a scroll list) counts with its own rectangle only.
    /// </summary>
    public static Rect2 ContentRect(Control control)
    {
        var rect = control.GetGlobalRect();
        Grow(control, ref rect);
        return rect;
    }

    private static void Grow(Node node, ref Rect2 rect)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is not Control { } c || !c.Visible || c.TopLevel) continue;
            var g = c.GetGlobalRect();
            if (g.Size.X > 0 && g.Size.Y > 0) rect = rect.Merge(g);
            if (!c.ClipContents) Grow(c, ref rect); // a scroll list shows only its own rectangle of its rows
        }
    }
}
