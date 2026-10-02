using System.Linq;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;

namespace Strata.Game.Audio;

/// <summary>Creature sounds: hits and deaths, bosses rising and falling, and the player being hurt.</summary>
public sealed partial class SoundEvents
{
    private void WireCreatures(GameWorld game)
    {
        game.Npcs.Damaged += e =>
        {
            ulong now = Time.GetTicksMsec();
            if (e.ToPlayer)
            {
                if (System.Numerics.Vector3.Distance(e.Position, _local.Center) < 3f) Sfx.Play("player_hurt");
                return;
            }
            if (now - _lastHitMs < 45) return; // a flurry of hits reads as one
            _lastHitMs = now;
            Sfx.PlayAt("hit", ToGodot(e.Position), e.Crit ? 2f : 0f);
        };
        game.Npcs.Killed += npc =>
        {
            string id = npc.Def.HasTag("gel") ? "slime_death" : npc.Def.Model is "skeleton" or "cursed_skull" or "dungeon_guardian" ? "hit_bone" : "npc_death";
            Sfx.PlayAt(id, ToGodot(npc.Center), npc.IsBoss ? 4f : 0f);
        };
        game.Npcs.BossSpawned += _ => Sfx.Play("boss_roar");
        game.Npcs.BossDefeated += _ => Sfx.Play("boss_defeated");
    }
}
