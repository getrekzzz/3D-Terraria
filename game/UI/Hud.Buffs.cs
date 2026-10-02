using System.Collections.Generic;
using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.UI;

/// <summary>The buff row under the hotbar: each active buff's icon with its time left under it, wrapping in the Buffs region.</summary>
public sealed partial class Hud
{
    private Control _buffs = null!;
    private readonly List<int> _buffSeconds = new();

    private void UpdateBuffs()
    {
        var active = _ui.Entity.State.Buffs.Active;
        bool grew = _buffs.GetChildCount() < active.Count;
        while (_buffs.GetChildCount() > active.Count)
        {
            _buffs.GetChild(_buffs.GetChildCount() - 1).Free();
            _buffSeconds.RemoveAt(_buffSeconds.Count - 1);
        }
        while (_buffs.GetChildCount() < active.Count)
        {
            var cell = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(UiLayout.BuffIcon, UiLayout.BuffCellHeight) };
            cell.AddChild(new TextureRect
            {
                Size = new Vector2(UiLayout.BuffIcon, UiLayout.BuffIcon), StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            var timer = new Label
            {
                Position = new Vector2(-(UiLayout.BuffPitchX - UiLayout.BuffIcon) / 2, UiLayout.BuffIcon),
                Size = new Vector2(UiLayout.BuffPitchX, UiLayout.BuffCellHeight - UiLayout.BuffIcon),
                HorizontalAlignment = HorizontalAlignment.Center, ClipText = true, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            timer.AddThemeFontSizeOverride("font_size", 12);
            timer.AddThemeColorOverride("font_outline_color", UiFonts.OutlineColor);
            timer.AddThemeConstantOverride("outline_size", UiFonts.Outline);
            cell.AddChild(timer);
            _buffs.AddChild(cell);
            _buffSeconds.Add(-1);
        }
        if (grew) ArrangeBuffs();
        BuffIconsShown = 0;
        for (int i = 0; i < active.Count; i++)
        {
            var b = active[i];
            var cell = _buffs.GetChild(i);
            var icon = cell.GetChild<TextureRect>(0);
            var texture = StatusIcons.Get(b.Def);
            if (!StatusIcons.IsFallback(texture)) BuffIconsShown++;
            int seconds = Mathf.CeilToInt(b.TicksLeft / 60f);
            if (icon.Texture == texture && _buffSeconds[i] == seconds) continue;
            var timer = cell.GetChild<Label>(1);
            if (icon.Texture != texture)
            {
                icon.Texture = texture;
                icon.TooltipText = b.Def.DisplayName;
                timer.AddThemeColorOverride("font_color", Color.FromHtml(b.Def.Color));
            }
            _buffSeconds[i] = seconds;
            timer.Text = seconds >= 60 ? $"{seconds / 60} m" : $"{seconds} s";
        }
    }

    /// <summary>The cells in rows across the Buffs region, as many per row as it is wide.</summary>
    private void ArrangeBuffs()
    {
        int perRow = Mathf.Max(1, (int)((_buffs.Size.X + UiLayout.BuffPitchX - UiLayout.BuffIcon + 0.5f) / UiLayout.BuffPitchX));
        for (int i = 0; i < _buffs.GetChildCount(); i++)
            _buffs.GetChild<Control>(i).Position = new Vector2(i % perRow * UiLayout.BuffPitchX, i / perRow * UiLayout.BuffPitchY);
    }
}
