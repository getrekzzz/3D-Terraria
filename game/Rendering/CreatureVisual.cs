using System.Collections.Generic;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// An instanced animated model (player, enemies): the GLB scene with prop materials on every surface, looping
/// locomotion clips, cross-faded playback and per-frame voxel light / tint. Generated humans play their shared
/// animation set through a <see cref="HumanoidAnimator"/> (layered actions, aim); other creatures their own clips.
/// </summary>
public sealed partial class CreatureVisual : Node3D
{
    private static readonly HashSet<string> Looping = new() { "idle", "walk", "run", "fly", "jump", "fall" };
    private readonly List<GeometryInstance3D> _geometry = new();
    private AnimationPlayer? _player;
    private string _current = "";

    public Skeleton3D? Skeleton { get; private set; }

    /// <summary>Set for generated humans with an animation set (see <see cref="HumanAnimations"/>).</summary>
    public HumanoidAnimator? Humanoid { get; private set; }

    /// <summary>Loads a creature model; returns null (and records a problem) if the file is missing.</summary>
    /// <param name="aim">Give a human an <see cref="AimModifier"/> (the player's body).</param>
    public static CreatureVisual? Create(string projectPath, PropLibrary props, bool aim = false)
    {
        var path = "res://" + projectPath;
        if (!ResourceLoader.Exists(path))
        {
            props.Problems.Add($"missing creature model {path}");
            return null;
        }
        var visual = new CreatureVisual { Name = "Visual" };
        var scene = GD.Load<PackedScene>(path).Instantiate<Node3D>();
        visual.AddChild(scene);
        visual.Setup(scene, props, scene.Transform);
        if (visual.Skeleton is { } skeleton && HumanAnimations.For(projectPath) is { } library)
        {
            // The set's tracks address the armature by one shared name.
            if (skeleton.GetParent() is Node3D armature && armature != scene) armature.Name = HumanoidAnimator.Rig;
            visual.Humanoid = HumanoidAnimator.Create(scene, skeleton, library, visual._player, aim);
        }
        return visual;
    }

    /// <summary>The model's vertical extent around its origin (metres, at scale 1).</summary>
    public float Bottom { get; private set; } = float.MaxValue;

    public float Top { get; private set; } = float.MinValue;

    /// <summary>
    /// How far to raise a model built around its centre (eyes, skulls) so it sits in its creature's box, which starts at
    /// the feet: only models whose bounds are centred on the origin and about the box's size (a body with legs dangling
    /// far below its box, like the Moon Lord's, is placed as modelled).
    /// </summary>
    public float LiftFor(float boxHeight, float scale)
    {
        float height = Top - Bottom, centre = (Top + Bottom) * 0.5f;
        if (Bottom >= 0f || height <= 0f || Mathf.Abs(centre) > 0.15f * height || height * scale > 2.2f * boxHeight) return 0f;
        return -Bottom * scale;
    }

    private PropLibrary? _props;

    private void Setup(Node node, PropLibrary props, Transform3D xf)
    {
        _props = props;
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            var box = xf * mi.Mesh.GetAabb();
            Bottom = Mathf.Min(Bottom, box.Position.Y);
            Top = Mathf.Max(Top, box.End.Y);
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                mi.SetSurfaceOverrideMaterial(i, props.Material(mi.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? "prop:missing", multimesh: false));
            _geometry.Add(mi);
        }
        if (node is AnimationPlayer ap)
        {
            _player = ap;
            foreach (var name in ap.GetAnimationList())
                if (Looping.Contains(name)) ap.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        }
        if (node is Skeleton3D sk) Skeleton = sk;
        foreach (var child in node.GetChildren()) Setup(child, props, child is Node3D n ? xf * n.Transform : xf);
    }

    public bool HasAnimation(string name) => Humanoid?.Has(name) ?? (_player != null && _player.HasAnimation(name));

    /// <summary>
    /// Plays a clip, cross-fading; restarting the same looping clip is a no-op unless <paramref name="restart"/>. For
    /// humans this sets the locomotion state (actions go through <see cref="Humanoid"/>).
    /// </summary>
    public void Play(string name, float blend = 0.15f, float speed = 1f, bool restart = false)
    {
        if (Humanoid != null)
        {
            Humanoid.Locomotion(name, speed);
            _current = name;
            return;
        }
        if (_player == null || !_player.HasAnimation(name)) return;
        if (_current == name && !restart && _player.IsPlaying())
        {
            _player.SpeedScale = speed;
            return;
        }
        _current = name;
        _player.Play(name, blend, speed);
        if (restart) _player.Seek(0, true);
    }

    public string Current => _current;

    public bool IsPlaying => Humanoid != null || (_player?.IsPlaying() ?? false);

    public override void _Process(double delta)
    {
        Humanoid?.Update((float)delta);
        UpdateSway((float)delta);
    }

    public void SetTint(Color tint)
    {
        foreach (var g in _geometry) g.SetInstanceShaderParameter("tint", tint);
    }

    private Color _light;
    private bool _lit;

    /// <summary>The (eased) voxel light the model is shaded with; attachments that should match it read this.</summary>
    public Color CurrentLight => _light;

    /// <summary>
    /// Sets the voxel light the model is shaded with, easing towards it (a few frames) so walking past a torch or
    /// under a roof fades rather than pops.
    /// </summary>
    public void SetLight(Color voxelLight, bool immediate = false)
    {
        _light = _lit && !immediate ? _light.Lerp(voxelLight, 0.18f) : voxelLight;
        _lit = true;
        foreach (var g in _geometry) g.SetInstanceShaderParameter("voxel_light", _light);
    }

    /// <summary>Attaches a node to a bone (held items); returns false if the bone does not exist.</summary>
    public bool AttachToBone(string bone, Node3D node)
    {
        if (Skeleton == null || Skeleton.FindBone(bone) < 0) return false;
        var attachment = new BoneAttachment3D { BoneName = bone, Name = "Attach_" + bone };
        Skeleton.AddChild(attachment);
        attachment.AddChild(node);
        return true;
    }

    /// <summary>
    /// Colours a recolourable player model (generated with marker colours) to a character's look: hair, skin, eyes,
    /// shirt, undershirt, trousers and shoes; the chosen hair model is worn on the head.
    /// </summary>
    public void SetAppearance(Strata.Core.Gameplay.Player.Appearance look)
    {
        WearHair(look.HairModelId);
        foreach (var g in _geometry)
        {
            g.SetInstanceShaderParameter("recolor", 1f);
            g.SetInstanceShaderParameter("hair_color", Linear(look.HairColor));
            g.SetInstanceShaderParameter("skin_color", Linear(look.SkinColor));
            g.SetInstanceShaderParameter("shirt_color", Linear(look.ShirtColor));
            g.SetInstanceShaderParameter("pants_color", Linear(look.PantsColor));
            g.SetInstanceShaderParameter("shoe_color", Linear(look.ShoeColor));
            g.SetInstanceShaderParameter("eye_color", Linear(look.EyeColor));
            g.SetInstanceShaderParameter("undershirt_color", Linear(look.UndershirtColor));
        }
    }

    private static Color Linear(string hex) => Color.FromHtml(hex).SrgbToLinear();

    /// <summary>Prefers finer mesh LODs (above 1) — the player's own body is always close to the camera.</summary>
    public void SetLodBias(float bias)
    {
        foreach (var g in _geometry) g.LodBias = bias;
    }

    public void SetCastShadows(bool on)
    {
        foreach (var g in _geometry) g.CastShadow = on ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
    }
}
