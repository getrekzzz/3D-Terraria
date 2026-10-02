using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>
/// The crosshair in the Crosshair region: a small cross in the cursor colour (Settings → Interface), brighter when the
/// target is within reach. Pointing at a town resident within talking range shows the speech-bubble hint and, at a
/// door, chest, bed, chair, lever or altar within reach, the hand hint, each with the interact key to its right; the
/// <see cref="TargetLabel"/> names what is under it below. With no region (the inventory's layout) only the cross shows.
/// </summary>
public sealed partial class Crosshair : Control
{
    private const float Arm = 8, Gap = 3, HintOffset = 16;

    private readonly UiContext _ui;
    private readonly TargetLabel _target;
    private bool _placed;
    private bool _inReach;
    private Texture2D? _hint;
    private string _key = "";
    private string? _cursorSetting;
    private Color _tint = Colors.White;

    public Crosshair(UiContext ui)
    {
        _ui = ui;
        Name = "Crosshair";
        MouseFilter = MouseFilterEnum.Ignore;
        _target = new TargetLabel();
        _target.Initialize(ui);
        AddChild(_target);
    }

    /// <summary>What the label under the crosshair shows (the <c>hud.target_name</c> probes).</summary>
    public string TargetText => _target.Text;

    /// <summary>
    /// Takes the Crosshair region of <paramref name="layout"/>; without one, the cross alone stays at the centre of
    /// <paramref name="screen"/> with no area of its own (so it is left out of the overlap check).
    /// </summary>
    public void Place(UiLayoutResult layout, Vector2 screen)
    {
        _placed = LayoutWidgets.Place(this, layout, UiRegion.Crosshair);
        if (!_placed)
        {
            Position = screen / 2;
            Size = Vector2.Zero;
        }
        _target.Visible = _placed;
        _target.Position = new Vector2(0, UiLayout.CrosshairTop + Arm + 4);
        _target.Size = new Vector2(Size.X, Mathf.Max(0, Size.Y - _target.Position.Y));
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        var interaction = _ui.Interaction;
        bool inReach = interaction.InReach;
        Texture2D? hint = null;
        if (_placed)
        {
            if (interaction.TalkTarget != null) hint = UiSprites.Get("talk_hint");
            else if (inReach && interaction.Target?.Object is { } obj && Interactive(obj.Def)) hint = UiSprites.Get("open_hint");
        }
        string setting = Settings.UserSettings.Current.CursorColor;
        if (inReach == _inReach && hint == _hint && ReferenceEquals(setting, _cursorSetting)) return;
        if (hint != null && _hint == null) _key = Settings.KeyBindings.Label("interact");
        _inReach = inReach;
        _hint = hint;
        if (!ReferenceEquals(setting, _cursorSetting))
        {
            _cursorSetting = setting;
            _tint = Color.FromHtml(Color.HtmlIsValid(setting) ? setting : Settings.UserSettings.DefaultCursorColor);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var at = _placed ? new Vector2(Size.X / 2, UiLayout.CrosshairTop) : Vector2.Zero;
        var c = new Color(_tint, _inReach ? 0.95f : 0.5f);
        var shadow = new Color(0, 0, 0, c.A * 0.6f);
        for (int pass = 0; pass < 2; pass++)
        {
            // A dark copy one pixel down-right keeps the cross visible on bright ground.
            var o = pass == 0 ? at + Vector2.One : at;
            var col = pass == 0 ? shadow : c;
            DrawLine(o + new Vector2(-Arm, 0), o + new Vector2(-Gap, 0), col, 2);
            DrawLine(o + new Vector2(Gap, 0), o + new Vector2(Arm, 0), col, 2);
            DrawLine(o + new Vector2(0, -Arm), o + new Vector2(0, -Gap), col, 2);
            DrawLine(o + new Vector2(0, Gap), o + new Vector2(0, Arm), col, 2);
        }
        if (_hint == null) return;
        var size = _hint.GetSize();
        var hintAt = (at + new Vector2(HintOffset, -size.Y / 2)).Round();
        DrawTextureRect(_hint, new Rect2(hintAt, size), false);
        var font = UiFonts.Bold;
        var baseline = new Vector2(hintAt.X + size.X + 4, at.Y + UiFonts.Body * 0.35f);
        DrawStringOutline(font, baseline, _key, HorizontalAlignment.Left, -1, UiFonts.Body, UiFonts.Outline, UiFonts.OutlineColor);
        DrawString(font, baseline, _key, HorizontalAlignment.Left, -1, UiFonts.Body, UiTheme.Text);
    }

    /// <summary>Objects the interact key does something to (stations and signs only show their name).</summary>
    private static bool Interactive(TileObjectDef def) => def.Interaction switch
    {
        TileInteraction.Door or TileInteraction.Chest or TileInteraction.Sit or TileInteraction.Sleep or TileInteraction.Switch => true,
        TileInteraction.Activate => def.Summon != null,
        _ => false,
    };
}
