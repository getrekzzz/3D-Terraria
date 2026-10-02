using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.Rendering;

/// <summary>
/// Hair and loose clothing in motion (X-17): people within the graphics preset's sway distance of the camera run a
/// <see cref="SpringSway"/> anchored at their head, and hand its offset to the character shader, which bends hair below
/// the head joint and cloth marked loose (robe skirts, coat tails) by it and adds wind flutter. Beyond the distance the
/// parts rest and nothing is updated; per frame it costs one spring step and one shader parameter per surface.
/// </summary>
public sealed partial class CreatureVisual
{
    /// <summary>How far from the camera characters' hair and clothes move (metres, 0 = off): the preset's swayDistance.</summary>
    public static float SwayDistance { get; set; } = 20f;

    private static readonly StringName SwayParam = "sway", SwayHeadParam = "sway_head";
    private SpringSway _spring;
    private bool _swaying, _swayReady;
    private float _swayHead = 1.575f;

    private void UpdateSway(float dt)
    {
        if (Humanoid == null || Skeleton == null) return;
        if (!_swayReady)
        {
            _swayReady = true;
            int head = Skeleton.FindBone("head");
            if (head >= 0) _swayHead = Skeleton.GetBoneGlobalRest(head).Origin.Y;
            foreach (var g in _geometry) g.SetInstanceShaderParameter(SwayHeadParam, _swayHead);
        }
        var camera = GetViewport()?.GetCamera3D();
        bool near = SwayDistance > 0f && camera != null && camera.GlobalPosition.DistanceSquaredTo(GlobalPosition) < SwayDistance * SwayDistance;
        if (!near)
        {
            if (_swaying)
            {
                _swaying = false;
                foreach (var g in _geometry) g.SetInstanceShaderParameter(SwayParam, new Color(0, 0, 0, 0));
            }
            return;
        }
        var anchor = GlobalTransform * new Vector3(0f, _swayHead, 0f);
        var at = new System.Numerics.Vector3(anchor.X, anchor.Y, anchor.Z);
        if (!_swaying) _spring.Reset(at);
        else _spring.Step(at, dt);
        _swaying = true;
        var offset = new Vector3(_spring.Offset.X, _spring.Offset.Y, _spring.Offset.Z);
        foreach (var g in _geometry)
        {
            var local = g.GlobalBasis.Inverse() * offset;
            g.SetInstanceShaderParameter(SwayParam, new Color(local.X, local.Y, local.Z, 1f));
        }
    }
}
