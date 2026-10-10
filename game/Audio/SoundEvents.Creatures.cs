using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Presentation;
using Strata.Core.Session;

namespace Strata.Game.Audio;

/// <summary>
/// Creature sounds by what they are made of (data/config/creature_materials.json): a squelch for gel, a clack for
/// bone, a clang for metal, a crunch for chitin…, each material throttled on its own so a flurry of hits reads as one;
/// the creature's own hit/death sound wins. A critical hit lands louder and truer in pitch. Damage over time is silent.
/// Bosses rise and fall loudly; the player is hurt, a little louder for a bigger hit (falls included: they go through
/// Core like any other hurt).
/// </summary>
public sealed partial class SoundEvents
{
    private CreatureMaterialsConfig _materials = null!;
    private readonly Dictionary<string, ulong> _lastMaterialHit = new();
    private ulong _lastPeriodicHurt;

    /// <summary>The creature materials (shared with the hit dust and gore).</summary>
    public CreatureMaterialsConfig Materials => _materials;

    private void WireCreatures(GameWorld game)
    {
        _materials = CreatureMaterialsConfig.Load(new Bootstrap.GodotDataSource());
        game.Npcs.Damaged += e =>
        {
            if (e.ToPlayer)
            {
                // One hurt sound per hit: this is the only place it plays (Core announces every player hurt here).
                if (!HurtsLocal(e)) return;
                if (e.Kind != DamageKind.Periodic) Sfx.Play("player_hurt", HurtVolume(e.Amount));
                else if (Time.GetTicksMsec() - _lastPeriodicHurt >= 800)
                {
                    // Burning or poisoned: a quieter hurt, at most every 0.8 s.
                    _lastPeriodicHurt = Time.GetTicksMsec();
                    Sfx.Play("player_hurt", -6f);
                }
                return;
            }
            if (e.Kind == DamageKind.Periodic) return;
            var npc = e.Target ?? game.Npcs.Find(e.NpcId);
            string material = npc != null ? _materials.ResolveId(npc.Def) : CreatureMaterialsConfig.Default;
            ulong now = Time.GetTicksMsec();
            if (_lastMaterialHit.TryGetValue(material, out ulong last) && now - last < 45) return;
            _lastMaterialHit[material] = now;
            string sound = npc?.Def.HitSound ?? _materials.Materials[material].HitSound;
            Sfx.PlayAt(Sfx.Has(sound) ? sound : "hit", ToGodot(e.Position), e.Crit ? 3f : 0f, e.Crit ? 0.03f : 0.08f);
        };
        game.Npcs.Killed += npc =>
        {
            string sound = npc.Def.DeathSound ?? _materials.Resolve(npc.Def).DeathSound;
            Sfx.PlayAt(Sfx.Has(sound) ? sound : "npc_death", ToGodot(npc.Center), npc.IsBoss ? 4f : 0f);
        };
        game.Npcs.BossSpawned += _ =>
        {
            Sfx.Play("boss_roar");
            Player.CameraShake.Punch(0.45f, 0.5f, 2f);
        };
        game.Npcs.BossDefeated += _ =>
        {
            Sfx.Play("boss_defeated");
            Player.CameraShake.Punch(0.7f, 0.7f, 2.5f);
        };
    }

    /// <summary>Whether a hurt is the local player's (events name the player; the distance is the fallback).</summary>
    private bool HurtsLocal(DamageEvent e) =>
        e.Player != null ? e.Player == _local : System.Numerics.Vector3.Distance(e.Position, _local.Center) < 3f;

    /// <summary>Small knocks are a little quieter than the player's hurt sound at full; big ones a little louder.</summary>
    private float HurtVolume(int damage) =>
        Mathf.Clamp(-4f + 10f * damage / Mathf.Max(1f, _local.State.Vitals.MaxLife), -4f, 2f);
}
