using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Map;
using Strata.Core.Gameplay.Town;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Game.UI;

/// <summary>
/// Pylons on the world map (P5-34): a marker per placed pylon, bright while it works, and a click on one travels
/// there through <see cref="PylonNetwork.Teleport"/> when the player stands near a working pylon.
/// </summary>
public static class PylonMap
{
    private const ulong RefreshMs = 1000;
    private const float PickPixels = 10f;

    private static readonly List<(TileObjectInstance Pylon, bool Active)> Cached = new();
    private static ulong _refreshedAt;
    private static object? _cachedFor;

    private static IReadOnlyList<(TileObjectInstance Pylon, bool Active)> Pylons(UiContext ui)
    {
        ulong now = Time.GetTicksMsec();
        if (_cachedFor == ui.Game && now - _refreshedAt < RefreshMs) return Cached;
        _cachedFor = ui.Game;
        _refreshedAt = now;
        Cached.Clear();
        var network = ui.Game.Town.Pylons;
        foreach (var p in network.All()) Cached.Add((p, network.IsActive(p)));
        return Cached;
    }

    private static Vector2 MapPoint(TileObjectInstance p)
    {
        var b = p.Bounds;
        return new Vector2((b.Min.X + b.Max.X) * 0.5f, (b.Min.Z + b.Max.Z) * 0.5f) / ExploredMap.CellsPerPixel;
    }

    /// <summary>Adds a marker per pylon (its light colour, dimmed while it lacks residents; hovering names it).</summary>
    public static void Markers(UiContext ui, List<MapMarker> into, float icon)
    {
        var pylons = Pylons(ui);
        for (int i = 0; i < pylons.Count; i++)
        {
            var (p, active) = pylons[i];
            var light = p.Def.Light is { Length: 3 } l ? new Color(l[0] / 15f, l[1] / 15f, l[2] / 15f) : new Color(0.6f, 0.9f, 1f);
            into.Add(new MapMarker(MapPoint(p), active ? light : light.Darkened(0.6f), null, active ? p.DisplayName : p.DisplayName + " (needs residents)", Size: icon * 0.22f));
        }
    }

    /// <summary>Travels to the pylon under a map click, if any; reports why not otherwise.</summary>
    public static void TryTeleport(UiContext ui, MapView view, Vector2 at, Action close)
    {
        TileObjectInstance? hit = null;
        float best = PickPixels / Mathf.Max(0.25f, view.Zoom);
        foreach (var (p, _) in Pylons(ui))
        {
            float d = MapPoint(p).DistanceTo(at);
            if (d <= best) (hit, best) = (p, d);
        }
        if (hit == null) return;
        var result = ui.Game.Town.Pylons.Teleport(ui.Entity, hit);
        string? why = result switch
        {
            PylonTeleport.NotNearPylon => "You must be near a working pylon to travel.",
            PylonTeleport.InactiveDestination => "Not enough residents live near that pylon.",
            PylonTeleport.NoRoom => "There is no room next to that pylon.",
            _ => null,
        };
        if (why != null)
        {
            ui.Notify(why, UiTheme.Muted);
            Audio.Sfx.Play("ui_close");
            return;
        }
        if (result != PylonTeleport.Teleported) return;
        Audio.Sfx.Play("teleport");
        close();
    }
}
