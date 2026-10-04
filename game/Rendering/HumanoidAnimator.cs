using System.Collections.Generic;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// Plays a generated human's animation set (assets/models/creatures/anim, see tools/blender/bgen/human/motion.py)
/// through an <see cref="AnimationTree"/> built in code:
/// <list type="bullet">
/// <item>locomotion: one cross-faded state (idle, walk, run, sprint, jump, fall, land, swim, climb, glide, death)
/// played at a speed (negative walks backwards);</item>
/// <item>an action over the upper body (swing, mine, thrust, shoot, bow, cast, drink, use) stretched to the item's
/// use time and restarted on every use, so you can swing while running;</item>
/// <item>a carried light held up in the right hand (blended in and out);</item>
/// <item>a hurt flinch over the upper body.</item>
/// </list>
/// An <see cref="AimModifier"/> then turns the torso and head toward the aim.
/// </summary>
public sealed class HumanoidAnimator
{
    /// <summary>The armature node name the animation sets' tracks use; every character's armature is renamed to it.</summary>
    public const string Rig = "anim_rig";

    public static readonly string[] States = { "idle", "walk", "run", "sprint", "jump", "fall", "land", "swim", "climb", "glide", "death" };
    public static readonly string[] Actions = { "swing", "mine", "thrust", "shoot", "bow", "cast", "drink", "use" };
    private static readonly HashSet<string> Looping = new() { "idle", "walk", "run", "sprint", "fall", "swim", "climb", "glide", "hold" };

    private static readonly string[] UpperBody =
    {
        "spine", "chest", "neck", "head", "clavicle.R", "clavicle.L", "upper_arm.R", "upper_arm.L", "forearm.R", "forearm.L", "hand.R", "hand.L",
    };

    private static readonly string[] Fingers = { "thumb", "index", "middle", "ring", "pinky" };

    private readonly AnimationTree _tree;
    private readonly AnimationLibrary _library;
    private string _state = ""; // nothing requested yet: the transition node starts with no current state
    private float _hold, _holdTarget, _actionLeft, _actionLength = 1f;

    private HumanoidAnimator(AnimationTree tree, AnimationLibrary library)
    {
        _tree = tree;
        _library = library;
    }

    public AimModifier? Aim { get; private set; }

    public string State => _state;

    /// <summary>The action playing over the upper body, or null.</summary>
    public string? Action { get; private set; }

    public bool ActionPlaying => (bool)_tree.Get("parameters/action/active");

    /// <summary>
    /// How far through the current action the pose is (0..1; 1 when none plays): the clip's position as the tree last
    /// played it, which can trail the requested start by a frame or a long one.
    /// </summary>
    public float ActionPhase
    {
        get
        {
            if (Action == null) return 1f;
            float length = (float)(double)_tree.Get($"parameters/a_{Action}/current_length");
            if (length <= 0f) return Mathf.Clamp(1f - _actionLeft / _actionLength, 0f, 1f);
            return Mathf.Clamp((float)(double)_tree.Get($"parameters/a_{Action}/current_position") / length, 0f, 1f);
        }
    }

    /// <summary>Seconds the current (or last) action was stretched over.</summary>
    public float ActionLength => _actionLength;

    /// <summary>
    /// Builds the tree under <paramref name="root"/> (the model's scene root, which holds the <see cref="Rig"/> armature)
    /// with the set's clips; the model's own AnimationPlayer (older embedded clips) is switched off.
    /// </summary>
    public static HumanoidAnimator Create(Node3D root, Skeleton3D skeleton, AnimationLibrary library, AnimationPlayer? old, bool aim)
    {
        if (old != null) old.Active = false;
        foreach (var name in library.GetAnimationList())
            if (Looping.Contains(name)) library.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        var tree = new AnimationTree { Name = "Motion", Deterministic = true };
        tree.AddAnimationLibrary("", library);
        tree.TreeRoot = BuildTree(library);
        root.AddChild(tree);
        tree.RootNode = tree.GetPathTo(root);
        tree.Active = true;
        var animator = new HumanoidAnimator(tree, library);
        animator.Locomotion("idle");
        if (aim)
        {
            animator.Aim = new AimModifier { Name = "Aim" };
            skeleton.AddChild(animator.Aim);
        }
        return animator;
    }

    private static AnimationNodeBlendTree BuildTree(AnimationLibrary library)
    {
        var bt = new AnimationNodeBlendTree();
        var loco = new AnimationNodeTransition { XfadeTime = 0.18f };
        foreach (var state in States)
        {
            loco.AddInput(state);
            bt.AddNode("s_" + state, Clip(library, state));
        }
        bt.AddNode("loco", loco);
        for (int i = 0; i < States.Length; i++) bt.ConnectNode("loco", i, "s_" + States[i]);
        bt.AddNode("loco_speed", new AnimationNodeTimeScale());
        bt.ConnectNode("loco_speed", 0, "loco");

        var pick = new AnimationNodeTransition { XfadeTime = 0f };
        foreach (var name in Actions)
        {
            pick.AddInput(name);
            bt.AddNode("a_" + name, Clip(library, name));
        }
        bt.AddNode("pick", pick);
        for (int i = 0; i < Actions.Length; i++) bt.ConnectNode("pick", i, "a_" + Actions[i]);
        bt.AddNode("act_speed", new AnimationNodeTimeScale());
        bt.ConnectNode("act_speed", 0, "pick");

        var action = new AnimationNodeOneShot { FadeInTime = 0.05f, FadeOutTime = 0.16f };
        Filter(action, UpperBody, both: true);
        bt.AddNode("action", action);
        bt.ConnectNode("action", 0, "loco_speed");
        bt.ConnectNode("action", 1, "act_speed");

        var hold = new AnimationNodeBlend2();
        Filter(hold, new[] { "clavicle.R", "upper_arm.R", "forearm.R", "hand.R" }, both: false);
        bt.AddNode("hold", hold);
        bt.AddNode("a_hold", Clip(library, "hold"));
        bt.ConnectNode("hold", 0, "action");
        bt.ConnectNode("hold", 1, "a_hold");

        var hurt = new AnimationNodeOneShot { FadeInTime = 0.04f, FadeOutTime = 0.14f };
        Filter(hurt, UpperBody, both: false);
        bt.AddNode("hurt", hurt);
        bt.AddNode("a_hurt", Clip(library, "hurt"));
        bt.ConnectNode("hurt", 0, "hold");
        bt.ConnectNode("hurt", 1, "a_hurt");
        bt.ConnectNode("output", 0, "hurt");
        return bt;
    }

    private static AnimationNodeAnimation Clip(AnimationLibrary library, string name) =>
        new() { Animation = library.HasAnimation(name) ? name : "idle" };

    private static void Filter(AnimationNode node, IEnumerable<string> bones, bool both)
    {
        node.FilterEnabled = true;
        foreach (var bone in bones)
        {
            node.SetFilterPath(new NodePath($"{Rig}/Skeleton3D:{bone}"), true);
            if (!bone.StartsWith("hand.")) continue;
            // The hand's fingers follow the hand (a drawn bowstring, an open palm); the other hand's too when both are filtered.
            string side = bone[^1..];
            if (!both && side == "L") continue;
            foreach (var f in Fingers)
                for (int k = 1; k <= 3; k++)
                    node.SetFilterPath(new NodePath($"{Rig}/Skeleton3D:{f}.0{k}.{side}"), true);
        }
    }

    public bool Has(string clip) => _library.HasAnimation(clip);

    /// <summary>Switches the locomotion state (cross-fading) and sets its playback speed.</summary>
    public void Locomotion(string state, float speed = 1f)
    {
        if (!_library.HasAnimation(state)) state = "idle";
        if (state != _state)
        {
            _state = state;
            _tree.Set("parameters/loco/transition_request", state);
        }
        _tree.Set("parameters/loco_speed/scale", speed);
    }

    /// <summary>
    /// Walk, run or sprint for a ground speed (m/s), and the playback rate that keeps the feet from sliding: each gait's
    /// stride covers a set distance per cycle. Undead sets (a slower shamble, recognised by their longer walk) never sprint.
    /// </summary>
    public string Gait(float speed, out float rate)
    {
        if (_library.GetAnimation("walk").Length > 1.2f)
        {
            if (speed < 1.6f)
            {
                rate = Mathf.Clamp(speed / 1.0f, 0.5f, 1.8f);
                return "walk";
            }
            rate = Mathf.Clamp(speed / 1.9f, 0.7f, 1.8f);
            return "run";
        }
        if (speed < 2f)
        {
            rate = Mathf.Clamp(speed / 1.4f, 0.5f, 1.5f);
            return "walk";
        }
        if (speed < 4.6f)
        {
            rate = Mathf.Clamp(speed / 2.9f, 0.7f, 1.6f);
            return "run";
        }
        rate = Mathf.Clamp(speed / 4.3f, 0.9f, 1.8f);
        return "sprint";
    }

    /// <summary>Starts (or restarts) an upper-body action stretched over <paramref name="seconds"/>.</summary>
    public void Play(string action, float seconds)
    {
        if (!_library.HasAnimation(action)) action = "use";
        var clip = _library.GetAnimation(action);
        Action = action;
        _tree.Set("parameters/pick/transition_request", action);
        _tree.Set("parameters/act_speed/scale", clip.Length / Mathf.Max(0.08f, seconds));
        _tree.Set("parameters/action/request", (int)AnimationNodeOneShot.OneShotRequest.Fire);
        _actionLeft = _actionLength = Mathf.Max(0.08f, seconds);
    }

    public void StopAction() => _tree.Set("parameters/action/request", (int)AnimationNodeOneShot.OneShotRequest.FadeOut);

    public void Hurt() => _tree.Set("parameters/hurt/request", (int)AnimationNodeOneShot.OneShotRequest.Fire);

    /// <summary>Raises (1) or lowers (0) a carried light in the right hand; eased by <see cref="Update"/>.</summary>
    public void Hold(bool on) => _holdTarget = on ? 1f : 0f;

    public void Update(float dt)
    {
        _hold = Mathf.MoveToward(_hold, _holdTarget, dt * 5f);
        _tree.Set("parameters/hold/blend_amount", Mathf.SmoothStep(0f, 1f, _hold));
        // (Timed rather than read from the tree, which only picks up a request on its next process.)
        _actionLeft -= dt;
        if (_actionLeft <= 0f) Action = null;
    }
}
