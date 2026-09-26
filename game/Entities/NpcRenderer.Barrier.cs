using System;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

public sealed partial class NpcRenderer
{
    /// <summary>A creature that emits light is lit at least by its own glow (the prop shader ignores omni lights).</summary>
    private static Color OwnLight(Npc npc, Color sampled)
    {
        if (npc.Def.Light is not { Length: 3 } l) return sampled;
        const float share = 0.7f;
        return new Color(Mathf.Max(sampled.R, l[0] / 15f * share), Mathf.Max(sampled.G, l[1] / 15f * share), Mathf.Max(sampled.B, l[2] / 15f * share));
    }

    /// <summary>
    /// Tiles the boss's barrier model (a wall's flesh) over <see cref="Npc.Barrier"/>: one tile per cell of a grid across
    /// the wall's height and width, stretched to fit, facing the direction the wall travels.
    /// </summary>
    private void UpdateBarrier(Npc npc, View view, Strata.Core.Voxels.VoxelWorld world)
    {
        var boss = npc.Def.Boss;
        if (boss?.BarrierModel is not { } model || npc.Barrier is not { } box) return;
        float tile = boss.BarrierTile;
        float height = box.Max.Y - box.Min.Y, width = box.Max.Z - box.Min.Z;
        int rows = Math.Max(1, (int)MathF.Ceiling(height / tile)), cols = Math.Max(1, (int)MathF.Ceiling(width / tile));
        while (view.Barrier.Count < rows * cols)
        {
            if (Acquire(model) is not { } added) return;
            view.Barrier.Add(added);
        }
        while (view.Barrier.Count > rows * cols)
        {
            Release(view.Barrier[^1], model);
            view.Barrier.RemoveAt(view.Barrier.Count - 1);
        }
        float rowH = height / rows, colW = width / cols, x = (box.Min.X + box.Max.X) * 0.5f;
        var scale = new Vector3(colW / tile, rowH / tile, 1f); // model: X across, Y up, thickness along its facing
        var rotation = new Vector3(0, npc.Yaw + Mathf.Pi, 0);
        var tint = npc.HurtFlash > 0 ? new Color(1.6f, 0.8f, 0.8f) : Colors.White;
        int i = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var part = view.Barrier[i++];
                var at = new Vector3(x, box.Min.Y + (r + 0.5f) * rowH, box.Min.Z + (c + 0.5f) * colW);
                part.Position = at;
                part.Rotation = rotation;
                part.Scale = scale;
                part.SetTint(tint);
                part.SetLight(OwnLight(npc, LightProbe.SampleSmooth(world, at)));
            }
    }
}
