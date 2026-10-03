using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Session;
using Strata.Game.Items;

namespace Strata.Game.Entities;

/// <summary>
/// While the housing menu is open, each housed resident's head banner hangs in their room (at
/// <see cref="Strata.Core.Gameplay.Town.TownNpc.BannerCell"/>). The sprites are pooled: allocated once, hidden while
/// the menu is closed.
/// </summary>
public sealed partial class HousingBannerRenderer : Node3D
{
    /// <summary>Banner width (metres).</summary>
    private const float Width = 0.42f;

    private const double Interval = 0.5;
    private readonly List<Sprite3D> _pool = new();
    private GameWorld _game = null!;
    private double _timer;

    /// <summary>Banners on screen now (probe).</summary>
    public int Shown { get; private set; }

    public void Initialize(GameWorld game)
    {
        _game = game;
        Name = "HousingBanners";
        Visible = false;
        SetProcess(false);
    }

    public void SetShown(bool shown)
    {
        Visible = shown;
        SetProcess(shown);
        _timer = 0;
        if (shown) Refresh();
        else Shown = 0;
    }

    public override void _Process(double delta)
    {
        _timer -= delta;
        if (_timer > 0) return;
        _timer = Interval;
        Refresh();
    }

    private void Refresh()
    {
        int used = 0;
        foreach (var r in _game.Town.Residents)
        {
            if (!r.IsHoused || r.BannerCell is not { } cell || PortraitIcons.TownNpc(r.Def) is not { } tex) continue;
            if (used == _pool.Count)
            {
                var s = new Sprite3D { Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Shaded = false, DoubleSided = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest };
                AddChild(s);
                _pool.Add(s);
            }
            var sprite = _pool[used++];
            sprite.Texture = tex;
            sprite.PixelSize = Width / Mathf.Max(1, tex.GetWidth());
            sprite.Position = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f) * Units.CellSize;
            sprite.Visible = true;
        }
        for (int i = used; i < _pool.Count; i++) _pool[i].Visible = false;
        Shown = used;
    }
}
