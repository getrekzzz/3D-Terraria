using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Arms for bosses whose hands fly free (boss parts: Skeletron's bone hands, Prime's four tools): each hand is joined to the
/// head's shoulder on its side by two stretched copies of the boss's <c>armModel</c> (modelled one unit long along
/// +Y), bending at an elbow that swings outward and down so the arm reads as reaching rather than a stick.
/// </summary>
public sealed partial class NpcRenderer
{
    /// <summary>
    /// Arms to a boss's living hand parts (Skeletron's): from the skull's centre to each hand's, on the side the hand
    /// is fixed to (so a swipe across the body crosses the arm over instead of flipping the shoulder); spare arms hide.
    /// </summary>
    private void UpdatePartArms(Npc npc, View view)
    {
        var boss = npc.Def.Boss!;
        if (!EnsureArms(view, boss.ArmModel!, boss.Parts.Length)) return;
        var head = view.Shown + Vector3.Up * npc.HalfExtents.Y;
        var right = new Basis(Vector3.Up, npc.Yaw + Mathf.Pi) * Vector3.Right;
        int used = 0;
        foreach (var f in npc.Followers)
        {
            if (used >= boss.Parts.Length) break;
            if (!f.IsPart || !f.Alive || !_views.TryGetValue(f.Id, out var hv)) continue;
            var hand = hv.Shown + Vector3.Up * f.HalfExtents.Y;
            var o = f.LeaderOffset;
            float side = o.X < 0f ? 1f : -1f; // the leader's +X is the view's left (see FollowLeader)
            float rest = new Vector2(o.X, o.Y).Length();
            Arm(view, used++, npc, head, hand, right, side, npc.HalfExtents.X, rest * 0.6f);
        }
        for (int i = used * 2; i < view.Arms.Count; i++) view.Arms[i].Visible = false;
    }

    private bool EnsureArms(View view, string model, int parts)
    {
        while (view.Arms.Count < parts * 2)
        {
            if (Acquire(model) is not { } piece) return false;
            view.Arms.Add(piece);
        }
        return true;
    }

    /// <summary>
    /// Arm <paramref name="index"/>: two equal bones from the shoulder on <paramref name="side"/> to the hand, long
    /// enough to reach with a little slack so the elbow always shows, bending outward and down.
    /// </summary>
    private void Arm(View view, int index, Npc npc, Vector3 head, Vector3 hand, Vector3 right, float side, float radius, float minBone)
    {
        var shoulder = head + right * side * radius * 0.9f + Vector3.Down * radius * 0.55f;
        var span = hand - shoulder;
        float d = span.Length();
        float bone = Mathf.Max(d * 0.56f, minBone);
        float lift = Mathf.Sqrt(Mathf.Max(0f, bone * bone - d * d * 0.25f));
        var outward = (right * side + Vector3.Down * 0.6f).Normalized();
        if (d > 1e-3f) outward = (outward - span / d * outward.Dot(span / d)).Normalized();
        var elbow = shoulder + span * 0.5f + outward * lift;
        Place(view.Arms[index * 2], shoulder, elbow, npc);
        Place(view.Arms[index * 2 + 1], elbow, hand, npc);
    }

    /// <summary>Stretches a unit-long arm piece (Blender +Y, Godot −Z) from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private void Place(CreatureVisual piece, Vector3 a, Vector3 b, Npc npc)
    {
        var y = b - a;
        float len = y.Length();
        piece.Visible = len > 1e-3f;
        if (!piece.Visible) return;
        // Bones are modelled along Blender's +Y, which glTF export turns into Godot's −Z: map −Z onto a→b.
        var along = y / len;
        var side = Mathf.Abs(along.Y) > 0.95f ? Vector3.Right : along.Cross(Vector3.Up).Normalized();
        var back = -along;
        piece.Transform = new Transform3D(new Basis(side, back.Cross(side), back * len), a);
        piece.SetTint(npc.HurtFlash > 0 ? new Color(2.2f, 0.7f, 0.7f) : Colors.White);
        piece.SetLight(OwnLight(npc, LightProbe.SampleSmooth(_game.Session.World, a.Lerp(b, 0.5f))));
    }

    private void ReleaseArms(Npc npc, View view)
    {
        if (npc.Def.Boss?.ArmModel is not { } model) return;
        foreach (var piece in view.Arms) Release(piece, model);
        view.Arms.Clear();
    }
}
