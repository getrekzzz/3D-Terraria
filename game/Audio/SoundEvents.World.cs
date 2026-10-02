using System.Linq;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;

namespace Strata.Game.Audio;

/// <summary>World sounds: mining bites, breaks and tinks, placing, explosions, events beginning and residents arriving.</summary>
public sealed partial class SoundEvents
{
    private void WireWorld(GameWorld game)
    {
        game.Editor.Struck += OnStruck;
        game.Editor.BlockPlaced += (cell, _) => Placed(cell);
        game.Editor.ObjectPlaced += obj => Placed(obj.Anchor);
        game.Editor.ObjectBroken += obj => Sfx.PlayAt("break_wood", Cell(obj.Anchor), -3f);
        game.Projectiles.Exploded += (at, radius) =>
        {
            _blastFrame = Engine.GetProcessFrames(); // the blast's own strike on the terrain follows at once
            Sfx.PlayAt("explosion", ToGodot(at), Mathf.Clamp(radius * 2f - 6f, -6f, 4f));
        };
        game.Events.Started += def =>
        {
            if (def.KillGoal > 0 || def.SkyTint != null) Sfx.Play("event_start", -2f);
        };
        game.Town.Arrived += _ => Sfx.Play("npc_arrive");
    }

    private void OnStruck(MineOutcome o)
    {
        var material = o.Material?.Sound ?? "stone";
        var cell = o.Broken.Count > 0 ? o.Broken.First() : o.Damaged.Count > 0 ? o.Damaged[0].Cell : o.TooHard.Count > 0 ? o.TooHard.First() : (Strata.Core.Common.Int3?)null;
        if (cell == null) return;
        bool broke = o.Broken.Count > 0;
        if (!broke && o.Damaged.Count == 0)
        {
            if (Engine.GetProcessFrames() == _blastFrame) return; // a bomb against bricks it cannot break: the blast speaks
            _tinkFrame = Engine.GetProcessFrames();
            Sfx.PlayAt("tink", Cell(cell.Value), -6f); // the pick is too weak: it only glances off
            return;
        }
        Sfx.PlayAt(Sfx.Has($"{(broke ? "break" : "dig")}_{material}") ? $"{(broke ? "break" : "dig")}_{material}" : broke ? "break_stone" : "dig_stone", Cell(cell.Value));
    }

    private void Placed(Strata.Core.Common.Int3 cell)
    {
        if (Engine.GetProcessFrames() == _lastPlaceFrame) return; // one sound per placement, not per cell
        _lastPlaceFrame = Engine.GetProcessFrames();
        Sfx.PlayAt("place", Cell(cell));
    }
}
