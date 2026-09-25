namespace Strata.Core.Content.Loading;

/// <summary>
/// Read-only access to data files by project-relative path (e.g. "data/blocks/natural.json").
/// The game implements this over Godot's virtual filesystem (res://, works inside exported .pck files);
/// tests and tools use <see cref="FileSystemDataSource"/>.
/// </summary>
public interface IDataSource
{
    bool Exists(string path);

    string ReadText(string path);
}

/// <summary>Reads files relative to a root directory on disk.</summary>
public sealed class FileSystemDataSource : IDataSource
{
    public string Root { get; }

    public FileSystemDataSource(string root) => Root = Path.GetFullPath(root);

    public bool Exists(string path) => File.Exists(Resolve(path));

    public string ReadText(string path) => File.ReadAllText(Resolve(path));

    private string Resolve(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Walks up from <paramref name="start"/> until a directory containing project.godot is found.</summary>
    public static string FindProjectRoot(string start)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "project.godot"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"No project.godot found above '{start}'.");
    }
}
