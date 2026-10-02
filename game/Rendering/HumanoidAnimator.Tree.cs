using System.Collections.Generic;
using Godot;

namespace Strata.Game.Rendering;

/// <summary>The animation tree <see cref="HumanoidAnimator"/> builds from a set's clips, and the check for missing clips.</summary>
public sealed partial class HumanoidAnimator
{
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

        // The right arm's looping clips, picked by a transition (a short cross-fade between them) under one blend.
        var holdPick = new AnimationNodeTransition { XfadeTime = 0.12f };
        foreach (var name in HoldClips)
        {
            holdPick.AddInput(name);
            bt.AddNode("h_" + name, Clip(library, name));
        }
        bt.AddNode("hold_pick", holdPick);
        for (int i = 0; i < HoldClips.Length; i++) bt.ConnectNode("hold_pick", i, "h_" + HoldClips[i]);
        var hold = new AnimationNodeBlend2();
        Filter(hold, new[] { "clavicle.R", "upper_arm.R", "forearm.R", "hand.R" }, both: false);
        bt.AddNode("hold", hold);
        bt.ConnectNode("hold", 0, "action");
        bt.ConnectNode("hold", 1, "hold_pick");

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

    /// <summary>Counts and reports (once per set) the clips a set lacks: an older set plays idle in their place.</summary>
    private static void CheckClips(AnimationLibrary library)
    {
        if (!Checked.Add(library.GetInstanceId())) return;
        foreach (var name in Required)
        {
            if (library.HasAnimation(name)) continue;
            MissingClips++;
            GD.PushWarning($"Human animation set {library.ResourcePath} has no '{name}' clip (rebuild: tools/blender/build_all.py --anims).");
        }
    }

    private static string[] Concat(params string[][] lists)
    {
        var all = new List<string>();
        foreach (var list in lists) all.AddRange(list);
        return all.ToArray();
    }

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
}
