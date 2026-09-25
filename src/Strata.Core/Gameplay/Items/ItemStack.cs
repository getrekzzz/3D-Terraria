using Strata.Core.Content.Defs;

namespace Strata.Core.Gameplay.Items;

/// <summary>A quantity of one item type, optionally with a reforge prefix.</summary>
public readonly struct ItemStack : IEquatable<ItemStack>
{
    public ItemStack(ItemDef item, int count, PrefixDef? prefix = null)
    {
        Item = count > 0 ? item : null;
        Count = count > 0 ? count : 0;
        Prefix = count > 0 ? prefix : null;
    }

    public static readonly ItemStack Empty = default;

    public ItemDef? Item { get; }
    public int Count { get; }
    public PrefixDef? Prefix { get; }

    public bool IsEmpty => Item == null || Count <= 0;
    public int SpaceLeft => IsEmpty ? 0 : Item!.MaxStack - Count;

    public ItemStack WithCount(int count) => IsEmpty || count <= 0 ? Empty : new ItemStack(Item!, count, Prefix);

    /// <summary>Can <paramref name="other"/> merge into this stack?</summary>
    public bool CanStackWith(ItemStack other) =>
        !IsEmpty && !other.IsEmpty && Item == other.Item && Prefix == other.Prefix && Item!.MaxStack > 1;

    public bool Equals(ItemStack other) => Item == other.Item && Count == other.Count && Prefix == other.Prefix;
    public override bool Equals(object? obj) => obj is ItemStack s && Equals(s);
    public override int GetHashCode() => HashCode.Combine(Item?.Id, Count, Prefix?.Id);
    public override string ToString() => IsEmpty ? "(empty)" : $"{Count}x {(Prefix != null ? Prefix.DisplayName + " " : "")}{Item!.DisplayName}";
}
