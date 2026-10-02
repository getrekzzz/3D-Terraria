using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// The generated humans' animation sets: <c>assets/models/creatures/anim/&lt;set&gt;.glb</c>, one per distinct
/// skeleton (body, height, gait), with <c>index.json</c> naming each character model's set (built by
/// <c>tools/blender/build_all.py --anims</c>). Sets load once (on a loader thread, see <see cref="AssetCache"/>) and
/// are shared by every character on them.
/// </summary>
public static class HumanAnimations
{
    private const string Folder = "res://assets/models/creatures/anim/";
    private static Dictionary<string, string>? _index;
    private static readonly Dictionary<string, AnimationLibrary?> Sets = new();

    /// <summary>The animation set for a character model (by its project path), or null for other creatures.</summary>
    public static AnimationLibrary? For(string projectPath)
    {
        TryFor(projectPath, now: true, out var library);
        return library;
    }

    /// <summary>
    /// The animation set for a character model: Ready (null for creatures without a set), or Pending while the set is
    /// loading on a loader thread (<paramref name="now"/> waits for it instead).
    /// </summary>
    public static AssetState TryFor(string projectPath, bool now, out AnimationLibrary? library)
    {
        library = null;
        if (SetPath(projectPath) is not { } path) return AssetState.Ready;
        if (Sets.TryGetValue(path, out library)) return AssetState.Ready;
        PackedScene? packed;
        if (now) packed = AssetCache.LoadNow<PackedScene>(path);
        else if (AssetCache.TryGet(path, out packed) == AssetState.Pending) return AssetState.Pending;
        if (packed != null)
        {
            var scene = packed.Instantiate();
            if (scene.FindChild("AnimationPlayer", true, false) is AnimationPlayer player && player.HasAnimationLibrary(""))
                library = player.GetAnimationLibrary("");
            scene.Free();
        }
        if (library == null) GD.PushWarning($"Animation set {path} is missing; {projectPath} keeps its own clips.");
        Sets[path] = library;
        return AssetState.Ready;
    }

    /// <summary>The res:// path of a character model's animation set, or null for creatures without one.</summary>
    public static string? SetPath(string projectPath)
    {
        _index ??= LoadIndex();
        return _index.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(projectPath), out var set) ? Folder + set + ".glb" : null;
    }

    /// <summary>Every animation set's res:// path (preloading).</summary>
    public static IEnumerable<string> AllSetPaths()
    {
        _index ??= LoadIndex();
        var seen = new HashSet<string>();
        foreach (var set in _index.Values)
            if (seen.Add(set)) yield return Folder + set + ".glb";
    }

    private static Dictionary<string, string> LoadIndex()
    {
        var path = Folder + "index.json";
        if (!FileAccess.FileExists(path)) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(FileAccess.GetFileAsString(path)) ?? new Dictionary<string, string>();
    }
}
