using Godot;
using Strata.Core.Gameplay.Items;
using Strata.Game.Items;

namespace Strata.Game.UI;

/// <summary>
/// The stack held on the cursor (<see cref="PlayerInventory.Cursor"/>), drawn at the pointer over every screen as
/// Terraria does: a 40 px icon just below and right of the tip with its count in bold outlined white. Shown whenever
/// the pointer is free and the cursor holds something; above every panel, below the tooltip. While a controller drives
/// the screen it follows the controller cursor (<see cref="PadCursor.Pointer"/>).
/// </summary>
public sealed partial class CursorItemLayer : CanvasLayer
{
    public const float IconSize = 40;
    private static readonly Vector2 PointerOffset = new(10, 10);

    private UiContext _ui = null!;
    private Control _root = null!;
    private HeldStack _held = null!;

    /// <summary>The count of the stack drawn at the pointer; 0 while nothing is (the <c>ui.cursor_count</c> probe).</summary>
    public int ShownCount { get; private set; }

    public void Initialize(UiContext ui)
    {
        _ui = ui;
        Name = "CursorItem";
        Layer = 90;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        _held = new HeldStack { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(IconSize, IconSize) };
        _root.AddChild(_held);
    }

    public override void _Process(double delta)
    {
        var cursor = _ui.Inventory.Cursor;
        bool show = !cursor.IsEmpty && Input.MouseMode != Input.MouseModeEnum.Captured;
        ShownCount = show ? cursor.Count : 0;
        if (_held.Visible != show) _held.Visible = show;
        if (!show) return;
        _held.Hold(cursor);
        _held.Position = (PadCursor.Pointer ?? _root.GetLocalMousePosition()) + PointerOffset;
    }

    /// <summary>The held icon and count; redrawn only when the stack changes (following the pointer just moves it).</summary>
    private sealed partial class HeldStack : Control
    {
        private ItemStack _stack;

        public HeldStack() => TextureFilter = TextureFilterEnum.LinearWithMipmaps;

        public void Hold(ItemStack stack)
        {
            if (stack.Equals(_stack)) return;
            _stack = stack;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_stack.IsEmpty) return;
            DrawTextureRect(ItemIcons.Get(_stack.Item!), new Rect2(Vector2.Zero, Size), false);
            if (_stack.Count <= 1) return;
            var font = UiFonts.Bold;
            var text = _stack.Count.ToString();
            var baseline = new Vector2(0, Size.Y + 2);
            DrawStringOutline(font, baseline, text, HorizontalAlignment.Left, -1, UiFonts.Stack, UiFonts.Outline, UiFonts.OutlineColor);
            DrawString(font, baseline, text, HorizontalAlignment.Left, -1, UiFonts.Stack, UiTheme.Text);
        }
    }
}
