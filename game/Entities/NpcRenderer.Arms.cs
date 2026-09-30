using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Arms for bosses whose hands fly free (Skeletron's bone hands, Prime's weapons): each free part is joined to the
/// head's shoulder on its side by two stretched copies of the boss's <c>armModel</c> (modelled one unit long along
/// +Y), bending at an elbow that swings outward and down so the arm reads as reaching rather than a stick.
/// </summary>
public sealed partial class NpcRenderer
{
    private void UpdateArms(Npc npc, View view)
    {
        if (npc.Def.Boss?.ArmModel is not { } model || npc.Def.Boss.SegmentModels.Length == 0) return;
        int parts = System.Math.Min(npc.Segments.Count, npc.Def.Boss.SegmentModels.Length);
        while (view.Arms.Count < parts * 2)
        {
            if (Acquire(model) is not { } piece) return;
            view.Arms.Add(piece);
        }
        float radius = npc.Def.Boss.SegmentRadius;
        var head = view.Shown;
        var right = new Basis(Vector3.Up, npc.Yaw + Mathf.Pi) * Vector3.Right;
        for (int i = 0; i < parts; i++)
        {
            var hand = new Vector3(npc.Segments[i].X, npc.Segments[i].Y, npc.Segments[i].Z);
            float side = Mathf.Sign((hand - head).Dot(right));
            if (side == 0f) side = i % 2 == 0 ? 1f : -1f;
            var shoulder = head + right * side * radius * 0.9f + Vector3.Down * radius * 0.55f;
            var span = hand - shoulder;
            float d = span.Length();
            // Two equal bones long enough to reach, with a little slack so the elbow always shows.
            float bone = Mathf.Max(d * 0.56f, npc.Def.Boss.SegmentSpacing * 0.6f);
            float lift = Mathf.Sqrt(Mathf.Max(0f, bone * bone - d * d * 0.25f));
            var outward = (right * side + Vector3.Down * 0.6f).Normalized();
            if (d > 1e-3f) outward = (outward - span / d * outward.Dot(span / d)).Normalized();
            var elbow = shoulder + span * 0.5f + outward * lift;
            Place(view.Arms[i * 2], shoulder, elbow, npc);
            Place(view.Arms[i * 2 + 1], elbow, hand, npc);
        }
    }

    /// <summary>Stretches a unit-long +Y arm piece from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private void Place(CreatureVisual piece, Vector3 a, Vector3 b, Npc npc)
    {
        var y = b - a;
        float len = y.Length();
        piece.Visible = len > 1e-3f;
        if (!piece.Visible) return;
        var up = y / len;
        var side = Mathf.Abs(up.Y) > 0.95f ? Vector3.Right : up.Cross(Vector3.Up).Normalized();
        piece.Transform = new Transform3D(new Basis(side, up * len, side.Cross(up)), a);
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
