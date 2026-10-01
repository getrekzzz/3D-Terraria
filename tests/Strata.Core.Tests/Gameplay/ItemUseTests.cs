using System.Numerics;
using Strata.Core.Commands;
using Strata.Core.Common;
using Strata.Core.Gameplay.Building;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels;

namespace Strata.Core.Tests.Gameplay;

public class ItemUseTests
{
    [Fact]
    public void ItemActionsAreInferredFromStats()
    {
        var uses = new ItemUseTable(TestPaths.Content);
        var db = TestPaths.Content;
        Assert.True(uses[db.Items.Get("copper_pickaxe")].Has(ItemAction.Mine | ItemAction.Melee));
        Assert.True(uses[db.Items.Get("copper_axe")].Has(ItemAction.Chop));
        Assert.True(uses[db.Items.Get("dirt")].Has(ItemAction.PlaceBlock));
        Assert.True(uses[db.Items.Get("torch")].Has(ItemAction.PlaceObject));
        Assert.True(uses[db.Items.Get("lesser_healing_potion")].Has(ItemAction.Consume));
        Assert.True(uses[db.Items.Get("wooden_bow")].Has(ItemAction.Shoot));
        Assert.Equal("wooden_arrow", uses[db.Items.Get("wooden_arrow")].Projectile!.Id);
        Assert.Equal(ItemAction.None, uses[db.Items.Get("gel")].Actions);
    }

    [Fact]
    public void PickaxeMinesTheGridBlockAndDropsOneStack()
    {
        using var g = TestGame.Create();
        g.Hold("copper_pickaxe");
        var world = g.World.Session.World;
        var target = g.Floor + new Int3(0, 0, 3);
        var solid = BlockGrid.Cells(target).Where(c => world.GetBlock(c) != 0).ToList();
        g.AimAt(TestGame.CellCenter(target) + new Vector3(0, 0.2f, 0));
        int uses = g.UseUntil(() => world.GetBlock(target) == 0);
        Assert.InRange(uses, 2, 12);
        // The whole aligned 1 m block opens, and its stone drops as one stack.
        Assert.All(BlockGrid.Cells(target), c => Assert.Equal(0, world.GetBlock(c)));
        Assert.Single(g.World.Drops.Items);
        Assert.Equal(solid.Count, g.World.Drops.Items.Sum(d => d.Stack.Count));

        // Walk over to the pit: the dropped stone flies into the backpack.
        g.Player.Position = TestGame.CellFeet(target + Int3.Up);
        g.Ticks(120);
        Assert.Empty(g.World.Drops.Items);
        Assert.Equal(solid.Count, g.Player.State.Inventory.Backpack.Count(g.Content.Items.Get("stone")));
    }

    [Fact]
    public void BlocksNeedEnoughPickPower()
    {
        using var g = TestGame.Create();
        var obsidian = g.Content.BlockId("obsidian");
        var cell = g.Floor + new Int3(3, 1, 0);
        g.World.Editor.SetBlock(cell, obsidian);
        g.Hold("copper_pickaxe");
        g.Player.Precision = true;
        g.AimAt(TestGame.CellCenter(cell));
        Assert.Equal(UseOutcome.TooHard, g.World.ItemUse.Use(g.Player, g.Target(), true));
        Assert.Equal(obsidian, g.World.Session.World.GetBlock(cell));
    }

    [Fact]
    public void PlacingBlocksConsumesItemsAndRespectsThePlayerBody()
    {
        using var g = TestGame.Create();
        g.Hold("gray_brick", 5);
        g.Player.Precision = true;
        var world = g.World.Session.World;
        var front = g.Floor + new Int3(0, 0, 3);
        g.AimAt(TestGame.CellCenter(front) + new Vector3(0, 0.24f, 0)); // top face
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, g.Target(), true));
        Assert.Equal(g.Content.BlockId("gray_brick"), world.GetBlock(front + Int3.Up));
        Assert.Equal(CellFlags.PlayerPlaced, world.GetFlags(front.X, front.Y + 1, front.Z) & CellFlags.PlayerPlaced);
        Assert.Equal(4, g.Player.State.Inventory.Held.Count);

        // Aiming at the floor under our own feet: the new block would overlap the body.
        g.Ticks(30);
        g.Player.Aim = Vector3.Normalize(new Vector3(0.01f, -1f, 0.01f));
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, g.Target(), true));
        Assert.Equal(4, g.Player.State.Inventory.Held.Count);
    }

    [Fact]
    public void GridPlacementFillsTheBlockOneItemPerCell()
    {
        using var g = TestGame.Create();
        g.Hold("gray_brick", 20);
        var world = g.World.Session.World;
        var front = g.Floor + new Int3(0, 0, 4);
        var block = BlockGrid.Cells(front + Int3.Up).Where(c => world.GetBlock(c) == 0).ToList();
        g.AimAt(TestGame.CellCenter(front) + new Vector3(0, 0.24f, 0));
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, g.Target(), true));
        Assert.All(block, c => Assert.Equal(g.Content.BlockId("gray_brick"), world.GetBlock(c)));
        Assert.Equal(20 - block.Count, g.Player.State.Inventory.Held.Count);

        // Too few items left: only as many cells as the stack holds are filled.
        g.Hold("gray_brick", 3);
        g.Ticks(30);
        var next = front + new Int3(0, 0, 2);
        g.AimAt(TestGame.CellCenter(next) + new Vector3(0, 0.24f, 0));
        Assert.Equal(UseOutcome.PlacedBlock, g.World.ItemUse.Use(g.Player, g.Target(), true));
        Assert.True(g.Player.State.Inventory.Held.IsEmpty);

        // The block around our own feet overlaps the body.
        g.Hold("gray_brick", 20);
        g.Ticks(30);
        g.Player.Aim = Vector3.Normalize(new Vector3(0.01f, -1f, 0.01f));
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, g.Target(), true));
        Assert.Equal(20, g.Player.State.Inventory.Held.Count);
    }

    [Fact]
    public void CrispShapesTakeTheBuildShapeAndFacing()
    {
        using var g = TestGame.Create();
        g.Hold("gray_brick", 5);
        g.Player.Precision = true;
        g.Player.BuildShape = CellShape.Slope;
        var front = g.Floor + new Int3(0, 0, 3);
        g.AimAt(TestGame.CellCenter(front) + new Vector3(0, 0.24f, 0));
        Assert.Equal(0, g.Player.FacingQuarter); // looking +Z
        g.World.ItemUse.Use(g.Player, g.Target(), true);
        byte code = g.World.Session.World.GetShape(front.X, front.Y + 1, front.Z);
        Assert.Equal(CellShape.Slope, ShapeCode.Shape(code));
        Assert.Equal(0, ShapeCode.Rotation(code));
    }

    [Fact]
    public void DiggingDownTargetsTheGroundUnderTheFeet()
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        var t = Targeting.UnderFeet(world, g.Player.Position);
        Assert.NotNull(t);
        Assert.Equal(g.Floor, t!.Cell);
        Assert.Equal(Int3.Up, t.Normal);
        g.Hold("copper_pickaxe");
        g.UseUntilTarget(() => world.GetBlock(g.Floor) == 0, () => Targeting.UnderFeet(world, g.Player.Position));
        Assert.Equal(0, world.GetBlock(g.Floor));
    }

    [Fact]
    public void LooseBlocksFallWhenUndermined()
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        var sand = g.Content.BlockId("sand");
        var baseCell = g.Floor + new Int3(4, 1, 4);
        var batch = world.BeginEdit();
        batch.SetBlock(baseCell, g.Content.BlockId("dirt"));
        for (int i = 1; i <= 3; i++) batch.SetBlock(baseCell + new Int3(0, i, 0), sand);
        g.World.Session.Commit(batch);

        var result = g.World.Editor.Mine(new[] { baseCell }, 100, 1000, 0);
        Assert.Single(result.Broken);
        for (int i = 0; i < 3; i++) Assert.Equal(sand, world.GetBlock(baseCell + new Int3(0, i, 0)));
        Assert.Equal(0, world.GetBlock(baseCell + new Int3(0, 3, 0)));
    }

    [Fact]
    public void TorchesNeedSupportAndDropWhenTheirBlockBreaks()
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        var pillar = g.Floor + new Int3(-3, 1, 0);
        g.World.Editor.SetBlock(pillar, g.Content.BlockId("dirt"));
        g.Hold("torch", 3);
        g.AimAt(TestGame.CellCenter(pillar) + new Vector3(0, 0.24f, 0));
        Assert.Equal(UseOutcome.PlacedObject, g.World.ItemUse.Use(g.Player, g.Target(), true));
        var torch = world.TileObjects.At(pillar + Int3.Up);
        Assert.NotNull(torch);
        Assert.Equal(2, g.Player.State.Inventory.Held.Count);

        g.World.Editor.Mine(new[] { pillar }, 100, 1000, 0);
        Assert.Null(world.TileObjects.At(pillar + Int3.Up));
        Assert.Contains(g.World.Drops.Items, d => d.Stack.Item!.Id == "torch");
    }

    [Fact]
    public void AxeChopsTreesIntoWood()
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        var oak = g.Content.TileObjects.Get("oak_tree");
        var anchor = g.Floor + new Int3(-1, 1, 4);
        Assert.Equal(Strata.Core.Gameplay.Building.PlacementResult.Ok, g.World.Editor.PlaceObject(oak, anchor, 0));
        g.Hold("copper_pickaxe");
        g.AimAt(TestGame.CellCenter(anchor + new Int3(1, 2, 1)));
        Assert.Equal(UseOutcome.WrongTool, g.World.ItemUse.Use(g.Player, g.Target(), true));

        g.Hold("copper_axe");
        g.Ticks(30);
        int uses = g.UseUntil(() => world.TileObjects.At(anchor) == null);
        Assert.Equal(oak.HitPoints, uses);
        Assert.InRange(g.World.Drops.Items.Where(d => d.Stack.Item!.Id == "wood").Sum(d => d.Stack.Count), 6, 12);
    }

    [Fact]
    public void RestorativesHealAndImposeFatigue()
    {
        using var g = TestGame.Create();
        var vitals = g.Player.State.Vitals;
        vitals.DamageDirect(60);
        g.Hold("lesser_healing_potion", 2);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(90, vitals.Life);
        Assert.True(g.Player.State.Buffs.Has("potion_sickness"));
        g.Ticks(60);
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(1, g.Player.State.Inventory.Held.Count);
    }

    [Fact]
    public void VitalCrystalsRaiseMaxLifeUpToTheCap()
    {
        using var g = TestGame.Create();
        g.Hold("life_crystal", 20);
        int used = 0;
        for (int i = 0; i < 20; i++)
        {
            g.Ticks(40);
            if (g.World.ItemUse.Use(g.Player, null, true) == UseOutcome.Consumed) used++;
        }
        Assert.Equal(15, used); // 100 → 400 in steps of 20
        Assert.Equal(400, g.Player.State.Vitals.MaxLife);
    }

    [Fact]
    public void BowsConsumeArrowsAndRequestShots()
    {
        using var g = TestGame.Create();
        var shots = new List<ShotRequest>();
        g.World.ItemUse.Shot += shots.Add;
        g.Hold("wooden_bow");
        Assert.Equal(UseOutcome.NoAmmo, g.World.ItemUse.Use(g.Player, null, true));
        g.Player.State.Inventory.Backpack[20] = new ItemStack(g.Content.Items.Get("wooden_arrow"), 3);
        Assert.Equal(UseOutcome.Shot, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Single(shots);
        Assert.Equal("wooden_arrow", shots[0].Projectile.Id);
        Assert.Equal(4 + 5, shots[0].Damage);
        Assert.Equal(2, g.Player.State.Inventory.Backpack[20].Count);
        Assert.Equal(UseOutcome.Cooldown, g.World.ItemUse.Use(g.Player, null, true));
    }

    [Fact]
    public void NoAmmoIsReportedOncePerPress()
    {
        using var g = TestGame.Create();
        var ctx = new CommandContext(g.World, new HeadlessCommandHost());
        var (failed, refused) = Feedback(g);
        g.Hold("wooden_bow");
        Assert.Equal(UseOutcome.NoAmmo, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(new[] { UseOutcome.NoAmmo }, failed);
        Assert.Equal(new[] { "Needs Wooden Arrow" }, refused);
        Assert.Equal(1, ctx.ReadProbe("use.failures"));
        // Holding the button reports nothing more, even for an auto-reuse gun that keeps trying.
        for (int i = 0; i < 20; i++) g.World.ItemUse.Use(g.Player, null, false);
        g.Hold("phoenix_blaster");
        for (int i = 0; i < 20; i++) Assert.Equal(UseOutcome.NoAmmo, g.World.ItemUse.Use(g.Player, null, false));
        Assert.Single(failed);
        Assert.Equal(1, g.World.ItemUse.Failures);
        // A new press names the cheapest ammo of the gun's class.
        Assert.Equal(UseOutcome.NoAmmo, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal("Needs Musket Ball", refused[^1]);

        // Clicking again at once clicks again, but the message stays up instead of stacking; later it repeats.
        g.Ticks(10);
        g.World.ItemUse.Use(g.Player, null, true);
        Assert.Equal(3, failed.Count);
        Assert.Equal(2, refused.Count);
        g.Ticks(150);
        g.World.ItemUse.Use(g.Player, null, true);
        Assert.Equal(3, refused.Count);
    }

    [Fact]
    public void NoManaIsReported()
    {
        using var g = TestGame.Create();
        var (failed, refused) = Feedback(g);
        g.Hold("amethyst_staff");
        g.Player.State.Vitals.Mana = 0;
        Assert.Equal(UseOutcome.NoMana, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(new[] { UseOutcome.NoMana }, failed);
        Assert.Equal(new[] { "Not enough mana" }, refused);
    }

    [Fact]
    public void PotionSicknessExplains()
    {
        using var g = TestGame.Create();
        var (failed, refused) = Feedback(g);
        g.Hold("lesser_healing_potion", 2);
        Assert.Equal(UseOutcome.Consumed, g.World.ItemUse.Use(g.Player, null, true));
        g.Ticks(60);
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal(new[] { UseOutcome.Blocked }, failed);
        Assert.Equal(new[] { g.Content.Buffs.Get("potion_sickness").DisplayName }, refused);
        Assert.Equal("Potion Sickness", refused[0]);

        // A curse refuses every item, and says so.
        g.Player.State.Buffs.Add(g.Content.Buffs.Get("cursed"), 600);
        g.Player.State.Recompute();
        g.Hold("wooden_bow");
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Equal("You can't use items right now", refused[^1]);
        Assert.Equal(2, failed.Count);
    }

    [Fact]
    public void PlacementBlockedStaysSilent()
    {
        using var g = TestGame.Create();
        var (failed, refused) = Feedback(g);
        g.Hold("gray_brick", 5);
        g.Player.Precision = true;
        g.Player.Aim = Vector3.Normalize(new Vector3(0.01f, -1f, 0.01f)); // into our own body
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, g.Target(), true));
        // A crystal at the life cap is refused quietly too.
        g.Player.State.BonusLife = 300;
        g.Hold("life_crystal");
        Assert.Equal(UseOutcome.Blocked, g.World.ItemUse.Use(g.Player, null, true));
        Assert.Empty(failed);
        Assert.Empty(refused);
        Assert.Equal(0, g.World.ItemUse.Failures);
    }

    private static (List<UseOutcome> Failed, List<string> Refused) Feedback(TestGame g)
    {
        var failed = new List<UseOutcome>();
        var refused = new List<string>();
        g.World.ItemUse.Failed += (_, _, outcome) => failed.Add(outcome);
        g.World.ItemUse.Refused += (_, message) => refused.Add(message);
        return (failed, refused);
    }

    [Fact]
    public void DoorsToggleAndChestsRollLootOnce()
    {
        using var g = TestGame.Create();
        var world = g.World.Session.World;
        var door = g.Content.TileObjects.Get("wooden_door");
        var anchor = g.Floor + new Int3(4, 1, 0);
        Assert.Equal(Strata.Core.Gameplay.Building.PlacementResult.Ok, g.World.Editor.PlaceObject(door, anchor, 0));
        var inst = world.TileObjects.At(anchor)!;
        Assert.True(ObjectInteraction.Blocks(inst));
        Assert.Equal(InteractOutcome.DoorToggled, g.World.Interaction.Interact(g.Player, inst));
        Assert.False(ObjectInteraction.Blocks(inst));

        var chestDef = g.Content.TileObjects.Get("gold_chest");
        var chest = new Strata.Core.Voxels.TileObjects.TileObjectInstance(chestDef, g.Floor + new Int3(-4, 1, -4), 0, 0) { PendingLoot = "surface_chest" };
        Assert.True(world.TileObjects.TryAdd(chest));
        TileObjectOpened(g, chest);
        var first = chest.Container!.Where(s => !s.IsEmpty).ToArray();
        Assert.NotEmpty(first);
        TileObjectOpened(g, chest);
        Assert.Equal(first, chest.Container!.Where(s => !s.IsEmpty).ToArray());
        // A container with items cannot be broken.
        g.Hold("copper_pickaxe");
        Assert.Equal(ObjectHitResult.NotEmpty, g.World.Editor.HitObject(chest, g.Player.State.Inventory.Held.Item));
    }

    private static void TileObjectOpened(TestGame g, Strata.Core.Voxels.TileObjects.TileObjectInstance chest) =>
        Assert.Equal(InteractOutcome.ContainerOpened, g.World.Interaction.Interact(g.Player, chest));
}
