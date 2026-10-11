namespace Strata.Core.Presentation;

/// <summary>A direction the controller's cursor steps in.</summary>
public enum NavDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>A control's screen rectangle (pixels, y down) for <see cref="FocusNavigation"/>.</summary>
public readonly record struct NavRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public float CenterX => X + Width / 2;
    public float CenterY => Y + Height / 2;
}

/// <summary>
/// Moving a controller cursor across slot grids, buttons and panels laid out anywhere on screen (Terraria's gamepad
/// inventory cursor): from the current control, the next one in a direction is the nearest whose centre lies that way,
/// preferring controls in the same row or column (the "beam") so grids step cell by cell, and jumping to the nearest
/// control of another panel when the row ends. Works on plain rectangles (logical pixels), so any screen can use it.
/// </summary>
public static class FocusNavigation
{
    /// <summary>How much a pixel off the row or column (beyond overlapping it) counts against a candidate.</summary>
    private const float OffBeamWeight = 4f;

    /// <summary>A little weight on the centres' misalignment, so of two candidates in the beam the aligned one wins.</summary>
    private const float AlignWeight = 0.1f;

    /// <summary>
    /// Pixels a candidate outside the beam is set back: one in the same row or column wins unless it is this much
    /// farther away (a grid's row continues before the cursor drops to a nearer control of another row).
    /// </summary>
    private const float OffBeamPenalty = 200f;

    /// <summary>
    /// The index of the control to step to from <paramref name="from"/> in <paramref name="dir"/>, or -1 when there is
    /// none that way. <paramref name="skip"/> (the current control's index) is never chosen.
    /// </summary>
    public static int Next(IReadOnlyList<NavRect> rects, NavRect from, NavDirection dir, int skip = -1)
    {
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < rects.Count; i++)
        {
            if (i == skip) continue;
            var c = rects[i];
            float primary, crossGap, align;
            switch (dir)
            {
                case NavDirection.Right:
                    if (c.CenterX <= from.CenterX + 0.5f || c.Right <= from.Right) continue;
                    primary = MathF.Max(0, c.X - from.Right);
                    crossGap = Gap(c.Y, c.Bottom, from.Y, from.Bottom);
                    align = MathF.Abs(c.CenterY - from.CenterY);
                    break;
                case NavDirection.Left:
                    if (c.CenterX >= from.CenterX - 0.5f || c.X >= from.X) continue;
                    primary = MathF.Max(0, from.X - c.Right);
                    crossGap = Gap(c.Y, c.Bottom, from.Y, from.Bottom);
                    align = MathF.Abs(c.CenterY - from.CenterY);
                    break;
                case NavDirection.Down:
                    if (c.CenterY <= from.CenterY + 0.5f || c.Bottom <= from.Bottom) continue;
                    primary = MathF.Max(0, c.Y - from.Bottom);
                    crossGap = Gap(c.X, c.Right, from.X, from.Right);
                    align = MathF.Abs(c.CenterX - from.CenterX);
                    break;
                default:
                    if (c.CenterY >= from.CenterY - 0.5f || c.Y >= from.Y) continue;
                    primary = MathF.Max(0, from.Y - c.Bottom);
                    crossGap = Gap(c.X, c.Right, from.X, from.Right);
                    align = MathF.Abs(c.CenterX - from.CenterX);
                    break;
            }
            float score = primary + crossGap * OffBeamWeight + align * AlignWeight + (crossGap > 0 ? OffBeamPenalty : 0);
            if (score >= bestScore) continue;
            bestScore = score;
            best = i;
        }
        return best;
    }

    /// <summary>The index of the control whose centre is nearest (<paramref name="x"/>, <paramref name="y"/>), or -1 for none.</summary>
    public static int Nearest(IReadOnlyList<NavRect> rects, float x, float y)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < rects.Count; i++)
        {
            float dx = rects[i].CenterX - x, dy = rects[i].CenterY - y, d = dx * dx + dy * dy;
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = i;
        }
        return best;
    }

    /// <summary>The distance between two intervals on an axis (0 when they overlap).</summary>
    private static float Gap(float aMin, float aMax, float bMin, float bMax) => MathF.Max(0, MathF.Max(aMin - bMax, bMin - aMax));
}
