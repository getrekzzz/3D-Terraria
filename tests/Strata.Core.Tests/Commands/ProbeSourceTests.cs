using System.Globalization;
using Strata.Core.Commands;
using Strata.Core.Common;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Commands;

/// <summary>A service the test host hands to probe sources (engine hosts pass the launched world and log).</summary>
public sealed record ProbeAnswer(double Value);

/// <summary>A probe source that needs a host service.</summary>
[ProbeSource]
internal static class AnswerProbeSource
{
    public static void Register(CommandContext ctx, ProbeAnswer answer)
    {
        ctx.AddProbe("test.answer", () => answer.Value);
        ctx.AddPrefixProbe("test.twice.", spec => double.TryParse(spec, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v * 2 : null);
    }
}

/// <summary>A probe source that needs only the context; registered after <see cref="AnswerProbeSource"/> (name order).</summary>
[ProbeSource]
internal static class PlainProbeSource
{
    public static void Register(CommandContext ctx) => ctx.AddProbe("test.after_answer", () => ctx.ReadProbe("test.answer") + 1);
}

public class ProbeSourceTests
{
    private static (TestGame Game, CommandContext Ctx) Setup()
    {
        var g = TestGame.Create();
        return (g, new CommandContext(g.World, new HeadlessCommandHost()) { Registry = BuiltinCommands.CreateRegistry() });
    }

    [Fact]
    public void CoreSourcesAreDiscoveredByEveryContext()
    {
        var (g, ctx) = Setup();
        using var _ = g;
        Assert.Contains(typeof(TownProbes), ProbeSources.In(typeof(CommandContext).Assembly));
        foreach (var name in new[] { "town.residents", "town.homed", "town.<id>" })
            Assert.Contains(name, ctx.ProbeNames);

        var town = g.World.Town;
        var guide = town.Find("guide") ?? town.Summon(g.Content.TownNpcs.Get("guide"), g.Floor + new Int3(3, 1, 0));
        Assert.Equal(town.Residents.Count, ctx.ReadProbe("town.residents"));
        Assert.Equal(1, ctx.ReadProbe("town.guide"));
        Assert.Equal(guide.Alive ? 1 : 0, ctx.ReadProbe("town.guide.alive"));
        Assert.Throws<CommandException>(() => ctx.ReadProbe("town.no_such_resident"));
    }

    [Fact]
    public void TestAssemblySourcesAnswerOnceRegistered()
    {
        var (g, ctx) = Setup();
        using var _ = g;
        Assert.Throws<CommandException>(() => ctx.ReadProbe("test.answer")); // only Strata.Core is scanned by default

        ProbeSources.RegisterAll(ctx, typeof(ProbeSourceTests).Assembly, new ProbeAnswer(42));
        Assert.Equal(42, ctx.ReadProbe("test.answer"));
        Assert.Equal(43, ctx.ReadProbe("test.after_answer"));
        Assert.Equal(5, ctx.ReadProbe("test.twice.2.5"));
        Assert.Contains("test.twice.<id>", ctx.ProbeNames);

        var r = ctx.Registry!;
        Assert.True(r.Execute(ctx, "assert test.answer == 42").Ok);
        Assert.False(r.Execute(ctx, "assert test.answer > 42").Ok);
    }

    [Fact]
    public void HotbarProbeReadsTheSelectedSlotZeroBased()
    {
        var (g, ctx) = Setup();
        using var _ = g;
        g.Player.State.Inventory.Select(2);
        Assert.Equal(2, ctx.ReadProbe("player.hotbar"));
        g.Player.State.Inventory.Select(-1); // the wheel wraps
        Assert.Equal(9, ctx.ReadProbe("player.hotbar"));
    }

    [Fact]
    public void SourcesRegisterInFullNameOrder()
    {
        var sources = ProbeSources.In(typeof(ProbeSourceTests).Assembly);
        Assert.Equal(new[] { typeof(AnswerProbeSource), typeof(PlainProbeSource) }, sources);
    }

    [Fact]
    public void MissingServiceOrBrokenContractFailsLoudly()
    {
        var (g, ctx) = Setup();
        using var _ = g;
        var ex = Assert.Throws<InvalidOperationException>(() => ProbeSources.RegisterAll(ctx, typeof(ProbeSourceTests).Assembly));
        Assert.Contains(nameof(AnswerProbeSource), ex.Message);
        Assert.Contains(nameof(ProbeAnswer), ex.Message);

        Assert.Throws<InvalidOperationException>(() => ProbeSources.RegisterMethod(typeof(NotStatic)));
        Assert.Throws<InvalidOperationException>(() => ProbeSources.RegisterMethod(typeof(NoRegister)));
        Assert.Throws<InvalidOperationException>(() => ProbeSources.RegisterMethod(typeof(ReturnsValue)));
    }

    [Fact]
    public void ProbeNamesCannotBeShadowed()
    {
        var (g, ctx) = Setup();
        using var _ = g;
        var ex = Assert.Throws<InvalidOperationException>(() => ctx.AddProbe("TOWN.residents", () => -1)); // names ignore case
        Assert.Contains("town.residents", ex.Message, StringComparison.OrdinalIgnoreCase);
        ProbeSources.RegisterAll(ctx, typeof(ProbeSourceTests).Assembly, new ProbeAnswer(1));
        Assert.Throws<InvalidOperationException>(() => ProbeSources.RegisterAll(ctx, typeof(ProbeSourceTests).Assembly, new ProbeAnswer(2)));
        Assert.Equal(1, ctx.ReadProbe("test.answer"));
    }

    private sealed class NotStatic
    {
        public static void Register(CommandContext ctx) => ctx.AddProbe("never", () => 0);
    }

    private static class NoRegister
    {
        public static void Add(CommandContext ctx) => ctx.AddProbe("never", () => 0);
    }

    private static class ReturnsValue
    {
        public static int Register(CommandContext ctx) => 0;
    }
}
