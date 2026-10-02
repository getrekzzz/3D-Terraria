namespace Strata.Core.Presentation;

/// <summary>
/// One <see cref="UiLayout"/> placement pass: the regions placed so far and the three ways a new region finds room
/// among them: as is (<see cref="TryPut"/>), moved to the nearest free spot (<see cref="Search"/>) or cut down from
/// its anchored corner (<see cref="Shrink"/>). Nothing placed through these ever overlaps or leaves the screen.
/// </summary>
internal sealed class UiLayoutPass
{
    private const int MaxCuts = 10; // depth of the Shrink search

    private readonly Dictionary<string, UiRect> _rects = new();
    private readonly List<UiRect> _placed = new();

    public UiLayoutPass(float width, float height)
    {
        Width = width;
        Height = height;
        Screen = new UiRect(0, 0, width, height);
    }

    public float Width { get; }
    public float Height { get; }
    public UiRect Screen { get; }
    public bool MinimapHidden { get; set; }

    public bool Has(string region) => _rects.ContainsKey(region);

    public UiRect Get(string region) => _rects.TryGetValue(region, out var r) ? r : default;

    /// <summary>True when <paramref name="r"/> is on screen and overlaps nothing placed.</summary>
    public bool Fits(UiRect r) => !r.IsEmpty && r.Inside(Screen) && Blocker(r) is null;

    public void Put(string region, UiRect r)
    {
        Remove(region);
        _rects[region] = r;
        _placed.Add(r);
    }

    public void Remove(string region)
    {
        if (_rects.Remove(region, out var old)) _placed.Remove(old);
    }

    /// <summary>Places <paramref name="r"/> as is when it fits.</summary>
    public bool TryPut(string region, UiRect r)
    {
        if (!Fits(r)) return false;
        Put(region, r);
        return true;
    }

    /// <summary>Places the first rectangle of <paramref name="candidates"/> that fits.</summary>
    public bool TryPutAny(string region, params UiRect[] candidates)
    {
        foreach (var r in candidates)
            if (TryPut(region, r)) return true;
        return false;
    }

    /// <summary>Places <paramref name="r"/>, or the nearest free spot for its size (see <see cref="Search"/>).</summary>
    public bool PutNear(string region, UiRect r)
    {
        var found = Search(r);
        if (found is null) return false;
        Put(region, found.Value);
        return true;
    }

    /// <summary>Places the largest cut of <paramref name="r"/> that fits (see <see cref="Shrink"/>).</summary>
    public bool PutShrunk(string region, UiRect r, bool fromLeft, bool fromTop, float minW, float minH)
    {
        var found = Shrink(r, fromLeft, fromTop, minW, minH);
        if (found is null) return false;
        Put(region, found.Value);
        return true;
    }

    /// <summary>The first placed rectangle <paramref name="r"/> overlaps, if any.</summary>
    public UiRect? Blocker(UiRect r)
    {
        foreach (var p in _placed)
            if (p.Intersects(r)) return p;
        return null;
    }

    /// <summary>
    /// <paramref name="r"/> moved to the nearest spot where it fits. A free spot can always slide until it touches a
    /// placed rectangle or the screen margin, so only positions lined up with those edges (and the preferred position)
    /// are tried. Null when nowhere on screen fits.
    /// </summary>
    public UiRect? Search(UiRect r)
    {
        if (Fits(r)) return r;
        float g = UiLayout.Gap, m = UiLayout.Margin;
        var xs = new List<float>(2 * _placed.Count + 3) { r.X, m, Width - m - r.W };
        var ys = new List<float>(2 * _placed.Count + 3) { r.Y, m, Height - m - r.H };
        foreach (var p in _placed)
        {
            xs.Add(p.Right + g);
            xs.Add(p.X - g - r.W);
            ys.Add(p.Bottom + g);
            ys.Add(p.Y - g - r.H);
        }
        UiRect? best = null;
        float bestDist = float.MaxValue;
        foreach (float x in xs)
            foreach (float y in ys)
            {
                float d = (x - r.X) * (x - r.X) + (y - r.Y) * (y - r.Y);
                if (d >= bestDist) continue;
                var c = r with { X = x, Y = y };
                if (!Fits(c)) continue;
                best = c;
                bestDist = d;
            }
        return best;
    }

    /// <summary>
    /// The largest cut of <paramref name="r"/> (clipped to the screen) that overlaps nothing placed, keeping its corner on
    /// the <paramref name="fromLeft"/> / <paramref name="fromTop"/> side: each overlap is resolved by pulling either the
    /// far vertical edge or the far horizontal edge back past the blocker, and every combination is tried. Null when no
    /// cut is at least <paramref name="minW"/> × <paramref name="minH"/>.
    /// </summary>
    public UiRect? Shrink(UiRect r, bool fromLeft, bool fromTop, float minW, float minH)
    {
        float x0 = MathF.Max(r.X, 0), y0 = MathF.Max(r.Y, 0);
        var clipped = new UiRect(x0, y0, MathF.Min(r.Right, Width) - x0, MathF.Min(r.Bottom, Height) - y0);
        return Cut(clipped, fromLeft, fromTop, minW, minH, 0);
    }

    private UiRect? Cut(UiRect r, bool fromLeft, bool fromTop, float minW, float minH, int depth)
    {
        if (r.W < minW - 0.01f || r.H < minH - 0.01f) return null;
        if (Blocker(r) is not { } b) return r;
        if (depth >= MaxCuts) return null;
        var narrow = fromLeft ? r with { W = b.X - UiLayout.Gap - r.X }
            : r with { X = b.Right + UiLayout.Gap, W = r.Right - b.Right - UiLayout.Gap };
        var shorter = fromTop ? r with { H = b.Y - UiLayout.Gap - r.Y }
            : r with { Y = b.Bottom + UiLayout.Gap, H = r.Bottom - b.Bottom - UiLayout.Gap };
        var best = Cut(narrow, fromLeft, fromTop, minW, minH, depth + 1);
        var other = Cut(shorter, fromLeft, fromTop, minW, minH, depth + 1);
        if (other is { } o && (best is not { } bb || o.W * o.H > bb.W * bb.H)) best = o;
        return best;
    }

    public UiLayoutResult Result(float slotScale, bool minimapHidden) =>
        new(Width, Height, new Dictionary<string, UiRect>(_rects), slotScale, minimapHidden);
}
