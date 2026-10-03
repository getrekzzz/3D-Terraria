using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Hearts and mana Stars from kills, and a magnet that respects a full inventory (P3-29).</summary>
public class PickupTests
{
    private static int Count(TestGame g, string id) => g.World.Drops.Items.Where(d => d.Stack.Item!.Id == id).Sum(d => d.Stack.Count);

    private static void Kills(TestGame g, string enemy, int n, DamageClass damage = DamageClass.Melee)
    {
        for (int i = 0; i < n; i++)
        {
            var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get(enemy), g.Player.Position + new Vector3(8, 1, 0))!;
            g.World.Npcs.Hit(npc, g.Player, 100000, damage, 0, 0f, g.Player.Center, melee: false);
            Assert.False(npc.Alive);
        }
    }

    [Fact]
    public void HurtPlayersKillsDropHeartsThatHealWithoutASlot()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var v = g.Player.State.Vitals;
        v.Life = v.MaxLife / 2;
        Kills(g, "zombie", 60);
        Assert.True(Count(g, GameWorld.HeartItem) >= 1);
        Assert.Equal(0, Count(g, GameWorld.ManaStarItem)); // mana is full

        var heart = g.World.Drops.Items.First(d => d.Stack.Item!.Id == GameWorld.HeartItem);
        heart.Position = g.Player.Center;
        heart.PickupDelay = 0;
        int before = v.Life;
        int applied = 0;
        g.World.PickupApplied += (_, _, life, _) => applied += life;
        g.Ticks(2);
        Assert.Equal(20, applied);
        Assert.True(v.Life >= before + 20);
        Assert.Equal(0, g.Player.State.Inventory.Backpack.Count(heart.Stack.Item!));
    }

    [Fact]
    public void HealthyPlayersGetNoHeartsAndMagicKillsDropStars()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        Kills(g, "zombie", 60);
        Assert.Equal(0, Count(g, GameWorld.HeartItem) + Count(g, GameWorld.ManaStarItem));
        g.Player.State.Vitals.Mana = 0;
        Kills(g, "zombie", 20, DamageClass.Magic);
        Assert.True(Count(g, GameWorld.ManaStarItem) >= 5); // the magic Star alone is 1 in 2
        Assert.Equal(0, Count(g, GameWorld.HeartItem));
    }

    [Fact]
    public void CrittersAndStatueSpawnsDropNothing()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        g.World.Drops.Config.DropOneIn = 1;
        g.Player.State.Vitals.Life = 10;
        var npc = g.World.Npcs.Spawn(g.Content.Enemies.Get("zombie"), g.Player.Position + new Vector3(8, 1, 0))!;
        npc.FromStatue = true;
        g.World.Npcs.Hit(npc, g.Player, 100000, DamageClass.Melee, 0, 0f, g.Player.Center, melee: false);
        var critter = g.Content.Enemies.All.First(e => e.HasTag("critter"));
        Kills(g, critter.Id, 10);
        Assert.Equal(0, Count(g, GameWorld.HeartItem));
    }

    [Fact]
    public void AFullBackpackLeavesItemsOnTheGroundButStillTakesHearts()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var inv = g.Player.State.Inventory;
        var stone = g.Content.Items.Get("stone");
        for (int i = 0; i < inv.Backpack.Size; i++) inv.Backpack[i] = new ItemStack(stone, stone.MaxStack);
        var dirt = g.Content.Items.Get("dirt");
        Assert.False(GameWorld.CanCollect(g.Player, new ItemStack(dirt, 1)));
        var at = g.Player.Center + new Vector3(2f, 0, 0);
        var drop = g.World.Drops.Spawn(new ItemStack(dirt, 1), at, Vector3.Zero, pickupDelay: 0);
        var v = g.Player.State.Vitals;
        v.Life = 50;
        g.World.SpawnHeart(g.Player.Center + new Vector3(-2f, 0, 0), Vector3.Zero);
        g.Ticks(120);
        Assert.Contains(drop, g.World.Drops.Items);
        Assert.True(Vector3.Distance(drop.Position, g.Player.Center) >= 1.5f);
        Assert.Equal(0, Count(g, GameWorld.HeartItem));
        Assert.True(v.Life >= 70);
    }

    [Fact]
    public void SoulsHangInTheAir()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var soul = g.Content.Items.Get("soul_of_light");
        Assert.True(soul.NoGravity);
        var drop = g.World.Drops.Spawn(new ItemStack(soul, 1), g.Player.Center + new Vector3(5f, 1.5f, 0), new Vector3(0, 1f, 0));
        float y = drop.Position.Y;
        g.Ticks(120);
        Assert.InRange(drop.Position.Y, y, y + 0.5f);
    }

    [Fact]
    public void ABossDefeatSpillsFiveToNineHearts()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var boss = g.World.Npcs.Spawn(g.Content.Enemies.Get("king_slime"), g.Player.Position + new Vector3(8, 1, 0))!;
        g.World.Npcs.Kill(boss);
        Assert.InRange(Count(g, GameWorld.HeartItem), 5, 9);
    }

    [Fact]
    public void AFullBackpackStillCollectsCoinsThatFitOnceTidied()
    {
        using var g = TestGame.Create();
        g.World.Spawner.Enabled = false;
        var inv = g.Player.State.Inventory;
        inv.Content ??= g.Content;
        var stone = g.Content.Items.Get("stone");
        for (int i = 0; i < inv.Backpack.Size; i++) inv.Backpack[i] = new ItemStack(stone, stone.MaxStack);
        var copper = g.Content.Items.Get("copper_coin");
        for (int i = 0; i < inv.Coins.Size; i++) inv.Coins[i] = new ItemStack(copper, copper.MaxStack); // untidy: 400 copper
        var coin = new ItemStack(copper, 5);
        Assert.True(inv.CanTake(coin));
        Assert.True(GameWorld.CanCollect(g.Player, coin));
        g.World.Drops.Spawn(coin, g.Player.Center + new Vector3(1.5f, 0, 0), Vector3.Zero, pickupDelay: 0);
        g.Ticks(120);
        Assert.Equal(0, Count(g, "copper_coin"));
        Assert.Equal(405, Strata.Core.Gameplay.Items.Coins.Total(inv.Coins));
        Assert.False(inv.CanTake(new ItemStack(g.Content.Items.Get("dirt"), 1)));
    }

    [Fact]
    public void PickupsAreAppliedNeverStored()
    {
        using var g = TestGame.Create();
        var inv = g.Player.State.Inventory;
        var heart = g.Content.Items.Get(GameWorld.HeartItem);
        var stack = new ItemStack(heart, 1);
        Assert.Equal(stack, inv.Pickup(stack)); // comes back whole
        Assert.Equal(0, inv.Backpack.Count(heart));
        var v = g.Player.State.Vitals;
        v.Life = 30;
        var r = Strata.Core.Commands.BuiltinCommands.CreateRegistry();
        var ctx = new Strata.Core.Commands.CommandContext(g.World, new Strata.Core.Commands.HeadlessCommandHost()) { Registry = r };
        Assert.True(r.Execute(ctx, "give heart 2").Ok);
        Assert.Equal(0, inv.Backpack.Count(heart));
        Assert.Equal(70, v.Life);
    }
}
