using Strata.Core.Gameplay.Player;
using Strata.Core.World;

namespace Strata.Core.Persistence;

/// <summary>A saved world as the world list shows it.</summary>
public sealed record WorldEntry(string Directory, string Name, long Seed, WorldSizePreset Size, Difficulty Difficulty, EvilKind Evil,
    DateTime LastPlayedUtc, long PlayTicks, bool Hardmode);

/// <summary>A saved character as the character list shows it.</summary>
public sealed record CharacterEntry(string Path, string Name, CharacterDifficulty Difficulty, int MaxLife, int MaxMana,
    long PlayTicks, Appearance Look, DateTime LastPlayedUtc);

/// <summary>
/// Lists, names and deletes saved worlds (one directory each, holding world.json) and characters
/// (&lt;name&gt;.player.json). Unreadable saves are skipped rather than failing the whole list.
/// </summary>
public static class SaveCatalog
{
    public static List<WorldEntry> Worlds(string worldsDirectory, Action<string, Exception>? onError = null)
    {
        var list = new List<WorldEntry>();
        if (!System.IO.Directory.Exists(worldsDirectory)) return list;
        foreach (var dir in System.IO.Directory.GetDirectories(worldsDirectory))
        {
            var store = new WorldSaveStore(dir);
            if (!store.Exists) continue;
            try
            {
                var m = store.LoadMeta();
                list.Add(new WorldEntry(dir, m.Name, m.Seed, m.Size, m.Difficulty, m.Evil, m.LastPlayedUtc, m.PlayTicks, m.Flags.Contains("hardmode")));
            }
            catch (Exception ex)
            {
                onError?.Invoke(dir, ex);
            }
        }
        return list.OrderByDescending(w => w.LastPlayedUtc).ToList();
    }

    public static List<CharacterEntry> Characters(string playersDirectory, Action<string, Exception>? onError = null)
    {
        var list = new List<CharacterEntry>();
        if (!System.IO.Directory.Exists(playersDirectory)) return list;
        foreach (var path in System.IO.Directory.GetFiles(playersDirectory, "*.player.json"))
        {
            try
            {
                var bytes = AtomicFile.ReadWithFallback(path);
                if (bytes == null) continue;
                var s = PlayerSave.Deserialize(System.Text.Encoding.UTF8.GetString(bytes));
                int life = s.MaxLife > 0 ? s.MaxLife : PlayerState.BaseLife + s.BonusLife;
                int mana = s.MaxMana > 0 ? s.MaxMana : PlayerState.BaseMana + s.BonusMana;
                list.Add(new CharacterEntry(path, s.Name, s.Difficulty, life, mana, s.PlayTicks, s.Look ?? new Appearance(), s.LastPlayedUtc));
            }
            catch (Exception ex)
            {
                onError?.Invoke(path, ex);
            }
        }
        return list.OrderByDescending(c => c.LastPlayedUtc).ToList();
    }

    /// <summary>The directory a new world with this name is saved in (unique: a number is appended if taken).</summary>
    public static string NewWorldDirectory(string worldsDirectory, string name)
    {
        var baseName = SafeName(name);
        var dir = Path.Combine(worldsDirectory, baseName);
        for (int i = 2; System.IO.Directory.Exists(dir); i++) dir = Path.Combine(worldsDirectory, $"{baseName}_{i}");
        return dir;
    }

    public static void DeleteWorld(string directory)
    {
        if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, recursive: true);
    }

    public static void DeleteCharacter(string path)
    {
        foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
            if (File.Exists(p)) File.Delete(p);
    }

    /// <summary>A file-system-safe form of a display name (letters, digits, '_' and '-').</summary>
    public static string SafeName(string name)
    {
        var safe = new string(name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        return safe.Length == 0 ? "world" : safe;
    }

    /// <summary>Play time as the lists show it, e.g. "3h 12m".</summary>
    public static string PlayTime(long ticks)
    {
        long minutes = ticks / (Common.Units.TicksPerSecond * 60L);
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";
    }
}
