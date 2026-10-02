using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.World;

namespace Strata.Core.Gameplay.Guide;

/// <summary>
/// Feeds each player's <see cref="PlayerJournal"/> from world events: kills (players within the credit radius; bosses
/// credit everyone), bug-net catches, conversations, crafting, purchases, reforges, mining and felling, deaths, and two
/// sweeps — creatures near a player count as seen (every 30 ticks) and every carried item as obtained (once a second).
/// Counter keys are built once per block or object, so recording allocates nothing after the first time.
/// </summary>
public sealed class JournalSystem
{
    /// <summary>Counter names (<see cref="PlayerJournal.Counters"/>).</summary>
    public const string Mined = "mined", Broken = "broken", TreesChopped = "trees_chopped", Purchases = "purchases",
        Reforges = "reforges", Deaths = "deaths", Catches = "catches", Rescued = "rescued", AnglerQuests = "angler_quests";

    public const int SeenInterval = 30;

    private readonly GameWorld _world;
    private readonly Dictionary<ContentDef, string[]> _counterKeys = new();

    public JournalSystem(GameWorld world)
    {
        _world = world;
        Bestiary = new Bestiary(world.Session.Content);
        world.Npcs.Killed += OnKilled;
        world.ItemUse.NetCaught += (p, npc) => OnCaught(p, npc);
        world.ItemPickedUp += (p, stack) => Obtain(p.State.Journal, stack);
        world.Crafted += (p, item, _) =>
        {
            p.State.Journal.MarkCrafted(item.Id);
            p.State.Journal.MarkObtained(item.Id);
        };
        world.Commerce.Traded += (p, item, count) =>
        {
            p.State.Journal.Count(Purchases, count);
            p.State.Journal.MarkObtained(item.Id);
        };
        world.Services.Reforged += (p, _) => p.State.Journal.Count(Reforges);
        world.Services.AnglerQuestDone += (p, _) => p.State.Journal.Count(AnglerQuests);
        world.PlayerDied += p => p.State.Journal.Count(Deaths);
        world.Town.Rescued += (npc, p) =>
        {
            p.State.Journal.Count(Rescued + ":" + npc.Def.Id);
            p.State.Journal.Count(Rescued);
        };
        world.Town.PlayerRecords = p => { p.State.Journal.Bestiary ??= Bestiary; return p.State.Journal; }; // the town's bestiary/kill/item/achievement conditions (P5-03)
        world.Editor.BlockBroken += (cell, block) => CountNear(cell, block, Mined, false);
        world.Editor.ObjectBroken += obj => CountNear(obj.Anchor, obj.Def, Broken, obj.Def.Collision == TileCollision.Trunk && obj.Def.BreakTool == BreakTool.Axe);
    }

    public Bestiary Bestiary { get; }

    public BestiaryConfig Config
    {
        get => Bestiary.Config;
        set => Bestiary.Config = value;
    }

    /// <summary>A player's records (for town arrival conditions, achievements and the guide).</summary>
    public IPlayerRecords RecordsFor(PlayerEntity player) => player.State.Journal;

    /// <summary>The best bestiary completion among present players (world-wide conditions such as the Zoologist's).</summary>
    public float BestBestiaryPercent()
    {
        float best = 0f;
        foreach (var p in _world.Players) best = MathF.Max(best, p.State.Journal.BestiaryPercent);
        return best;
    }

    public void Tick()
    {
        long tick = _world.Session.Clock.Tick;
        if (tick % SeenInterval == 0) SweepSeen();
        if (tick % Units.TicksPerSecond == 0)
            foreach (var p in _world.Players) SweepCarried(p.State.Journal, p.State.Inventory);
    }

    private void OnKilled(Npc npc)
    {
        var def = npc.Def;
        string entry = Bestiary.EntryOf(def);
        bool everyone = def.Boss != null || npc.IsPart || def.BestiaryAs != null;
        float r2 = Config.CreditRadius * Config.CreditRadius;
        foreach (var p in _world.Players)
        {
            if (!everyone && Vector3.DistanceSquared(p.Center, npc.Center) > r2) continue;
            var j = p.State.Journal;
            j.MarkSeen(entry);
            j.AddKill(def.Id);
        }
    }

    private static void OnCaught(PlayerEntity p, Npc npc)
    {
        var j = p.State.Journal;
        j.MarkSeen(Bestiary.EntryOf(npc.Def));
        j.MarkCaught(Bestiary.EntryOf(npc.Def));
        j.Count(Catches);
    }

    private void SweepSeen()
    {
        var npcs = _world.Npcs.All;
        float r2 = Config.SeenRadius * Config.SeenRadius;
        foreach (var p in _world.Players)
        {
            if (!p.Alive) continue;
            var j = p.State.Journal;
            j.Bestiary ??= Bestiary;
            for (int i = 0; i < npcs.Count; i++)
            {
                var n = npcs[i];
                if (n.Alive && Vector3.DistanceSquared(p.Center, n.Center) <= r2) j.MarkSeen(Bestiary.EntryOf(n.Def));
            }
            var residents = _world.Town.Residents;
            for (int i = 0; i < residents.Count; i++)
                if (residents[i].TalkingTo == p) j.MarkMet(residents[i].Def.Id);
        }
    }

    private static void SweepCarried(PlayerJournal j, PlayerInventory inv)
    {
        Sweep(j, inv.Backpack);
        Sweep(j, inv.Ammo);
        Sweep(j, inv.Coins);
        Sweep(j, inv.Armor);
        Sweep(j, inv.Accessories);
        Sweep(j, inv.VanityArmor);
        Sweep(j, inv.VanityAccessories);
        Sweep(j, inv.Dyes);
        Sweep(j, inv.Misc);
    }

    private static void Sweep(PlayerJournal j, Inventory storage)
    {
        for (int i = 0; i < storage.Size; i++)
            if (storage[i].Item is { } item) j.MarkObtained(item.Id);
    }

    private static void Obtain(PlayerJournal j, ItemStack stack)
    {
        if (stack.Item is { } item) j.MarkObtained(item.Id);
    }

    /// <summary>Counts a broken block or object for the nearest player within the credit radius.</summary>
    private void CountNear(Int3 cell, ContentDef def, string prefix, bool tree)
    {
        var at = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f) * Units.CellSize;
        PlayerEntity? best = null;
        float bestD = Config.CreditRadius * Config.CreditRadius;
        foreach (var p in _world.Players)
        {
            float d = Vector3.DistanceSquared(p.Center, at);
            if (d <= bestD) (best, bestD) = (p, d);
        }
        if (best == null) return;
        var j = best.State.Journal;
        foreach (var key in KeysOf(def, prefix)) j.Count(key);
        if (tree) j.Count(TreesChopped);
    }

    /// <summary>"mined", "mined:&lt;id&gt;" and "mined:&lt;tag&gt;" for each tag (or the same under "broken").</summary>
    private string[] KeysOf(ContentDef def, string prefix)
    {
        if (_counterKeys.TryGetValue(def, out var keys)) return keys;
        keys = new string[2 + def.Tags.Length];
        keys[0] = prefix;
        keys[1] = prefix + ":" + def.Id;
        for (int i = 0; i < def.Tags.Length; i++) keys[2 + i] = prefix + ":#" + def.Tags[i];
        return _counterKeys[def] = keys;
    }
}
