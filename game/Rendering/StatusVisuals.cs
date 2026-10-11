using System;
using System.Collections.Generic;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Buffs;
using Strata.Core.Presentation;

namespace Strata.Game.Rendering;

/// <summary>
/// Buff and debuff looks on the player and on creatures (BuffDef <c>visual</c>): a pulsing body tint for the model's
/// instance <c>tint</c> uniform (<see cref="Tint"/>), a looping EffectKit status emitter per visible status and, for
/// glowing ones, a light from the DynamicLights pool (at most the preset's <c>statusLights</c>). Renderers call
/// <see cref="Track"/> every frame for each living carrier; a status not tracked for two frames (it ended, or its
/// carrier died or despawned) hands its emitter back, so nothing leaks. Allocation-free once warm.
/// </summary>
public sealed partial class StatusVisuals : Node
{
    /// <summary>A parsed visual (cached per buff definition).</summary>
    private sealed class Look
    {
        public Color Tint = Colors.White;
        public float Strength;
        public EffectRecipe? Recipe;
        public Color Particle;
        public float Rate, Rise;
        public Color LightColor;
        public float LightPeak;
    }

    /// <summary>One status on one carrier and the emitter it borrowed.</summary>
    private sealed class Entry
    {
        public int Handle = -1;
        public ulong Seen;
        public Vector3 Center;
        public Look Look = null!;
    }

    private static readonly Dictionary<BuffDef, Look> Looks = new();
    private readonly Dictionary<(long Carrier, BuffDef Buff), Entry> _entries = new();
    private readonly List<(long, BuffDef)> _stale = new();
    private readonly Stack<Entry> _free = new();
    private readonly List<Entry> _lit = new();
    private EffectKit _kit = null!;
    private int _lights;

    public static StatusVisuals? Instance { get; private set; }

    /// <summary>Status emitters attached right now (probe <c>fx.status_emitters</c>).</summary>
    public int Emitters { get; private set; }

    public void Initialize(EffectKit kit, GraphicsSettings graphics)
    {
        Name = "StatusVisuals";
        _kit = kit;
        Instance = this;
        Looks.Clear(); // definitions of an earlier world
        _lights = graphics.Preset.StatusLights;
        graphics.Changed += p => _lights = p.StatusLights;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>The carrier's tint with every visual status multiplied in, pulsing gently (seeded per carrier).</summary>
    public static Color Tint(BuffSet buffs, Color tint, float seed)
    {
        var active = buffs.Active;
        if (active.Count == 0) return tint;
        float pulse = 0.85f + 0.15f * Mathf.Sin((float)Time.GetTicksMsec() * 0.006f + seed * 6.283f);
        for (int i = 0; i < active.Count; i++)
        {
            var look = LookOf(active[i].Def);
            if (look == null || look.Strength <= 0f) continue;
            tint *= Colors.White.Lerp(look.Tint, look.Strength * pulse);
        }
        return tint;
    }

    /// <summary>Keeps the carrier's status emitters attached and following it this frame (call every frame while alive).</summary>
    public void Track(long carrier, BuffSet buffs, Vector3 center, Vector3 halfExtents, bool visible = true)
    {
        var active = buffs.Active;
        ulong frame = Engine.GetProcessFrames();
        for (int i = 0; i < active.Count; i++)
        {
            var def = active[i].Def;
            var look = LookOf(def);
            if (look?.Recipe is not { } recipe) continue;
            if (!_entries.TryGetValue((carrier, def), out var entry))
            {
                int handle = _kit.AcquireStatus(recipe, look.Particle, look.Rate, look.Rise);
                if (handle < 0) continue; // budget spent: tint only until an emitter frees up
                entry = _free.Count > 0 ? _free.Pop() : new Entry();
                entry.Handle = handle;
                entry.Look = look;
                _entries[(carrier, def)] = entry;
                Emitters++;
            }
            entry.Seen = frame;
            entry.Center = center;
            _kit.MoveStatus(entry.Handle, center, halfExtents, visible);
        }
    }

    public override void _Process(double delta)
    {
        ulong frame = Engine.GetProcessFrames();
        _stale.Clear();
        foreach (var (key, entry) in _entries)
            if (frame > entry.Seen + 2) _stale.Add(key);
        foreach (var key in _stale)
        {
            var entry = _entries[key];
            _entries.Remove(key);
            _kit.ReleaseStatus(entry.Handle);
            entry.Handle = -1;
            _free.Push(entry);
            Emitters--;
        }
    }

    /// <summary>Light candidates for DynamicLights: the nearest glowing statuses, at most the preset's statusLights.</summary>
    public IEnumerable<DynamicLights.Source> Lights(Vector3 eye, float radius)
    {
        _lit.Clear();
        if (_lights <= 0) yield break;
        foreach (var entry in _entries.Values)
            if (entry.Look.LightPeak > 0f && entry.Center.DistanceTo(eye) <= radius) _lit.Add(entry);
        _lit.Sort((a, b) => a.Center.DistanceSquaredTo(eye).CompareTo(b.Center.DistanceSquaredTo(eye)));
        for (int i = 0; i < Math.Min(_lights, _lit.Count); i++)
        {
            var e = _lit[i];
            yield return new DynamicLights.Source(e.Center, e.Look.LightColor, e.Look.LightPeak, DynamicLights.Kind.Fire,
                2.5f + e.Look.LightPeak * 4f, 0.6f, (i * 0.618034f) % 1f, () => e.Center);
        }
    }

    private static Look? LookOf(BuffDef def)
    {
        if (def.Visual is not { } v) return null;
        if (Looks.TryGetValue(def, out var look)) return look;
        look = new Look
        {
            Tint = v.Tint != null ? Color.FromHtml(v.Tint) : Colors.White,
            Strength = v.Tint != null ? v.TintStrength : 0f,
            Recipe = v.Particle != null && Enum.TryParse<EffectRecipe>(v.Particle, true, out var r) ? r : null,
            Particle = Color.FromHtml(v.Color ?? def.Color),
            Rate = v.Rate,
            Rise = v.Rise,
        };
        if (v.Light is { Length: 3 } l)
        {
            int max = Math.Max(1, Math.Max(l[0], Math.Max(l[1], l[2])));
            look.LightColor = new Color(l[0] / (float)max, l[1] / (float)max, l[2] / (float)max);
            look.LightPeak = max / 15f;
        }
        Looks[def] = look;
        return look;
    }
}
