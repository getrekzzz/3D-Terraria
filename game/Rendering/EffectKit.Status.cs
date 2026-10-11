using System.Collections.Generic;
using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.Rendering;

/// <summary>
/// Looping status emitters (buff and debuff particles, StatusVisuals): a second pool beside the one-shot bursts, sharing
/// each recipe's mesh and voxel-lit material. A handle is borrowed while a status lasts and handed back when it ends;
/// at most the preset's <c>statusEmitters</c> are out at once and their rate scales by <c>effectsScale</c>. The pool
/// only grows (to the largest budget seen), so a preset change never invalidates a handle in use.
/// </summary>
public sealed partial class EffectKit
{
    private const float StatusLifetime = 0.7f;
    private readonly List<StatusEmitter> _status = new();
    private int _statusBudget;

    /// <summary>Status emitters currently borrowed (probes: <c>fx.status_emitters</c>).</summary>
    public int StatusInUse { get; private set; }

    private void InitializeStatus(GraphicsSettings graphics)
    {
        ResizeStatus(graphics.Preset.StatusEmitters);
        graphics.Changed += p => ResizeStatus(p.StatusEmitters);
    }

    private void ResizeStatus(int budget)
    {
        _statusBudget = budget;
        while (_status.Count < budget) _status.Add(new StatusEmitter(this));
    }

    /// <summary>
    /// Borrows a looping emitter of a recipe's look: <paramref name="rate"/> particles per second (scaled by the preset),
    /// drifting up (<paramref name="rise"/> &gt; 0) or dripping down. Returns -1 when the budget is spent.
    /// </summary>
    public int AcquireStatus(EffectRecipe recipe, Color color, float rate, float rise)
    {
        if (StatusInUse >= _statusBudget) return -1;
        int amount = Mathf.Clamp(Mathf.RoundToInt(rate * _scale * StatusLifetime), 0, 48);
        if (amount == 0) return -1;
        for (int i = 0; i < _status.Count; i++)
        {
            if (_status[i].InUse) continue;
            _status[i].Start(_meshes[(int)recipe], recipe == EffectRecipe.Sparks, color, amount, rise);
            StatusInUse++;
            return i;
        }
        return -1;
    }

    /// <summary>Moves a borrowed emitter onto its carrier (centre and half extents of the body) and relights it.</summary>
    public void MoveStatus(int handle, Vector3 center, Vector3 halfExtents, bool visible)
    {
        if ((uint)handle >= (uint)_status.Count || _world == null) return;
        _status[handle].Move(center, halfExtents, visible, LightProbe.Sample(_world, center));
    }

    /// <summary>Hands a borrowed emitter back (its live particles fade out on their own).</summary>
    public void ReleaseStatus(int handle)
    {
        if ((uint)handle >= (uint)_status.Count || !_status[handle].InUse) return;
        _status[handle].Stop();
        StatusInUse--;
    }

    private sealed class StatusEmitter
    {
        private readonly CpuParticles3D _node;
        private readonly Gradient _ramp = new();

        public StatusEmitter(Node3D parent)
        {
            _ramp.SetColor(0, Colors.White);
            _ramp.SetColor(1, Colors.White);
            _node = new CpuParticles3D
            {
                Emitting = false,
                OneShot = false,
                Lifetime = StatusLifetime,
                Explosiveness = 0f,
                Randomness = 0.6f,
                ColorInitialRamp = _ramp,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                LocalCoords = false,
                EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
                Spread = 25f,
                DampingMin = 1f,
                DampingMax = 2f,
                ScaleAmountMin = 0.5f,
                ScaleAmountMax = 1.1f,
                ScaleAmountCurve = FadeOut(),
                Visible = false,
            };
            parent.AddChild(_node);
        }

        public bool InUse { get; private set; }

        public void Start(Mesh mesh, bool glow, Color color, int amount, float rise)
        {
            InUse = true;
            _node.Mesh = mesh;
            _ramp.SetColor(0, color.Lightened(glow ? 0.25f : 0.12f));
            _ramp.SetColor(1, color.Darkened(0.15f));
            if (_node.Amount != amount) _node.Amount = amount;
            _node.Direction = rise >= 0 ? Vector3.Up : Vector3.Down;
            float speed = Mathf.Abs(rise);
            _node.InitialVelocityMin = speed * 0.3f;
            _node.InitialVelocityMax = speed * 0.8f;
            _node.Gravity = new Vector3(0, rise * 1.5f, 0);
            _node.Emitting = true;
        }

        public void Move(Vector3 center, Vector3 half, bool visible, Color light)
        {
            _node.GlobalPosition = center;
            _node.EmissionBoxExtents = half * 0.8f;
            _node.Visible = visible;
            _node.SetInstanceShaderParameter(LightParam, light);
        }

        public void Stop()
        {
            InUse = false;
            _node.Emitting = false;
        }

        private static Curve FadeOut()
        {
            var c = new Curve();
            c.AddPoint(new Vector2(0f, 0.6f));
            c.AddPoint(new Vector2(0.25f, 1f));
            c.AddPoint(new Vector2(1f, 0f));
            return c;
        }
    }
}
