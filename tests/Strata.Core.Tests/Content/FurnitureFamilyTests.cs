using System.Text.Json;
using Strata.Core.Tests.Support;

namespace Strata.Core.Tests.Content;

/// <summary>The furniture families (tools/content/furniture_families.json → data/items/generated/furniture_families.json).</summary>
public class FurnitureFamilyTests
{
    private static readonly JsonElement Table = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(TestPaths.ProjectRoot, "tools", "content", "furniture_families.json"))).RootElement;

    private static IEnumerable<(string Family, string Piece, string Id)> Pieces()
    {
        foreach (var fam in Table.GetProperty("families").EnumerateArray())
        {
            string id = fam.GetProperty("id").GetString()!;
            foreach (var piece in Table.GetProperty("pieces").EnumerateObject())
            {
                string pid = Override(fam, "existing", piece.Name) ?? Override(fam, "ids", piece.Name) ?? $"{id}_{piece.Name}";
                yield return (id, piece.Name, pid);
            }
        }
    }

    private static string? Override(JsonElement fam, string key, string piece) =>
        fam.TryGetProperty(key, out var map) && map.TryGetProperty(piece, out var id) ? id.GetString() : null;

    [Fact]
    public void EveryFamilyIsCompleteAndCraftable()
    {
        var content = TestPaths.Content;
        int count = 0;
        foreach (var (family, piece, id) in Pieces())
        {
            Assert.True(content.TileObjects.Contains(id), $"{family} has no {piece} ({id})");
            Assert.True(content.Items.Contains(id), $"{id} has no item");
            Assert.True(content.Recipes.All.Any(r => r.Output == id), $"{id} cannot be crafted");
            count++;
        }
        Assert.True(count >= 14 * 17, $"only {count} pieces");
    }

    [Fact]
    public void PiecesFurnishAHomeAndLight()
    {
        var content = TestPaths.Content;
        foreach (var (family, piece, id) in Pieces())
        {
            var def = content.TileObjects.Get(id);
            switch (piece)
            {
                case "door": Assert.Contains("door", def.Housing); break;
                case "chair" or "sofa": Assert.Contains("chair", def.Housing); break;
                case "table" or "workbench" or "bookcase" or "piano": Assert.Contains("table", def.Housing); break;
                case "lamp" or "lantern" or "candle" or "candelabra" or "chandelier":
                    Assert.Contains("light", def.Housing);
                    Assert.NotNull(def.Light);
                    break;
                case "platform": Assert.Equal(Strata.Core.Content.Defs.TileCollision.Platform, def.Collision); break;
            }
        }
    }

    [Fact]
    public void GeneratedFileIsUpToDate()
    {
        var psi = new System.Diagnostics.ProcessStartInfo("python3", "tools/content/gen_furniture.py --check")
        {
            WorkingDirectory = TestPaths.ProjectRoot, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, output);
    }
}
