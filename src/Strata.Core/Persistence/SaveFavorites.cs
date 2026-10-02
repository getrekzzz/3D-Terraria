using System.Text.Json.Nodes;

namespace Strata.Core.Persistence;

/// <summary>
/// The characters and worlds starred on the select screens, as Terraria's favorites.json: one small file beside the
/// saves, keyed by each save's file name (characters) or directory name (worlds), so the saves themselves stay
/// untouched. Starred saves are listed first. A missing or unreadable file is an empty list; every change is written
/// at once.
/// </summary>
public sealed class SaveFavorites
{
    private readonly HashSet<string> _characters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _worlds = new(StringComparer.Ordinal);

    private SaveFavorites(string path) => Path = path;

    /// <summary>The favourites file.</summary>
    public string Path { get; }

    /// <summary>Reads <paramref name="path"/> (or its backup); an unreadable file gives an empty list.</summary>
    public static SaveFavorites Load(string path)
    {
        var favorites = new SaveFavorites(path);
        var bytes = AtomicFile.ReadWithFallback(path, IsJsonObject);
        if (bytes == null) return favorites;
        var root = JsonNode.Parse(bytes)!.AsObject();
        Read(root["characters"], favorites._characters);
        Read(root["worlds"], favorites._worlds);
        return favorites;
    }

    public bool IsCharacter(string characterPath) => _characters.Contains(Key(characterPath));

    public bool IsWorld(string worldDirectory) => _worlds.Contains(Key(worldDirectory));

    /// <summary>Stars or unstars a character save and writes the file; returns whether it is starred now.</summary>
    public bool ToggleCharacter(string characterPath) => Toggle(_characters, characterPath);

    /// <summary>Stars or unstars a world and writes the file; returns whether it is starred now.</summary>
    public bool ToggleWorld(string worldDirectory) => Toggle(_worlds, worldDirectory);

    /// <summary>Drops a deleted character's star (a new save with the same name starts unstarred).</summary>
    public void ForgetCharacter(string characterPath)
    {
        if (_characters.Remove(Key(characterPath))) Save();
    }

    /// <summary>Drops a deleted world's star.</summary>
    public void ForgetWorld(string worldDirectory)
    {
        if (_worlds.Remove(Key(worldDirectory))) Save();
    }

    /// <summary>The starred entries first, each group keeping the order it had (most recently played first).</summary>
    public static List<T> StarredFirst<T>(IEnumerable<T> entries, Func<T, bool> starred)
    {
        var all = entries.ToList();
        var list = new List<T>(all.Count);
        list.AddRange(all.Where(starred));
        list.AddRange(all.Where(e => !starred(e)));
        return list;
    }

    private bool Toggle(HashSet<string> set, string path)
    {
        string key = Key(path);
        bool now = set.Add(key) || !set.Remove(key);
        Save();
        return now;
    }

    private void Save()
    {
        var root = new JsonObject
        {
            ["characters"] = Array(_characters),
            ["worlds"] = Array(_worlds),
        };
        AtomicFile.WriteAllText(Path, root.ToJsonString(SaveJson.Options));
    }

    private static JsonArray Array(HashSet<string> set)
    {
        var array = new JsonArray();
        foreach (var key in set.OrderBy(k => k, StringComparer.Ordinal)) array.Add(JsonValue.Create(key));
        return array;
    }

    private static void Read(JsonNode? node, HashSet<string> into)
    {
        if (node is not JsonArray array) return;
        foreach (var item in array)
            if (item is JsonValue v && v.TryGetValue(out string? key) && !string.IsNullOrEmpty(key)) into.Add(key);
    }

    /// <summary>A save's key: its file or directory name (paths differ between machines, names do not).</summary>
    private static string Key(string path) => System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));

    private static bool IsJsonObject(byte[] bytes)
    {
        try
        {
            return JsonNode.Parse(bytes) is JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
