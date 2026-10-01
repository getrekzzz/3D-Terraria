using System.Linq;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;

namespace Strata.Game.Audio;

/// <summary>
/// Turns what happens in the world into sound: picks biting blocks by material (or tinking off one too hard), placing,
/// swings and shots by weapon kind, a dry click or a mana fizzle for a press that could not be used, hits and deaths,
/// explosions, bosses rising and falling, events beginning, residents arriving, pick-ups, drinks, the Magic Mirror and the
/// local player's death.
/// </summary>
public sealed partial class SoundEvents : Node
{
    private GameWorld _game = null!;
    private PlayerEntity _local = null!;
    private ulong _lastPlaceFrame;
    private ulong _tinkFrame;
    private ulong _blastFrame;
    private ulong _lastHitMs;
    private bool _wasAlive = true;
    private int _lastSerial;

    public void Initialize(GameWorld game, PlayerEntity local)
    {
        Name = "SoundEvents";
        _game = game;
        _local = local;
        game.Editor.Struck += OnStruck;
        game.Editor.BlockPlaced += (cell, _) => Placed(cell);
        game.Editor.ObjectPlaced += obj => Placed(obj.Anchor);
        game.Editor.ObjectBroken += obj => Sfx.PlayAt("break_wood", Cell(obj.Anchor), -3f);
        game.ItemUse.Used += OnUsed;
        game.ItemUse.Failed += (p, _, outcome) =>
        {
            if (p != _local) return; // once per press, never while held
            if (outcome == UseOutcome.NoMana) Sfx.Play("no_mana", -4f);
            else Sfx.Play("use_fail", -4f);
        };
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
        game.ItemPickedUp += (p, stack) =>
        {
            if (p == _local) Sfx.Play(stack.Item!.HasTag("coin") ? "coins" : "pickup");
        };
        game.ItemUse.Caught += (p, _) =>
        {
            if (p == _local) Sfx.Play("splash");
        };
        game.ItemUse.Recalled += (p, _) =>
        {
            if (p == _local) Sfx.Play("teleport");
        };
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.sounds");
        if (_wasAlive && !_local.Alive) Sfx.Play("player_death");
        _wasAlive = _local.Alive;
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

    private void OnUsed(PlayerEntity p, Strata.Core.Gameplay.Items.ItemUseInfo info, UseOutcome outcome)
    {
        if (p != _local) return;
        var item = info.Item;
        // Every swing of a tool whooshes once, whether or not it also bites a block (that sound comes from the hit).
        bool newSwing = p.UseSerial != _lastSerial;
        _lastSerial = p.UseSerial;
        switch (outcome)
        {
            case UseOutcome.Swung:
                Sfx.Play("swing", item.UseTime < 18 ? -3f : 0f);
                break;
            case UseOutcome.Mined or UseOutcome.Damaged or UseOutcome.HitObject or UseOutcome.TooHard or UseOutcome.WrongTool:
                if (newSwing && info.IsTool) Sfx.Play("swing", item.UseTime < 18 ? -3f : 0f);
                // An object too tough for the tool (an altar before its time) tinks too; blocks already did where struck.
                if (outcome == UseOutcome.TooHard && Engine.GetProcessFrames() != _tinkFrame) Sfx.Play("tink", -6f);
                break;
            case UseOutcome.Shot:
                Sfx.Play(ShotSound(item), item.UseTime <= 6 ? -5f : 0f);
                break;
            case UseOutcome.Consumed:
                if (item.HealLife > 0) Sfx.Play("potion");
                else if (item.HealMana > 0) Sfx.Play("mana");
                else if (item.Category == ItemCategory.BossSummon) Sfx.Play("summon");
                else Sfx.Play("heal", -4f);
                break;
            case UseOutcome.PouredLiquid:
            case UseOutcome.CollectedLiquid:
                Sfx.Play("splash", -4f);
                break;
            case UseOutcome.Wired:
                Sfx.Play("wire", -2f);
                break;
            case UseOutcome.Caught:
                Sfx.Play("net");
                Sfx.Play("pickup");
                break;
            case UseOutcome.Mounted:
                Sfx.Play("minecart");
                break;
        }
    }

    private static string ShotSound(ItemDef item)
    {
        if (item.UseAmmo == "arrow") return "bow";
        if (item.UseAmmo is "bullet" or "rocket") return item.UseTime >= 30 || item.UseAmmo == "rocket" ? "gun_heavy" : "gun";
        if (item.DamageType == DamageClass.Magic) return item.ManaCost >= 12 ? "magic_heavy" : "magic";
        if (item.DamageType == DamageClass.Summon) return "summon";
        return "swing";
    }

    private static Vector3 Cell(Strata.Core.Common.Int3 c) => (new Vector3(c.X, c.Y, c.Z) + new Vector3(0.5f, 0.5f, 0.5f)) * Strata.Core.Common.Units.CellSize;

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
