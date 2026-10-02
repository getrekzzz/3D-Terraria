using Strata.Core.Content.Defs;

namespace Strata.Core.Content.Loading;

/// <summary>
/// Derives implied definitions so data never repeats itself: a placeable block implies a block item,
/// and a placeable tile object implies the item that places it.
/// Explicit definitions always win.
/// </summary>
internal static class DerivedContent
{
    public static void Apply(ContentDatabase db)
    {
        foreach (var block in db.Blocks.All)
        {
            if (block.Id == ContentDatabase.AirId || !block.AutoItem || block.Drop != null || db.Items.Contains(block.Id)) continue;
            db.Items.TryAdd(new ItemDef
            {
                Id = block.Id,
                Name = block.Name,
                Description = block.Description,
                Category = ItemCategory.Block,
                MaxStack = ItemDef.CommonMaxStack,
                Value = block.ItemValue,
                UseStyle = UseStyle.Place,
                UseTime = 12,
                UseAnimation = 12,
                AutoReuse = true,
                Consumable = true,
                PlaceBlock = block.Id,
                HeldLight = block.Light,
                Covers = block.Covers,
                Tags = new[] { "derived" },
            });
            db.Items.Find(block.Id)!.SourceFile = block.SourceFile;
        }

        foreach (var obj in db.TileObjects.All)
        {
            var itemId = obj.PlacingItemId;
            if (!obj.AutoItem || itemId == null || db.Items.Contains(itemId)) continue;
            db.Items.TryAdd(new ItemDef
            {
                Id = itemId,
                Name = obj.Name,
                Description = obj.Description,
                Category = obj.ItemCategory,
                MaxStack = obj.ItemMaxStack,
                Value = obj.ItemValue,
                UseStyle = UseStyle.Place,
                UseTime = 15,
                UseAnimation = 15,
                AutoReuse = true,
                Consumable = true,
                PlaceObject = obj.Id,
                HeldLight = obj.Light,
                Covers = obj.Covers,
                Tags = obj.Tags.Append("derived").ToArray(), // e.g. "shop" for stations only sold by a resident
            });
            db.Items.Find(itemId)!.SourceFile = obj.SourceFile;
        }
    }
}
