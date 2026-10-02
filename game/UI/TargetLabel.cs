using Godot;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Presentation;
using Strata.Core.Voxels;

namespace Strata.Game.UI;

/// <summary>
/// The name under the crosshair, drawn as Terraria draws hover names: small, in the UI font at hover size, beside the
/// cursor (below and right of the cross, slid left when it would leave the region). A creature, boss or town resident
/// shows at any distance with its life ("Blue Slime 25/25", "King Slime 2000/2000", "Andrew the Guide 250/250"),
/// residents and creatures in white, bosses in the purple of their awakening message; otherwise the block or object
/// within reach is named in white. Hidden when the "Show target name" setting is off. The text is rebuilt only when
/// what it shows changes. <see cref="Crosshair"/> holds it over its whole region.
/// </summary>
public sealed partial class TargetLabel : Control
{
    /// <summary>The name's top-left corner from the centre of the cross, as Terraria offsets mouse text from the cursor.</summary>
    public static readonly Vector2 Offset = new(12, 11);

    /// <summary>A boss's name: the purple of "King Slime has awoken!".</summary>
    public static readonly Color BossColor = Color.Color8(190, 110, 255);

    private UiContext _ui = null!;
    private object? _shown;
    private int _life = -1, _max = -1;
    private Color _color = UiTheme.Text;

    /// <summary>What the label shows ("King Slime 2000/2000", a block's name, or empty; the <c>hud.target_name</c> probes).</summary>
    public string Text { get; private set; } = "";

    /// <summary>The colour the name is drawn in.</summary>
    public Color TextColor => _color;

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "TargetLabel";
        MouseFilter = MouseFilterEnum.Ignore;
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
            Set($"{name} {l}/{m}", pick.Npc is { IsBoss: true } ? BossColor : UiTheme.Text);
        }
        else if (interaction.InReach && interaction.Target is { } t)
        {
            object? what = t.Object != null ? t.Object.Def : t.Block is 0 or VoxelWorld.Unknown ? null : _ui.Game.Session.Content.Blocks[t.Block];
            if (!Show(what, 0, 0) || what == null) return;
            Set(t.Object?.Def.DisplayName ?? _ui.Game.Session.Content.Blocks[t.Block].DisplayName, UiTheme.Text);
        }
        else Show(null, 0, 0);
    }

    public override void _Draw()
    {
        if (Text.Length == 0) return;
        var font = UiFonts.Bold;
        float width = font.GetStringSize(Text, HorizontalAlignment.Left, -1, UiFonts.Hover).X;
        var cross = new Vector2(Size.X / 2, UiLayout.CrosshairTop);
        float x = Mathf.Max(0, Mathf.Min(cross.X + Offset.X, Size.X - width - UiFonts.Outline / 2f));
        var baseline = new Vector2(x, cross.Y + Offset.Y + font.GetAscent(UiFonts.Hover)).Round();
        DrawStringOutline(font, baseline, Text, HorizontalAlignment.Left, -1, UiFonts.Hover, UiFonts.Outline, UiFonts.OutlineColor);
        DrawString(font, baseline, Text, HorizontalAlignment.Left, -1, UiFonts.Hover, _color);
    }

    private void Set(string text, Color color)
    {
        Text = text;
        _color = color;
        QueueRedraw();
    }

    /// <summary>Records what is shown; false when it is unchanged. Showing nothing clears the text at once.</summary>
    private bool Show(object? what, int life, int max)
    {
        if (what == _shown && life == _life && max == _max) return false;
        _shown = what;
        _life = life;
        _max = max;
        if (what == null) Set("", UiTheme.Text);
        return true;
    }
}
