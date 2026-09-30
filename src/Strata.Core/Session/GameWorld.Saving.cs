using Strata.Core.Gameplay.Player;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>Saves modified chunks, world metadata and every player. Returns chunks written.</summary>
    public int SaveAll()
    {
        Events.Persist();
        Town.Persist();
        int chunks = Session.SaveAll();
        foreach (var p in Players) SavePlayer(p.State);
        Maps.Save();
        return chunks;
    }

    public void SavePlayer(PlayerState state)
    {
        if (PlayerDirectory == null || Players.Any(p => p.State == state && p.Lost)) return;
        WritePlayer(PlayerDirectory, state);
    }
}
