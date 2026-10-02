namespace Strata.Core.Presentation;

/// <summary>
/// Decides the dynamic 3D resolution scale without oscillating (every change reallocates the viewport's buffers, a
/// hitch of its own). Fed every frame with the frame's length and the GPU's render time, it judges 30-frame windows,
/// leaving out hitch frames over 50 ms (CPU stalls such as saves, which fewer pixels would not fix). It steps down one
/// level after two consecutive slow, GPU-bound windows, and up only when the GPU time predicted at the next level
/// (scaling with pixel count, gpu × (next / current)²) stays under 12 ms for four windows and at least 8 s have passed
/// since the last step down. Levels are 1.0, 0.9, 0.8, 0.7 and 0.6, capped at the preset's render scale (which is
/// itself the top level), with at least 3 s between any two changes. Drivers without GPU timing (0 ms) are judged by
/// frame times alone. Deterministic and allocation-free.
/// </summary>
public sealed class ResolutionGovernor
{
    public const int Window = 30, DownWindows = 2, UpWindows = 4;
    public const double HitchMs = 50, SlowMs = 1000.0 / 57.0, GpuBusyMs = 13.5, UpBudgetMs = 12, UpCooldownS = 8, MinGapS = 3;
    private static readonly float[] Ladder = { 1.0f, 0.9f, 0.8f, 0.7f, 0.6f };

    private float _cap = 1f;
    private int _level; // 0 = the cap, then the ladder's levels below it
    private int _frames, _slow, _good;
    private double _frameSum, _gpuSum, _clockS, _lastChangeS = double.NegativeInfinity, _lastDownS = double.NegativeInfinity;

    public ResolutionGovernor(float cap) => Reset(cap);

    /// <summary>The scale to render at.</summary>
    public float Scale => LevelScale(_level);

    /// <summary>Scale changes made (probe <c>render.scale_changes</c>).</summary>
    public int Changes { get; private set; }

    /// <summary>Starts over at a (new) preset's render scale.</summary>
    public void Reset(float cap)
    {
        _cap = cap;
        _level = 0;
        _frames = _slow = _good = 0;
        _frameSum = _gpuSum = 0;
        _lastChangeS = _lastDownS = double.NegativeInfinity;
    }

    /// <summary>Feeds one frame; returns true when <see cref="Scale"/> changed.</summary>
    public bool Feed(double frameMs, double gpuMs)
    {
        _clockS += frameMs / 1000.0;
        if (frameMs > HitchMs) return false;
        _frameSum += frameMs;
        _gpuSum += gpuMs;
        if (++_frames < Window) return false;
        double frame = _frameSum / _frames, gpu = _gpuSum / _frames;
        _frames = 0;
        _frameSum = _gpuSum = 0;
        bool measured = gpu > 0.01;
        bool slow = frame > SlowMs && (!measured || gpu > GpuBusyMs);
        _slow = slow ? _slow + 1 : 0;
        bool roomUp = false;
        if (_level > 0)
        {
            float ratio = LevelScale(_level - 1) / LevelScale(_level);
            roomUp = (measured ? gpu : frame) * ratio * ratio < UpBudgetMs;
        }
        _good = roomUp ? _good + 1 : 0;
        if (_clockS - _lastChangeS < MinGapS) return false;
        if (_slow >= DownWindows && _level < LevelCount - 1)
        {
            _level++;
            _lastDownS = _clockS;
            return Changed();
        }
        if (_good >= UpWindows && _clockS - _lastDownS >= UpCooldownS)
        {
            _level--;
            return Changed();
        }
        return false;
    }

    private bool Changed()
    {
        _lastChangeS = _clockS;
        _slow = _good = 0;
        Changes++;
        return true;
    }

    private int LevelCount
    {
        get
        {
            int n = 1;
            foreach (var l in Ladder) if (l < _cap - 0.001f) n++;
            return n;
        }
    }

    private float LevelScale(int level)
    {
        if (level == 0) return _cap;
        foreach (var l in Ladder)
            if (l < _cap - 0.001f && --level == 0) return l;
        return _cap;
    }
}
