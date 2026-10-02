using Godot;
using Strata.Game.UI;

namespace Strata.Game.Menu;

/// <summary>
/// The title-screen logo (assets/ui/logo.png, drawn by tools/textures/ui_logo.py): it sways gently about its centre
/// (rotation 0.03·sin(0.6t), scale 1 + 0.02·sin t, as Terraria's) and dims with the backdrop's night. The control keeps
/// a fixed size in its container; only the picture inside moves, so nothing around it is laid out again.
/// </summary>
public sealed partial class MenuLogo : Control
{
    public const string Sprite = "logo";
    private static readonly Color Night = new(0.62f, 0.68f, 0.86f);

    private TextureRect _picture = null!;
    private float _time;

    /// <summary>The backdrop whose daylight tints the logo (none: always lit).</summary>
    public MenuBackground? Background { get; init; }

    public override void _Ready()
    {
        Name = "Logo";
        MouseFilter = MouseFilterEnum.Ignore;
        var texture = UiSprites.Get(Sprite);
        CustomMinimumSize = texture.GetSize();
        _picture = new TextureRect { Texture = texture, MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.KeepCentered };
        _picture.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_picture);
        Resized += () => _picture.PivotOffset = Size / 2;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        _picture.Rotation = 0.03f * Mathf.Sin(0.6f * _time);
        float scale = 1f + 0.02f * Mathf.Sin(_time);
        _picture.Scale = new Vector2(scale, scale);
        _picture.Modulate = Night.Lerp(Colors.White, Background?.Daylight ?? 1f);
    }
}
