using Strata.Core.Commands;
using Strata.Core.Common;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Gameplay;

public class CommandTests
{
    private static (TestGame Game, CommandRegistry Registry, CommandContext Ctx) Setup()
    {
        var g = TestGame.Create();
        var registry = BuiltinCommands.CreateRegistry();
        var ctx = new CommandContext(g.World, new HeadlessCommandHost()) { Registry = registry };
        return (g, registry, ctx);
    }

    [Fact]
    public void EveryCommandHasHelpAndUsage()
    {
        var (g, r, ctx) = Setup();
        using var _ = g;
        foreach (var c in r.Commands)
        {
            var help = r.Execute(ctx, $"help {c.Name}");
            Assert.True(help.Ok, help.Output);
            Assert.Contains(c.Usage, help.Output);
        }
        Assert.True(r.Execute(ctx, "help").Ok);
        Assert.True(r.Execute(ctx, "# comment").Ok);
        Assert.False(r.Execute(ctx, "nonsense").Ok);
    }

    [Fact]
    public void GiveAssertAndUseWorkTogether()
    {
        var (g, r, ctx) = Setup();
        using var _ = g;
        Assert.True(r.Execute(ctx, "give gray_brick 10").Ok);
        Assert.True(r.Execute(ctx, "assert inv.gray_brick == 10").Ok);
        Assert.False(r.Execute(ctx, "assert inv.gray_brick > 10").Ok);
        Assert.False(r.Execute(ctx, "give no_such_item").Ok);

        int slot = g.Player.State.Inventory.Backpack.Stacks.Count() - 1;
        Assert.True(r.Execute(ctx, $"select {slot + 1}").Ok);
        Assert.Equal("gray_brick", g.Player.State.Inventory.Held.Item!.Id);
        Assert.True(r.Execute(ctx, "aim 0 -0.8 1").Ok);
        var used = r.Execute(ctx, "use");
        Assert.StartsWith("PlacedBlock", used.Output);
        Assert.True(r.Execute(ctx, "assert inv.gray_brick < 10").Ok); // one per filled cell of the grid block
    }

    [Fact]
    public void EditTimeFlagAndTeleportCommands()
    {
        var (g, r, ctx) = Setup();
        using var _ = g;
        Assert.True(r.Execute(ctx, "setblock 2 0 0 gray_brick slope 1").Ok);
        Assert.Equal(g.Content.BlockId("gray_brick"), g.World.Session.World.GetBlock(g.Floor + new Int3(2, 1, 0)));
        Assert.True(r.Execute(ctx, "fill -1 0 -1 1 0 1 dirt").Ok);
        Assert.True(r.Execute(ctx, "assert block.1.0.1 > 0").Ok);
        Assert.True(r.Execute(ctx, "time midnight").Ok);
        Assert.True(r.Execute(ctx, "assert time.day == 0").Ok);
        var flag = g.Content.Flags.All.First().Id;
        Assert.True(r.Execute(ctx, $"flag {flag}").Ok);
        Assert.True(r.Execute(ctx, $"assert flag.{flag} == 1").Ok);
        Assert.True(r.Execute(ctx, "tp 100 300 100").Ok);
        Assert.True(r.Execute(ctx, "assert player.cell_y == 300").Ok);
        Assert.False(r.Execute(ctx, "tp 1 2").Ok);
        // ~ is relative to the feet cell, and the player lands in the middle of the cell.
        ctx.Player.Position += new System.Numerics.Vector3(0.2f, 0f, 0.24f);
        Assert.True(r.Execute(ctx, "tp ~ ~ ~2").Ok);
        Assert.Equal(new Int3(100, 300, 102), ctx.Player.FeetCell);
        Assert.Equal(BuiltinCommands.Feet(new Int3(100, 300, 102)), ctx.Player.Position);
        Assert.False(r.Execute(ctx, "tp ~ ~x ~").Ok);
        Assert.True(r.Execute(ctx, "inspect").Ok);
    }

    [Fact]
    public void RegenRestoresGeneratedTerrain()
    {
        var (g, r, ctx) = Setup();
        using var _ = g;
        var world = g.World.Session.World;
        var chunk = ChunkMath.CellToChunk(g.Floor);
        var expected = g.World.Session.Generator.Generate(chunk).Data;
        Assert.True(r.Execute(ctx, "regen").Ok);
        var origin = ChunkMath.ChunkOrigin(chunk);
        for (int i = 0; i < 2000; i++)
        {
            int x = (i * 7) & 31, y = (i * 13) & 31, z = (i * 29) & 31;
            Assert.Equal(expected.GetBlock(ChunkMath.Index(x, y, z)), world.GetBlock(origin.X + x, origin.Y + y, origin.Z + z));
        }
    }
}
