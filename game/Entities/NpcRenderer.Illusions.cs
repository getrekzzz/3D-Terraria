using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

public sealed partial class NpcRenderer
{
    /// <summary>Illusions are dim, faintly violet copies (the visual has no transparency).</summary>
    private static readonly Color IllusionTint = new(0.5f, 0.42f, 0.6f);

    /// <summary>
    /// Decoy copies of a creature (the Brain of Cthulhu's illusions): pooled visuals of its current model placed at
    /// <see cref="Strata.Core.AI.Illusions.Position"/>, turned with it and dimmed. Purely visual: no collision or damage,
    /// as in Terraria; released when the creature has none (or goes).
    /// </summary>
    private void UpdateIllusions(Npc npc, View view)
    {
        int want = npc.Alive ? npc.Illusions : 0;
        if (view.IllusionModel != view.Model) ReleaseIllusions(view);
        while (view.Illusions.Count > want)
        {
            Release(view.Illusions[^1], view.IllusionModel!);
            view.Illusions.RemoveAt(view.Illusions.Count - 1);
        }
        if (want == 0) return;
        view.IllusionModel = view.Model;
        while (view.Illusions.Count < want)
        {
            if (Acquire(view.Model) is not { } copy) return;
            view.Illusions.Add(copy);
        }
        // Place copies relative to the smoothed body so they move with it, not with the raw simulation step.
        var body = view.Visual;
        var center = npc.Center;
        for (int i = 0; i < want; i++)
        {
            var p = Strata.Core.AI.Illusions.Position(npc, i) - center;
            var copy = view.Illusions[i];
            copy.Position = body.Position + new Vector3(p.X, p.Y, p.Z);
            copy.Basis = new Basis(Vector3.Up, (i + 1) * (Mathf.Pi * 0.5f)) * body.Basis;
            copy.SetTint(IllusionTint);
            copy.SetLight(OwnLight(npc, LightProbe.SampleSmooth(_game.Session.World, copy.Position + Vector3.Up * 0.5f)));
            if (body.Current is { Length: > 0 } clip && copy.Current != clip) copy.Play(clip);
        }
    }

    private void ReleaseIllusions(View view)
    {
        if (view.IllusionModel != null)
            foreach (var copy in view.Illusions) Release(copy, view.IllusionModel);
        view.Illusions.Clear();
        view.IllusionModel = null;
    }
}
