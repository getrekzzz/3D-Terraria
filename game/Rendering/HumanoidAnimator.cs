using System.Collections.Generic;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>
/// Plays a generated human's animation set (assets/models/creatures/anim, see tools/blender/bgen/human/motion.py)
/// through an <see cref="AnimationTree"/> built in code:
/// <list type="bullet">
/// <item>locomotion: one cross-faded state (idle, walk, run, sprint, strafe_left, strafe_right, jump, fall, land,
/// swim, climb, glide, death) played at a speed (negative walks backwards);</item>
/// <item>an action over the upper body (swing, mine, thrust, shoot, bow, cast, drink, use) played once per use,
/// stretched to the use animation (a use too quick for the clip skips its wind-up), so you can swing while running;
/// chained uses flow on without fading back to the locomotion pose in between;</item>
/// <item>a carried light held up in the right hand (blended in and out);</item>
/// <item>a hurt flinch over the upper body.</item>
/// </list>
/// An <see cref="AimModifier"/> then turns the torso (chest, arms and head) onto the aim.
/// </summary>
public sealed class HumanoidAnimator
{
    /// <summary>The armature node name the animation sets' tracks use; every character's armature is renamed to it.</summary>
    public const string Rig = "anim_rig";

    public static readonly string[] States =
    {
        "idle", "walk", "run", "sprint", "strafe_left", "strafe_right", "jump", "fall", "land", "swim", "climb", "glide", "death",
    };

    public static readonly string[] Actions = { "swing", "mine", "thrust", "shoot", "bow", "cast", "drink", "use" };

    private static readonly HashSet<string> Looping = new()
    {
        "idle", "walk", "run", "sprint", "strafe_left", "strafe_right", "fall", "swim", "climb", "glide", "hold",
    };

    /// <summary>
    /// The strafe clips run at this angle (degrees) to the left or right of where the hips face, covering the same
    /// ground per cycle as run (tools/blender/bgen/human/strafe.py): turning the legs this far toward the movement and
    /// playing strafe_left or strafe_right moves the feet exactly sideways while the aim keeps the torso on target.
    /// </summary>
    public const float StrafeAngle = 45f;

    private static readonly string[] UpperBody =
    {
        "spine", "chest", "neck", "head", "clavicle.R", "clavicle.L", "upper_arm.R", "upper_arm.L", "forearm.R", "forearm.L", "hand.R", "hand.L",
    };

    private static readonly string[] Fingers = { "thumb", "index", "middle", "ring", "pinky" };

    // Tree parameters set every frame or every use (cached: a string converts to a new StringName on each call).
    private static readonly StringName HoldBlend = "parameters/hold/blend_amount", ActionBlend = "parameters/action/blend_amount";
    private static readonly StringName PickRequest = "parameters/pick/transition_request", ActionSpeed = "parameters/act_speed/scale";
    private static readonly StringName ActionSeek = "parameters/act_seek/seek_request", HurtRequest = "parameters/hurt/request";
    private static readonly StringName LocoRequest = "parameters/loco/transition_request", LocoSpeed = "parameters/loco_speed/scale";

    private readonly AnimationTree _tree;
    private readonly AnimationLibrary _library;

    /// <summary>Undead sets shamble: a slower, longer walk (and no sprint).</summary>
    private readonly bool _shambles;
    private string _state = ""; // nothing requested yet: the transition node starts with no current state
    private float _hold, _holdTarget, _actionLeft = -1f, _actionWeight, _sincePlay;
    private int _framesEnded = int.MaxValue;
    private bool _sustain;

    private HumanoidAnimator(AnimationTree tree, AnimationLibrary library)
    {
        _tree = tree;
        _library = library;
        CanStrafe = library.HasAnimation("strafe_left") && library.HasAnimation("strafe_right");
        _shambles = library.HasAnimation("walk") && library.GetAnimation("walk").Length > 1.2f;
    }

    public AimModifier? Aim { get; private set; }

    public string State => _state;

    /// <summary>The action playing over the upper body, or null.</summary>
    public string? Action { get; private set; }

    /// <summary>The action is blended in at all (playing, or fading back to the locomotion pose).</summary>
    public bool ActionPlaying => _actionWeight > 0f;

    /// <summary>How much of the upper body the action holds (0–1).</summary>
    public float ActionWeight => _actionWeight;

    /// <summary>
    /// Chained uses (started before, or within 0.15 s after, the last one ended) that found the action no longer fully
    /// blended in: the upper body dipped toward the locomotion pose in between (probe <c>player.action_dips</c>; 0 when
    /// autoswings and chops flow on).
    /// </summary>
    public int ActionDips { get; private set; }

    /// <summary>Fade of the action over the locomotion pose: in when a use starts from rest, out once the uses stop.</summary>
    private const float ActionFadeIn = 0.05f, ActionFadeOut = 0.16f;

    /// <summary>
    /// How long (s, and at least two frames) a finished action holds its last pose before fading out: covers the tick
    /// before the next chained use starts and the frame it is seen in, so autoswings and chops flow from one into the next.
    /// </summary>
    private const float ChainGrace = 0.08f;

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
        // Each use seeks the clip (to its start, or past the wind-up for a quick use); the seek sits below the time
        // scale, so its position is in the clip's own seconds.
        bt.AddNode("act_seek", new AnimationNodeTimeSeek());
        bt.ConnectNode("act_seek", 0, "pick");
        bt.AddNode("act_speed", new AnimationNodeTimeScale());
        bt.ConnectNode("act_speed", 0, "act_seek");

        // A plain blend rather than a OneShot (which fades every use out over its end and in again on the next):
        // Update eases the weight, so a chained use carries on from the pose the last one ended in.
        var action = new AnimationNodeBlend2();
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

    /// <summary>The set has both strafe clips (every generated set since the strafe rebuild).</summary>
    public bool CanStrafe { get; }

    /// <summary>
    /// The playback rate of strafe_left and strafe_right at a ground speed (m/s): they cover run's ground per cycle, so
    /// they follow run's rate (allowed a little faster, as there is no sprint strafe) and the feet do not slide.
    /// </summary>
    public float StrafeRate(float speed) => Mathf.Clamp(speed / (_shambles ? 1.9f : 2.9f), 0.5f, 2.4f);

    /// <summary>Switches the locomotion state (cross-fading) and sets its playback speed.</summary>
    public void Locomotion(string state, float speed = 1f)
    {
        if (!_library.HasAnimation(state)) state = "idle";
        if (state != _state)
        {
            _state = state;
            _tree.Set(LocoRequest, state);
        }
        _tree.Set(LocoSpeed, speed);
    }

    /// <summary>
    /// Walk, run or sprint for a ground speed (m/s), and the playback rate that keeps the feet from sliding: each gait's
    /// stride covers a set distance per cycle. Undead sets (a slower shamble, recognised by their longer walk) never sprint.
    /// </summary>
    public string Gait(float speed, out float rate)
    {
        if (_shambles)
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

    /// <summary>Starts an upper-body action from its first frame, stretched over <paramref name="seconds"/> (creatures).</summary>
    public void Play(string action, float seconds) => Play(action, seconds, float.PositiveInfinity, 0f);

    /// <summary>
    /// Starts an upper-body action played once over <paramref name="seconds"/> (the use animation after speed bonuses).
    /// A use too quick for the clip at <paramref name="maxRate"/> times its authored speed skips the wind-up: the clip
    /// starts at <paramref name="windupEnd"/> (a fraction of the clip, where the strike begins) and plays the rest over
    /// the use, so the strike lands inside it and the clip still ends when the use does. A chained use picks up from the
    /// pose the last one ended in (the action stays blended in).
    /// </summary>
    public void Play(string action, float seconds, float maxRate, float windupEnd)
    {
        if (!_library.HasAnimation(action)) action = "use";
        float length = (float)_library.GetAnimation(action).Length;
        seconds = Mathf.Max(0.05f, seconds);
        float from = 0f, rate = length / seconds;
        if (rate > maxRate && windupEnd > 0f && windupEnd < 1f)
        {
            from = windupEnd;
            rate = length * (1f - windupEnd) / seconds;
        }
        // A use following on from the last (which had time to blend fully in, and ended at most a moment ago) should
        // find the action still fully in.
        if (_sincePlay > 2f * ActionFadeIn && _actionLeft > -0.15f && _actionWeight < 0.999f) ActionDips++;
        _sincePlay = 0f;
        Action = action;
        _tree.Set(PickRequest, action);
        _tree.Set(ActionSpeed, rate);
        _tree.Set(ActionSeek, from * length);
        _actionLeft = seconds;
        _framesEnded = 0;
        // Blended in from this frame (a blend near zero skips the input), so the tree applies the clip and its seek now.
        _actionWeight = Mathf.Max(_actionWeight, 0.01f);
        _tree.Set(ActionBlend, Mathf.SmoothStep(0f, 1f, _actionWeight));
    }

    /// <summary>
    /// Keeps the action playing through this frame's <see cref="Update"/>: its use is still running in the simulation,
    /// which can fall behind real time under load (the timer alone would end it early and fade before a chained use).
    /// </summary>
    public void Sustain() => _sustain = true;

    /// <summary>Lets the action go: it fades out from the pose it is in.</summary>
    public void StopAction()
    {
        _actionLeft = -ChainGrace;
        _framesEnded = int.MaxValue;
        Action = null;
    }

    public void Hurt() => _tree.Set(HurtRequest, (int)AnimationNodeOneShot.OneShotRequest.Fire);

    /// <summary>Raises (1) or lowers (0) a carried light in the right hand; eased by <see cref="Update"/>.</summary>
    public void Hold(bool on) => _holdTarget = on ? 1f : 0f;

    public void Update(float dt)
    {
        _hold = Mathf.MoveToward(_hold, _holdTarget, dt * 5f);
        _tree.Set(HoldBlend, Mathf.SmoothStep(0f, 1f, _hold));
        // (Timed rather than read from the tree, which only picks up a request on its next process.)
        _actionLeft = Mathf.Max(_actionLeft - dt, -1f);
        _sincePlay = Mathf.Min(_sincePlay + dt, 10f);
        if (_sustain && Action != null) _actionLeft = Mathf.Max(_actionLeft, 1e-3f);
        _sustain = false;
        if (_actionLeft <= 0f)
        {
            Action = null;
            if (_framesEnded < int.MaxValue) _framesEnded++;
        }
        if (_actionWeight <= 0f && Action == null) return;
        // Fully in while the use plays and for a moment after (the next chained use starts within it); then out.
        bool engaged = _actionLeft > -ChainGrace || _framesEnded <= 2;
        _actionWeight = engaged ? Mathf.MoveToward(_actionWeight, 1f, dt / ActionFadeIn) : Mathf.MoveToward(_actionWeight, 0f, dt / ActionFadeOut);
        _tree.Set(ActionBlend, Mathf.SmoothStep(0f, 1f, _actionWeight));
    }
}
