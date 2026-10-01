using Strata.Core.Gameplay.Player;
using Strata.Core.Persistence;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>Writes a character's save file (new characters from the menu, saves during play).</summary>
    public static void WritePlayer(string directory, PlayerState state)
    {
        Directory.CreateDirectory(directory);
        state.LastPlayedUtc = DateTime.UtcNow;
        AtomicFile.WriteAllText(PlayerPath(directory, state.Name), PlayerSave.From(state).Serialize());
    }

    /// <summary>Loads a player file, or creates a new character with the starting kit.</summary>
    public static PlayerState LoadOrCreatePlayer(string? directory, string name, Content.ContentDatabase content)
    {
        if (directory != null)
        {
            var bytes = AtomicFile.ReadWithFallback(PlayerPath(directory, name));
            if (bytes != null)
            {
                var save = PlayerSave.Deserialize(System.Text.Encoding.UTF8.GetString(bytes));
                return save.ToState(content);
            }
        }
        return PlayerState.CreateNew(name, content);
    }

    /// <summary>Deletes a player's file and its backup (fresh characters for tests).</summary>
    public static void DeletePlayer(string directory, string name)
    {
        var path = PlayerPath(directory, name);
        foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
            if (File.Exists(p)) File.Delete(p);
    }

    public static string PlayerPath(string directory, string name)
    {
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        return Path.Combine(directory, safe + ".player.json");
    }
}
