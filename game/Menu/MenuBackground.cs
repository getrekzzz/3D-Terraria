using Godot;

namespace Strata.Game.Menu;

/// <summary>
/// The title-screen backdrop: a sky that slowly cycles through day and night (sun, moon, stars), drifting clouds and
/// three layers of hills with tree silhouettes scrolling at different speeds.
/// </summary>
public sealed partial class MenuBackground : Control
{
    private const float DayLength = 90f;
    private float _time = DayLength * 0.3f;
    private float _scroll;

    /// <summary>How light the sky is now: 1 by day, 0 at night (the logo dims with it).</summary>
    public float Daylight { get; private set; } = 1f;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _scroll += (float)delta * 12f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        float phase = (_time % DayLength) / DayLength;           // 0 dawn, 0.25 noon, 0.5 dusk, 0.75 midnight
        float sunHeight = Mathf.Sin(phase * Mathf.Tau);
        float day = Mathf.SmoothStep(-0.15f, 0.25f, sunHeight);
        Daylight = day;
        float dusk = Mathf.Clamp(1f - Mathf.Abs(sunHeight) * 4f, 0f, 1f);

        var top = new Color(0.02f, 0.03f, 0.09f).Lerp(new Color(0.22f, 0.45f, 0.85f), day);
        var horizon = new Color(0.06f, 0.08f, 0.18f).Lerp(new Color(0.62f, 0.8f, 0.97f), day).Lerp(new Color(0.98f, 0.55f, 0.3f), dusk * 0.7f);
        const int bands = 24;
        for (int i = 0; i < bands; i++)
        {
            float t0 = i / (float)bands, t1 = (i + 1) / (float)bands;
            DrawRect(new Rect2(0, size.Y * t0, size.X, size.Y * (t1 - t0) + 1), top.Lerp(horizon, t0 * t0));
        }

        // Stars fade in at night.
        if (day < 0.8f)
        {
            var rng = new RandomNumberGenerator { Seed = 7 };
            for (int i = 0; i < 160; i++)
            {
                var p = new Vector2(rng.Randf() * size.X, rng.Randf() * size.Y * 0.65f);
                float twinkle = 0.6f + 0.4f * Mathf.Sin(_time * (1f + rng.Randf() * 3f) + i);
                DrawCircle(p, 1f + rng.Randf() * 1.2f, new Color(1, 1, 1, (1f - day) * twinkle * 0.9f));
            }
        }

        // Sun and moon travel across the sky opposite each other.
        var arcCenter = new Vector2(size.X * 0.5f, size.Y * 0.95f);
        float radius = size.Y * 0.8f;
        var sun = arcCenter + new Vector2(-Mathf.Cos(phase * Mathf.Tau), -Mathf.Sin(phase * Mathf.Tau)) * new Vector2(size.X * 0.45f, radius);
        var moon = arcCenter + new Vector2(Mathf.Cos(phase * Mathf.Tau), Mathf.Sin(phase * Mathf.Tau)) * new Vector2(size.X * 0.45f, radius);
        DrawCircle(sun, 46, new Color(1f, 0.9f, 0.55f, 0.25f));
        DrawCircle(sun, 30, new Color(1f, 0.95f, 0.7f));
        DrawCircle(moon, 24, new Color(0.9f, 0.93f, 1f));
        DrawCircle(moon + new Vector2(9, -5), 20, top.Lerp(horizon, 0.3f));

        // Clouds drift to the right.
        var cloud = new Color(1f, 1f, 1f, 0.75f).Lerp(new Color(0.35f, 0.38f, 0.5f, 0.6f), 1f - day);
        for (int i = 0; i < 7; i++)
        {
            float x = Mathf.PosMod(i * 310f + _scroll * (0.6f + i * 0.07f), size.X + 400f) - 200f;
            float y = size.Y * (0.12f + 0.06f * (i % 3));
            for (int k = 0; k < 5; k++) DrawCircle(new Vector2(x + k * 34f, y + (k % 2) * 8f), 26f + (k % 3) * 8f, cloud);
        }

        Hills(size, _scroll * 0.25f, size.Y * 0.62f, 60f, 0.004f, new Color(0.3f, 0.45f, 0.4f).Lerp(new Color(0.06f, 0.08f, 0.12f), 1f - day), 9f);
        Hills(size, _scroll * 0.55f, size.Y * 0.72f, 45f, 0.006f, new Color(0.2f, 0.4f, 0.22f).Lerp(new Color(0.04f, 0.07f, 0.08f), 1f - day), 13f);
        Hills(size, _scroll * 1.1f, size.Y * 0.84f, 30f, 0.009f, new Color(0.14f, 0.3f, 0.14f).Lerp(new Color(0.02f, 0.04f, 0.05f), 1f - day), 18f);
    }

    /// <summary>A rolling hill layer with trees on it.</summary>
    private void Hills(Vector2 size, float offset, float baseY, float amplitude, float frequency, Color color, float treeSize)
    {
        const int steps = 64;
        var points = new Vector2[steps + 3];
        for (int i = 0; i <= steps; i++)
        {
            float x = size.X * i / steps;
            points[i] = new Vector2(x, Height(x + offset, baseY, amplitude, frequency));
        }
        points[steps + 1] = new Vector2(size.X, size.Y);
        points[steps + 2] = new Vector2(0, size.Y);
        DrawColoredPolygon(points, color);
        float spacing = treeSize * 5f;
        float start = -Mathf.PosMod(offset, spacing);
        for (float x = start; x < size.X + spacing; x += spacing)
        {
            float wx = x + offset;
            float hash = Mathf.PosMod(Mathf.Sin(Mathf.Floor(wx / spacing) * 12.9898f) * 43758.55f, 1f);
            if (hash < 0.45f) continue;
            float y = Height(wx, baseY, amplitude, frequency);
            float h = treeSize * (2.2f + hash * 1.6f);
            DrawRect(new Rect2(x - treeSize * 0.12f, y - h * 0.6f, treeSize * 0.24f, h * 0.6f + 2), color.Darkened(0.2f));
            DrawCircle(new Vector2(x, y - h * 0.7f), treeSize * (0.7f + hash * 0.3f), color.Darkened(0.1f));
            DrawCircle(new Vector2(x - treeSize * 0.4f, y - h * 0.55f), treeSize * 0.55f, color.Darkened(0.1f));
            DrawCircle(new Vector2(x + treeSize * 0.4f, y - h * 0.55f), treeSize * 0.55f, color.Darkened(0.1f));
        }
    }

    private static float Height(float x, float baseY, float amplitude, float frequency) =>
        baseY - amplitude * (0.6f * Mathf.Sin(x * frequency) + 0.4f * Mathf.Sin(x * frequency * 2.3f + 1.7f));
}
