using Godot;
using Strata.Core.Session;

namespace Strata.Game.UI;

/// <summary>
/// Terraria's death screen over the whole HUD: the view darkens (to 55 % over a second) under a large red "You were
/// slain..." with the cause of death below it ("Wanderer was slain by a Zombie.") and the respawn countdown. A hardcore
/// character reads "Name has perished..." and "Hardcore characters do not return." instead. It fades away on respawn.
/// The cause also goes to the chat log (wired in Hud.Messages).
/// </summary>
public sealed partial class DeathScreen : Control
{
    private const float Dim = 0.55f;
    private const double FadeIn = 1.0, FadeOut = 0.4;
    private const int LineSize = 20;

    private readonly ColorRect _shade;
    private readonly Label _title, _cause, _countdown;
    private bool _dead;
    private int _seconds = -1;
    private float _alpha;

    public DeathScreen()
    {
        Name = "DeathScreen";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _shade = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = MouseFilterEnum.Ignore };
        _shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_shade);
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 10);
        AddChild(column);
        column.AddChild(_title = Line(UiFonts.Death, UiTheme.Death, UiFonts.OutlineLarge));
        column.AddChild(_cause = Line(LineSize, UiTheme.Text, UiFonts.Outline));
        column.AddChild(_countdown = Line(LineSize, UiTheme.Text, UiFonts.Outline));
    }

    /// <summary>The screen is up: the player is dead (the <c>ui.death_dim</c> probe; not while it fades away).</summary>
    public bool Shown => _dead;

    /// <summary>The cause of the latest death, under the title.</summary>
    public string Cause
    {
        get => _cause.Text;
        set => _cause.Text = value;
    }

    /// <summary>Follows the player's life each frame: up on death, counting down, away on respawn.</summary>
    public void Refresh(PlayerEntity player, double delta)
    {
        bool dead = !player.Alive;
        if (dead && !_dead)
        {
            _seconds = -1;
            Visible = true;
        }
        _dead = dead;
        if (dead)
        {
            int seconds = player.Lost ? -2 : Mathf.CeilToInt(player.RespawnTicks / 60f); // texts change only with it
            if (seconds != _seconds)
            {
                _seconds = seconds;
                _title.Text = player.Lost ? $"{player.State.Name} has perished..." : "You were slain...";
                _countdown.Text = player.Lost ? "Hardcore characters do not return." : seconds > 0 ? $"Respawning in {seconds}" : "";
            }
        }
        if (!Visible) return;
        _alpha = Mathf.MoveToward(_alpha, dead ? 1f : 0f, (float)(delta / (dead ? FadeIn : FadeOut)));
        _shade.Color = new Color(0, 0, 0, Dim * _alpha);
        Modulate = new Color(1, 1, 1, dead ? 1f : _alpha); // the words show at once; everything fades out together
        if (!dead && _alpha <= 0f)
        {
            Visible = false;
            _cause.Text = "";
        }
    }

    private static Label Line(int size, Color color, int outline)
    {
        var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", UiFonts.OutlineColor);
        label.AddThemeConstantOverride("outline_size", outline);
        return label;
    }
}
