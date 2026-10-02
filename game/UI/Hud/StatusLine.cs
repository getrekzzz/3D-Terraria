using Godot;

namespace Strata.Game.UI;

/// <summary>
/// One line under the crosshair (the StatusLine region) for what the game tells the player in passing: refusals ("Too
/// far away", "It's locked."), crafting results and housing notices. A message shows for <see cref="Hold"/> seconds and
/// fades over <see cref="Fade"/>; a newer one replaces it. In between it shows the build-shape hint while a shaped
/// block is held.
/// </summary>
public sealed partial class StatusLine : OutlinedText
{
    public const double Hold = 2.0;
    public const double Fade = 0.5;

    private static readonly Color HintColor = new(UiTheme.Muted, 0.85f);

    private double _left;
    private string _hint = "";

    public StatusLine()
    {
        Name = "StatusLine";
    }

    /// <summary>The message on show, or "" (the <c>hud.status</c> probe).</summary>
    public string Message => _left > 0 ? Text : "";

    public void Show(string text, Color color)
    {
        Text = text;
        SelfModulate = color;
        _left = Hold + Fade;
    }

    /// <summary>The build-shape hint shown while no message is (empty for none).</summary>
    public string Hint
    {
        set
        {
            if (_hint == value) return;
            _hint = value;
            if (_left <= 0) ShowHint();
        }
    }

    public override void _Process(double delta)
    {
        if (_left <= 0) return;
        _left -= delta;
        if (_left <= 0)
        {
            ShowHint();
            return;
        }
        float alpha = (float)Mathf.Clamp(_left / Fade, 0, 1);
        if (!Mathf.IsEqualApprox(SelfModulate.A, alpha)) SelfModulate = new Color(SelfModulate, alpha);
    }

    private void ShowHint()
    {
        Text = _hint;
        SelfModulate = HintColor;
    }
}
