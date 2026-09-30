using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// The generated humans' animation sets: <c>assets/models/creatures/anim/&lt;set&gt;.glb</c>, one per distinct
/// skeleton (body, height, gait), with <c>index.json</c> naming each character model's set (built by
/// <c>tools/blender/build_all.py --anims</c>). Sets load once and are shared by every character on them.
/// </summary>
public static class HumanAnimations
{
    private const string Folder = "res://assets/models/creatures/anim/";
    private static Dictionary<string, string>? _index;
    private static readonly Dictionary<string, AnimationLibrary?> Sets = new();

    /// <summary>The animation set for a character model (by its project path), or null for other creatures.</summary>
    public static AnimationLibrary? For(string projectPath)
    {
        _index ??= LoadIndex();
        if (!_index.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(projectPath), out var set)) return null;
        if (Sets.TryGetValue(set, out var cached)) return cached;
        AnimationLibrary? library = null;
        var path = Folder + set + ".glb";
        if (ResourceLoader.Exists(path))
        {
            var scene = GD.Load<PackedScene>(path).Instantiate();
            if (scene.FindChild("AnimationPlayer", true, false) is AnimationPlayer player && player.HasAnimationLibrary(""))
                library = player.GetAnimationLibrary("");
            scene.Free();
        }
        if (library == null) GD.PushWarning($"Animation set {path} is missing; {projectPath} keeps its own clips.");
        Sets[set] = library;
        return library;
    }

    private static Dictionary<string, string> LoadIndex()
    {
        var path = Folder + "index.json";
        if (!FileAccess.FileExists(path)) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(FileAccess.GetFileAsString(path)) ?? new Dictionary<string, string>();
    }
}
