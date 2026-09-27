using Strata.Core.Content;
using Strata.Core.Meshing;

namespace Strata.Game.Bootstrap;

/// <summary>Process-wide services created once by <see cref="GameRoot"/> and handed to world runtimes.</summary>
public sealed class GameServices
{
    public GameServices(GodotLog log, GodotDataSource data, ContentDatabase content, TextureLayerCatalog layers, RenderTables render, CommandLine args, Rendering.GraphicsSettings graphics)
    {
        Log = log;
        Data = data;
        Content = content;
        Layers = layers;
        Render = render;
        Args = args;
        Graphics = graphics;
    }

    public GodotLog Log { get; }
    public GodotDataSource Data { get; }
    public ContentDatabase Content { get; }
    public TextureLayerCatalog Layers { get; }
    public RenderTables Render { get; }
    public CommandLine Args { get; }
    public Rendering.GraphicsSettings Graphics { get; }
}
