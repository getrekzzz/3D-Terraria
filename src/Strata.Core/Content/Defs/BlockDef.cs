namespace Strata.Core.Content.Defs;

/// <summary>How a block is meshed. See docs/architecture/voxels-and-meshing.md.</summary>
public enum BlockRender
{
    /// <summary>Not rendered (air).</summary>
    None,

    /// <summary>Natural material: rendered as part of the smooth surface-nets terrain.</summary>
    Smooth,

    /// <summary>Built material: rendered as crisp cube faces flush with the voxel grid.</summary>
    Crisp,

    /// <summary>Crisp but see-through (glass, ice, webs); never occludes neighbours.</summary>
    Transparent,
}

/// <summary>Texture layer names for a block (texture-array layers produced by tools/textures).</summary>
public sealed class BlockTextures
{
    public string? All { get; init; }
    public string? Top { get; init; }
    public string? Side { get; init; }
    public string? Bottom { get; init; }

    public string TopLayer => Top ?? All ?? Side ?? "missing";
    public string SideLayer => Side ?? All ?? Top ?? "missing";
    public string BottomLayer => Bottom ?? All ?? Side ?? "missing";
}

/// <summary>A voxel material (one cell of terrain or construction).</summary>
public sealed class BlockDef : ContentDef
{
    public BlockRender Render { get; init; } = BlockRender.Smooth;

    /// <summary>Collides with entities. Defaults to true for any rendered block.</summary>
    public bool? Solid { get; init; }

    public BlockTextures Textures { get; init; } = new();

    /// <summary>Mining health multiplier (1 = dirt-like). Hits needed scale with this.</summary>
    public float Hardness { get; init; } = 1f;

    /// <summary>Minimum pickaxe power required to mine.</summary>
    public int PickPower { get; init; }

    /// <summary>Item dropped when mined. Defaults to the item with the same id, if any.</summary>
    public string? Drop { get; init; }

    /// <summary>Emitted light as [r, g, b], each 0–15.</summary>
    public int[]? Light { get; init; }

    /// <summary>
    /// Soft self-glow of the block's surface (0–1, shader only): ore flecks stay visible in a dark cave without lighting
    /// anything around them (unlike <see cref="Light"/>, it never enters the baked block light).
    /// </summary>
    public float? Glow { get; init; }

    public string Sound { get; init; } = "stone";

    /// <summary>Particle/minimap colour as #RRGGBB.</summary>
    public string Color { get; init; } = "#808080";

    public float Friction { get; init; } = 1f;

    /// <summary>Falls when unsupported (sand-like).</summary>
    public bool Falls { get; init; }

    /// <summary>Players can climb it (rope): no gravity inside, jump climbs up and crouch slides down.</summary>
    public bool Climbable { get; init; }

    /// <summary>Movement speed multiplier while inside a non-solid block of this type (webs).</summary>
    public float MoveDrag { get; init; } = 1f;

    /// <summary>Damage per second dealt to entities touching this block (thorns, cinder).</summary>
    public int ContactDamage { get; init; }

    /// <summary>
    /// Automatically derive a placeable block item with the same id (unless the block drops something
    /// else or an explicit item exists).
    /// </summary>
    public bool AutoItem { get; init; } = true;

    /// <summary>Value (copper) of the derived block item.</summary>
    public int ItemValue { get; init; }

    public bool IsSolid => Solid ?? Render != BlockRender.None;

    /// <summary>Participates in the smooth/crisp opaque surface (occludes and closes the terrain mesh).</summary>
    public bool IsOpaque => Render is BlockRender.Smooth or BlockRender.Crisp;

    public bool EmitsLight => Light is { Length: 3 } l && (l[0] | l[1] | l[2]) != 0;
}
