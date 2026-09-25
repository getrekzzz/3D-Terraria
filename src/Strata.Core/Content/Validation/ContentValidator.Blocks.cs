using Strata.Core.Content.Defs;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    private static void ValidateBlocks(Ctx ctx)
    {
        foreach (var b in ctx.Db.Blocks.All)
        {
            if (b.Id == ContentDatabase.AirId) continue;
            if (b.Render == BlockRender.None) ctx.Warn(b, "render 'none' block other than air.");
            if (b.Hardness <= 0) ctx.Error(b, "hardness must be > 0.");
            if (b.PickPower < 0) ctx.Error(b, "pickPower must be >= 0.");
            ctx.Light(b, b.Light, "light");
            ctx.Color(b, b.Color, "color");
            ctx.Ref(b, ctx.Db.Items, b.Drop, "drop");
            if (b.Render != BlockRender.None)
            {
                var t = b.Textures;
                if (t.All == null && t.Top == null && t.Side == null) ctx.Error(b, "rendered block needs textures.");
                if (ctx.Options.TextureLayers is { } layers)
                {
                    foreach (var layer in new[] { t.TopLayer, t.SideLayer, t.BottomLayer }.Distinct())
                        if (!layers.Contains(layer)) ctx.Error(b, $"texture layer '{layer}' is not in the terrain atlas.");
                }
            }
        }
    }

    private static void ValidateOreVeins(Ctx ctx)
    {
        foreach (var o in ctx.Db.OreVeins.All)
        {
            ctx.Ref(o, ctx.Db.Blocks, o.Block, "block");
            foreach (var r in o.Replace) ctx.Ref(o, ctx.Db.Blocks, r, "replace");
            foreach (var b in o.Biomes) ctx.Ref(o, ctx.Db.Biomes, b, "biomes");
            if (o.DepthMin > o.DepthMax) ctx.Error(o, "depthMin must be <= depthMax.");
            if (o.PerChunk < 0) ctx.Error(o, "perChunk must be >= 0.");
            if (o.SizeMin <= 0 || o.SizeMax < o.SizeMin || o.SizeMax > 12) ctx.Error(o, "size must be [min>0, max>=min, max<=12].");
        }
    }

    private static void ValidateTileObjects(Ctx ctx)
    {
        foreach (var t in ctx.Db.TileObjects.All)
        {
            if (t.Size.Length != 3 || t.Size.Any(v => v < 1 || v > 64)) ctx.Error(t, "size must be [x, y, z] with values 1–64.");
            ctx.Ref(t, ctx.Db.Items, t.Item, "item");
            ctx.Ref(t, ctx.Db.LootTables, t.Loot, "loot");
            ctx.Light(t, t.Light, "light");
            ctx.Asset(t, t.Model, "model");
            if (t.Variants < 1) ctx.Error(t, "variants must be >= 1.");
            if (t.HitPoints < 1) ctx.Error(t, "hitPoints must be >= 1.");
            if (t.Interaction == TileInteraction.Chest && t.ContainerSlots <= 0) ctx.Error(t, "chest needs containerSlots.");
            if (t.Interaction == TileInteraction.Station && t.Stations.Length == 0) ctx.Error(t, "station needs 'stations'.");
            foreach (var h in t.Housing)
                if (h is not ("door" or "light" or "table" or "chair" or "bed" or "comfort"))
                    ctx.Error(t, $"unknown housing role '{h}'.");
        }
    }
}
