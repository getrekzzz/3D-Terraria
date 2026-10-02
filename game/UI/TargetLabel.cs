using Godot;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Voxels;

namespace Strata.Game.UI;

/// <summary>
/// The name under the crosshair. A creature, boss or town resident shows at any distance with its life ("Blue Slime
/// 25/25", "Eater of Worlds 3920/4000", "Andrew the Guide 250/250"), tinted green, then yellow, then red as it is hurt
/// (bosses in the boss bar's red); otherwise the block or object within reach is named. Hidden when the "Show target
/// name" setting is off. The text is rebuilt only when what it shows changes.
/// </summary>
public sealed partial class TargetLabel : Label
{
    private static readonly Color Healthy = new(0.5f, 1f, 0.45f);
    private static readonly Color Hurt = new(1f, 0.92f, 0.35f);
    private static readonly Color Dying = new(1f, 0.35f, 0.3f);
    private static readonly Color Boss = new Color(0.75f, 0.12f, 0.2f).Lightened(0.25f); // the boss bar, legible as text

    private UiContext _ui = null!;
    private object? _shown;
    private string? _sign;
    private int _life = -1, _max = -1;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "TargetLabel";
        HorizontalAlignment = HorizontalAlignment.Center;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.Center);
        GrowHorizontal = GrowDirection.Both;
        OffsetTop = 16;
        AddThemeFontSizeOverride("font_size", 14);
        AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        AddThemeConstantOverride("outline_size", 4);
    }

    public override void _Process(double delta)
    {
        var interaction = _ui.Interaction;
        if (!Settings.UserSettings.Current.ShowTargetName) Show(null, 0, 0);
        else if (interaction.Hovered is { } pick)
        {
            object who = (object?)pick.Npc ?? pick.Resident!;
            int life = pick.Npc?.Life ?? pick.Resident!.Life;
            int max = pick.Npc?.MaxLife ?? pick.Resident!.Def.Life;
            if (!Show(who, life, max)) return;
            var (name, l, m) = EntityPicking.Describe(pick);
            Text = $"{name} {l}/{m}";
            AddThemeColorOverride("font_color", pick.Npc is { IsBoss: true } ? Boss : LifeColor(m > 0 ? (float)l / m : 0f));
        }
        else if (interaction.InReach && interaction.Target is { } t)
        {
            object? what = t.Object != null ? t.Object.Def : t.Block is 0 or VoxelWorld.Unknown ? null : _ui.Game.Session.Content.Blocks[t.Block];
            // A sign shows what it says under its name (the text instance changes when it is edited).
            string? sign = t.Object?.SignText;
            if ((!Show(what, 0, 0) && ReferenceEquals(sign, _sign)) || what == null) return;
            _sign = sign;
            Text = t.Object?.DisplayName ?? _ui.Game.Session.Content.Blocks[t.Block].DisplayName;
            if (sign != null) Text += "\n\"" + sign + "\"";
            AddThemeColorOverride("font_color", UiTheme.Text);
        }
        else Show(null, 0, 0);
    }

    /// <summary>Records what is shown; false when it is unchanged. Showing nothing clears the text at once.</summary>
    private bool Show(object? what, int life, int max)
    {
        if (what == _shown && life == _life && max == _max) return false;
        _shown = what;
        _life = life;
        _max = max;
        if (what == null) Text = "";
        return true;
    }

    /// <summary>Green at full life, yellow at half, red near death.</summary>
    private static Color LifeColor(float fraction)
    {
        fraction = Mathf.Clamp(fraction, 0f, 1f);
        return fraction >= 0.5f ? Hurt.Lerp(Healthy, (fraction - 0.5f) * 2f) : Dying.Lerp(Hurt, fraction * 2f);
    }
}
