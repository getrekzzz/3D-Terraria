using Strata.Core.Gameplay.Player;
using Strata.Core.Persistence;

namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    private bool _autosaveInFlight;

    /// <summary>True while an autosave batch is still being written in the background.</summary>
    public bool AutosaveInFlight => _autosaveInFlight;

    /// <summary>Saves modified chunks, world metadata, every player and their maps, and waits for the writes. Returns chunks saved.</summary>
    public int SaveAll()
    {
        if (Session.Saver is not { } saver)
        {
            Events.Persist();
            Town.Persist();
            foreach (var p in Players) SavePlayer(p.State);
            return 0;
        }
        var batch = CaptureSave();
        saver.Enqueue(batch);
        saver.Flush();
        return batch.Chunks.Count;
    }

    /// <summary>
    /// Starts a background save (the autosave): captures chunks, world state, players and maps on the main thread and
    /// leaves encoding, compression and disk writes to the save thread. <see cref="Autosaved"/> is raised on the main
    /// thread when it lands. Returns false (try again later) while the previous autosave is still being written.
    /// </summary>
    public bool BeginAutosave()
    {
        if (Session.Saver is not { } saver)
        {
            Autosaved?.Invoke(SaveAll());
            return true;
        }
        if (_autosaveInFlight) return false;
        using var profile = Diagnostics.Metrics.Frame.Time("tick.autosave");
        var batch = CaptureSave();
        _autosaveInFlight = true;
        batch.Done = chunks =>
        {
            _autosaveInFlight = false;
            Autosaved?.Invoke(chunks);
        };
        saver.Enqueue(batch);
        return true;
    }

    private SaveBatch CaptureSave()
    {
        Events.Persist();
        Town.Persist();
        var batch = Session.BeginSave();
        if (PlayerDirectory is { } dir)
            foreach (var p in Players)
            {
                if (p.Lost) continue; // a dead hardcore character is gone for good
                p.State.LastPlayedUtc = DateTime.UtcNow;
                var json = PlayerSave.From(p.State).Serialize();
                var player = p;
                // Re-checked on the save thread: a character lost after this capture must not be written back.
                batch.Files.Add(new SaveFile(PlayerPath(dir, p.State.Name), () => player.Lost ? null : System.Text.Encoding.UTF8.GetBytes(json)));
            }
        Maps.QueueSave(batch);
        return batch;
    }

    public void SavePlayer(PlayerState state)
    {
        if (PlayerDirectory == null || Players.Any(p => p.State == state && p.Lost)) return;
        WritePlayer(PlayerDirectory, state);
    }

    /// <summary>
    /// Deletes a lost character's file. It goes through the saver, after every queued write, so an autosave still in
    /// flight cannot write the character back (and the saver's own temp file is never pulled from under it).
    /// </summary>
    private void DeleteLostPlayer(string directory, string name)
    {
        if (Session.Saver is not { } saver)
        {
            DeletePlayer(directory, name);
            return;
        }
        var path = PlayerPath(directory, name);
        var batch = new SaveBatch();
        batch.Deletes.Add(path);
        batch.Deletes.Add(path + ".bak");
        batch.Deletes.Add(path + ".tmp");
        saver.Enqueue(batch);
    }

    /// <summary>World exit: lets a running autosave land, then saves and unloads everything synchronously.</summary>
    public void Shutdown()
    {
        Session.Saver?.Flush();
        SaveAll();
        Streamer.UnloadAll();
    }
}
