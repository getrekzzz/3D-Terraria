using Godot;
using Strata.Core.Content.Loading;

namespace Strata.Game.Bootstrap;

/// <summary>Reads project files through Godot's virtual filesystem (works inside exported .pck files).</summary>
public sealed class GodotDataSource : IDataSource
{
    public bool Exists(string path) => FileAccess.FileExists("res://" + path);

    public string ReadText(string path)
    {
        var text = FileAccess.GetFileAsString("res://" + path);
        var error = FileAccess.GetOpenError();
        if (error != Error.Ok) throw new System.IO.IOException($"Cannot read res://{path}: {error}");
        return text;
    }
}
