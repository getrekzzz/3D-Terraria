namespace Strata.Core.Presentation;

/// <summary>
/// Where the HUD's accessibility overlays go on a computed <see cref="UiLayout"/> (X-12), clear of every region it
/// placed: the caption lines just above the chat log, anchored to its left edge and cut down (fewer lines, narrower)
/// to fit; the autosave icon in the bottom-right corner, or the nearest free spot to it (above the event bar or the
/// inventory's Settings button). Recomputed with the layout, never per frame.
/// </summary>
public static class HudOverlays
{
    public const float CaptionLine = 22;
    public const float CaptionsWidth = 360;
    public const float CaptionsMinWidth = 200;
    public const float SaveIcon = 28;

    /// <summary>The caption rows' rectangle for up to <paramref name="lines"/> lines; null without room (or a chat log).</summary>
    public static UiRect? CaptionsRect(UiLayoutResult layout, int lines)
    {
        if (!layout.TryGet(UiRegion.Chat, out var chat) || lines <= 0) return null;
        var pass = PassOf(layout);
        float h = lines * CaptionLine;
        float w = MathF.Min(CaptionsWidth, MathF.Max(chat.W, CaptionsMinWidth));
        var full = new UiRect(chat.X, chat.Y - UiLayout.Gap - h, w, h);
        return pass.Shrink(full, fromLeft: true, fromTop: false, CaptionsMinWidth, CaptionLine);
    }

    /// <summary>The autosave icon's square, clear of the layout and of <paramref name="captions"/>; null when nothing fits.</summary>
    public static UiRect? SaveIconRect(UiLayoutResult layout, UiRect? captions = null)
    {
        var pass = PassOf(layout);
        if (captions is { } c) pass.Put("captions", c);
        var corner = new UiRect(layout.Width - UiLayout.Margin - SaveIcon, layout.Height - UiLayout.Margin - SaveIcon, SaveIcon, SaveIcon);
        return pass.Search(corner);
    }

    private static UiLayoutPass PassOf(UiLayoutResult layout)
    {
        var pass = new UiLayoutPass(layout.Width, layout.Height);
        foreach (var (region, rect) in layout.Rects) pass.Put(region, rect);
        return pass;
    }
}

/// <summary>
/// The autosave icon's timing (X-12): it fades in while a background save runs, stays at least
/// <see cref="MinSeconds"/> so a quick save still registers, then fades out; it spins at a steady rate while shown.
/// </summary>
public sealed class SaveIndicator
{
    public const float MinSeconds = 1.2f;
    public const float FadeInSeconds = 0.15f;
    public const float FadeOutSeconds = 0.5f;
    public const float TurnsPerSecond = 1.2f;

    private double _shownFor;
    private bool _active;

    public float Alpha { get; private set; }

    /// <summary>The spinner's rotation in radians, 0..2π.</summary>
    public float Angle { get; private set; }

    public bool Visible => Alpha > 0f;

    /// <param name="saving">A background save is being written now.</param>
    public void Update(bool saving, double dt)
    {
        if (saving && !_active)
        {
            _active = true;
            _shownFor = 0;
        }
        if (!_active) return;
        _shownFor += dt;
        if (saving || _shownFor < MinSeconds)
        {
            Alpha = MathF.Min(1f, Alpha + (float)(dt / FadeInSeconds));
        }
        else
        {
            Alpha = MathF.Max(0f, Alpha - (float)(dt / FadeOutSeconds));
            if (Alpha <= 0f) _active = false;
        }
        Angle = (float)((Angle + dt * TurnsPerSecond * Math.Tau) % Math.Tau);
    }
}
