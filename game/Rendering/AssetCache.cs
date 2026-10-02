using System.Collections.Generic;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>What <see cref="AssetCache.TryGet{T}"/> found.</summary>
public enum AssetState
{
    Ready,
    Pending,
    Missing,
}

/// <summary>
/// Loads models and animation sets on Godot's loader threads so a creature, boss or prop seen for the first time no
/// longer stalls the frame that meets it. <see cref="TryGet{T}"/> starts a threaded load and reports Pending until
/// <see cref="Pump"/> (run every frame by <see cref="AssetPump"/>) has moved the finished resource into a
/// strong-reference cache. <see cref="LoadNow{T}"/> loads synchronously (finishing a pending request) for the player's
/// own body, held items and menus, which must appear in the frame they are asked for.
/// Main thread only: the loader threads only parse resources; every node is instantiated by the caller on the main
/// thread.
/// </summary>
public static class AssetCache
{
    private static readonly Dictionary<string, Resource> Loaded = new();
    private static readonly Dictionary<string, bool> Exists = new();
    private static readonly HashSet<string> Missing = new();
    private static readonly List<string> Requests = new();
    private static readonly HashSet<string> Requested = new();

    /// <summary>Threaded loads not yet moved into the cache (probe <c>assets.pending</c>).</summary>
    public static int PendingCount => Requests.Count;

    /// <summary>Resources held by the cache (probe <c>assets.loaded</c>).</summary>
    public static int LoadedCount => Loaded.Count;

    /// <summary>
    /// The resource when it is loaded; otherwise starts (or continues) a threaded load and reports Pending, or Missing
    /// when the file does not exist or failed to load.
    /// </summary>
    public static AssetState TryGet<T>(string resPath, out T? resource) where T : Resource
    {
        resource = null;
        if (Loaded.TryGetValue(resPath, out var found))
        {
            resource = found as T;
            return resource != null ? AssetState.Ready : AssetState.Missing;
        }
        if (Missing.Contains(resPath) || !FileExists(resPath)) return AssetState.Missing;
        if (Requested.Contains(resPath)) return AssetState.Pending;
        if (ResourceLoader.HasCached(resPath))
        {
            // Already in Godot's cache (loaded elsewhere): taking it is cheap.
            resource = LoadNow<T>(resPath);
            return resource != null ? AssetState.Ready : AssetState.Missing;
        }
        Request(resPath);
        return Requested.Contains(resPath) ? AssetState.Pending : AssetState.Missing;
    }

    /// <summary>True while a threaded load of the path is in flight.</summary>
    public static bool IsPending(string resPath) => Requested.Contains(resPath);

    /// <summary>Starts a threaded load (preloading); a no-op for loaded, pending or missing paths.</summary>
    public static void Request(string resPath)
    {
        if (Loaded.ContainsKey(resPath) || Requested.Contains(resPath) || Missing.Contains(resPath)) return;
        if (!FileExists(resPath)) return;
        if (ResourceLoader.LoadThreadedRequest(resPath, "", false) != Error.Ok)
        {
            Fail(resPath);
            return;
        }
        Requested.Add(resPath);
        Requests.Add(resPath);
    }

    /// <summary>Loads synchronously (waiting for a pending request); null when the file is missing or broken.</summary>
    public static T? LoadNow<T>(string resPath) where T : Resource
    {
        if (Loaded.TryGetValue(resPath, out var found)) return found as T;
        if (Missing.Contains(resPath) || !FileExists(resPath)) return null;
        Resource? res;
        if (Requested.Remove(resPath))
        {
            Requests.Remove(resPath);
            res = ResourceLoader.LoadThreadedGet(resPath); // blocks until the loader thread is done
        }
        else res = ResourceLoader.Load(resPath);
        if (res == null)
        {
            Fail(resPath);
            return null;
        }
        Loaded[resPath] = res;
        return res as T;
    }

    /// <summary>
    /// Moves finished threaded loads into the cache, at most <paramref name="maxPerFrame"/> per call (taking a large
    /// resource still costs a little main-thread time).
    /// </summary>
    public static void Pump(int maxPerFrame = 4)
    {
        int moved = 0;
        for (int i = Requests.Count - 1; i >= 0 && moved < maxPerFrame; i--)
        {
            var path = Requests[i];
            var status = ResourceLoader.LoadThreadedGetStatus(path);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress) continue;
            Requests.RemoveAt(i);
            Requested.Remove(path);
            moved++;
            var res = status == ResourceLoader.ThreadLoadStatus.Loaded ? ResourceLoader.LoadThreadedGet(path) : null;
            if (res != null) Loaded[path] = res;
            else Fail(path);
        }
    }

    /// <summary>
    /// Drops every held resource (the world closed), first finishing the threaded loads still in flight so none is
    /// orphaned. Resources still used elsewhere stay alive through their users.
    /// </summary>
    public static void Clear()
    {
        foreach (var path in Requests) ResourceLoader.LoadThreadedGet(path);
        Requests.Clear();
        Requested.Clear();
        Loaded.Clear();
    }

    private static void Fail(string resPath)
    {
        if (Missing.Add(resPath)) GD.PushWarning($"Asset {resPath} failed to load.");
    }

    private static bool FileExists(string resPath)
    {
        if (!Exists.TryGetValue(resPath, out var exists)) Exists[resPath] = exists = ResourceLoader.Exists(resPath);
        return exists;
    }
}
