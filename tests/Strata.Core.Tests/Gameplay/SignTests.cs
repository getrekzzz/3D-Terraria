using Strata.Core.Common;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Tests.Support;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Tests.Gameplay;

/// <summary>Editable signs (X-03): text is sanitised, opened by interacting and saved in the chunk's object meta.</summary>
public class SignTests
{
    [Fact]
    public void TextIsTrimmedToSixLinesAndTheLengthLimit()
    {
        Assert.Null(TileObjectInstance.SanitizeSign("   \n "));
        Assert.Equal("a\nb", TileObjectInstance.SanitizeSign(" a\r\nb\t"));
        Assert.Equal(6, TileObjectInstance.SanitizeSign(string.Join("\n", Enumerable.Range(1, 9)))!.Split('\n').Length);
        Assert.Equal(TileObjectInstance.MaxSignLength, TileObjectInstance.SanitizeSign(new string('x', 1000))!.Length);
    }

    [Fact]
    public void ReadingASignOpensItAndItsTextSurvivesSaving()
    {
        var dir = TestWorlds.TempDir();
        Int3 anchor;
        using (var g = TestGame.Create(dir))
        {
            g.World.Spawner.Enabled = false;
            anchor = g.Floor + new Int3(3, 1, 3);
            var sign = new TileObjectInstance(g.Content.TileObjects.Get("sign"), anchor, 0, 0);
            Assert.True(g.World.Session.World.TileObjects.TryAdd(sign));
            TileObjectInstance? opened = null;
            g.World.Interaction.SignOpened += (_, s) => opened = s;
            Assert.Equal(InteractOutcome.SignOpened, g.World.Interaction.Interact(g.Player, sign));
            Assert.Same(sign, opened);
            Assert.True(g.World.EditSign(sign, "Welcome home\nKeep out of the caves"));
            Assert.False(g.World.EditSign(new TileObjectInstance(g.Content.TileObjects.Get("wooden_chair"), anchor, 0, 0), "no"));
            g.World.SaveAll();
        }
        var session = Strata.Core.Session.WorldSession.Load(TestPaths.Content, dir, new Strata.Core.Meshing.RenderTables(TestPaths.Content, _ => 0),
            new Strata.Core.Diagnostics.MemoryLog());
        session.Install(session.Produce(ChunkMath.CellToChunk(anchor)));
        var loaded = session.World.TileObjects.At(anchor);
        Assert.NotNull(loaded);
        Assert.Equal("Welcome home\nKeep out of the caves", loaded!.SignText);
    }
}
