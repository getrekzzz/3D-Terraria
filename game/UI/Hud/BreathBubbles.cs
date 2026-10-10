using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// Breath as Terraria's 10 bubbles on the Breath region above the crosshair, shown only while it is below the maximum:
/// bubble <c>i</c> stays while breath is above i tenths and the last one shrinks as it runs out, so they pop one by one
/// underwater. Out of breath (drowning), the empty row flashes red. Redrawn only when breath changes.
/// </summary>
public sealed partial class BreathBubbles : Control
{
    private static readonly Color Drowning = Color.Color8(255, 80, 90);

    private readonly Texture2D _bubble = UiSprites.Get("bubble");
    private VitalRow _row;
    private int _breath = -1, _max = 1;
    private bool _placed;
    private double _time;

    public BreathBubbles()
    {
        Name = "Breath";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
    }

    /// <summary>Bubbles drawn (the <c>hud.bubbles</c> probe): 0 at full breath, when the row is hidden.</summary>
    public int Shown { get; private set; }

    /// <summary>The layout shows the Breath region (set by the HUD after each layout).</summary>
    public bool Placed
    {
        get => _placed;
        set
        {
            _placed = value;
            Visible = value && _breath >= 0 && _breath < _max;
        }
    }

    public void Set(int breath, int max, double delta)
    {
        if (breath != _breath || max != _max)
        {
            _breath = breath;
            _max = max;
            _row = VitalsDisplay.Bubbles(breath, max);
            Shown = 0;
            if (breath < max)
                for (int i = 0; i < _row.Count; i++)
                    if (_row.Fill(i) > 0) Shown++;
            Visible = _placed && breath < max;
            QueueRedraw();
        }
        if (breath > 0 || !Visible)
        {
            SelfModulate = Colors.White;
            return;
        }
        _time += delta;
        SelfModulate = (int)(_time * 4) % 2 == 0 ? Drowning : Drowning.Darkened(0.45f);
    }

    public override void _Draw()
    {
        float pitch = Size.X / VitalsDisplay.BubbleCount;
        for (int i = 0; i < _row.Count; i++)
        {
            float fill = _row.Fill(i);
            var centre = new Vector2((i + 0.5f) * pitch, Size.Y / 2);
            if (_breath <= 0) LifeHearts.DrawIcon(this, _bubble, centre, 0.6f, new Color(1, 1, 1, 0.7f)); // drowning: the empty row
            else if (fill > 0) LifeHearts.DrawIcon(this, _bubble, centre, Mathf.Max(0.3f, fill), Colors.White);
        }
    }
}
