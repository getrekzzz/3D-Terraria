using Godot;

namespace Strata.Game.Rendering;

/// <summary>Showing and hiding a model's material slots (a helmet hides the hair) and measuring its bones.</summary>
public sealed partial class CreatureVisual
{
    private static ShaderMaterial? _hidden;

    /// <summary>Shows or hides every surface of a material slot (e.g. <c>char:hair</c>) without touching the mesh.</summary>
    public void SetSlotVisible(string slot, bool visible)
    {
        if (_props == null) return;
        _hidden ??= new ShaderMaterial { Shader = new Shader { Code = "shader_type spatial;\nrender_mode unshaded, shadows_disabled;\nvoid fragment() { discard; }\n" } };
        foreach (var g in _geometry)
        {
            if (g is not MeshInstance3D mi || mi.Mesh == null) continue;
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                if (mi.Mesh.SurfaceGetMaterial(i)?.ResourceName == slot)
                    mi.SetSurfaceOverrideMaterial(i, visible ? _props.Material(slot, multimesh: false) : _hidden);
        }
    }

    /// <summary>
    /// A bone's rest length: the distance to the child bone that continues it (the one furthest along its axis), or
    /// 0 for an end bone (the head) or a missing one.
    /// </summary>
    public float BoneLength(string bone)
    {
        int index = Skeleton?.FindBone(bone) ?? -1;
        if (index < 0) return 0f;
        float best = 0f;
        foreach (int child in Skeleton!.GetBoneChildren(index))
            best = Mathf.Max(best, Skeleton.GetBoneRest(child).Origin.Y);
        return best;
    }

    /// <summary>The name of a bone's parent, or null.</summary>
    public string? ParentBone(string bone)
    {
        int index = Skeleton?.FindBone(bone) ?? -1;
        int parent = index < 0 ? -1 : Skeleton!.GetBoneParent(index);
        return parent < 0 ? null : Skeleton!.GetBoneName(parent);
    }
}
