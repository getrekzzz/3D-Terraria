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
    /// The logical viewport the layout is computed for: the visible rectangle (the window over the UI scale). A headless
    /// run has no real window (its viewport is a token 64 px), so it lays out for the project's window size over the
    /// UI scale, as that window would.
    /// </summary>
    public static Vector2 LogicalSize(Viewport viewport)
    {
        if (DisplayServer.GetName() != "headless") return viewport.GetVisibleRect().Size;
        _projectWindow ??= new Vector2((int)ProjectSettings.GetSetting("display/window/size/viewport_width"),
            (int)ProjectSettings.GetSetting("display/window/size/viewport_height"));
        float scale = viewport is Window w ? w.ContentScaleFactor : 1f;
        return _projectWindow.Value / Mathf.Max(scale, 0.01f);
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
    /// is added to <paramref name="pairs"/> as "A (rect) × B (rect)" when given.
    /// </summary>
    public static int CountOverlaps(SceneTree tree, List<string>? pairs = null)
    {
        var shown = new List<(Control Control, UiRect Rect)>();
        foreach (var node in tree.GetNodesInGroup(Group))
        {
            if (node is not Control { } c || !c.IsVisibleInTree()) continue;
            var g = c.GetGlobalRect();
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
}
