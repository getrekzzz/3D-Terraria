using Godot;

namespace Strata.Game.Ambience;

/// <summary>
/// The atmosphere model of shaders/sky.gdshader on the CPU (kilometres, Rayleigh + Mie + ozone): the colour of
/// sunlight after crossing the air (reddening at dusk) and a sky irradiance estimate for clouds and sprites.
/// </summary>
public static class Atmosphere
{
    private const float PlanetRadius = 6360f;
    private const float TopRadius = 6420f;
    private const float ViewerAltitude = 0.2f;
    private static readonly Vector3 BetaRayleigh = new(5.802e-3f, 13.558e-3f, 33.1e-3f);
    private const float BetaMie = 3.996e-3f;
    private const float BetaMieExtinction = 4.4e-3f;
    private static readonly Vector3 BetaOzone = new(0.650e-3f, 1.881e-3f, 0.085e-3f);

    /// <summary>Fraction of light (per channel) reaching the ground from a light in direction <paramref name="toLight"/>.</summary>
    public static Vector3 Transmittance(Vector3 toLight)
    {
        var o = new Vector3(0, PlanetRadius + ViewerAltitude, 0);
        var l = toLight.Normalized();
        if (HitsPlanet(o, l)) return Vector3.Zero;
        var od = OpticalDepth(o, l, 24);
        return Exp(-(BetaRayleigh * od.X + Vector3.One * (BetaMieExtinction * od.Y) + BetaOzone * od.Z));
    }

    /// <summary>In-scattered sky radiance looking along <paramref name="dir"/> (same units as the sky shader).</summary>
    public static Vector3 Scatter(Vector3 dir, Vector3 toLight, float intensity, int samples = 10, int lightSamples = 4)
    {
        if (intensity <= 0f || toLight.Y < -0.3f) return Vector3.Zero;
        var d = new Vector3(dir.X, Mathf.Max(dir.Y, 0.002f), dir.Z).Normalized();
        var l = toLight.Normalized();
        var o = new Vector3(0, PlanetRadius + ViewerAltitude, 0);
        float tmax = RayTop(o, d);
        var od = Vector3.Zero;
        Vector3 sr = Vector3.Zero, sm = Vector3.Zero;
        for (int i = 0; i < samples; i++)
        {
            float f0 = i / (float)samples, f1 = (i + 1) / (float)samples;
            float t0 = tmax * f0 * f0, t1 = tmax * f1 * f1, ds = t1 - t0;
            var p = o + d * (0.5f * (t0 + t1));
            var rho = Densities(p.Length() - PlanetRadius) * ds;
            od += rho;
            if (HitsPlanet(p, l)) continue;
            var lod = OpticalDepth(p, l, lightSamples);
            var tau = BetaRayleigh * (od.X + lod.X) + Vector3.One * (BetaMieExtinction * (od.Y + lod.Y)) + BetaOzone * (od.Z + lod.Z);
            var tr = Exp(-tau);
            sr += tr * rho.X;
            sm += tr * rho.Y;
        }
        float mu = d.Dot(l);
        float pr = 3f / (16f * Mathf.Pi) * (1f + mu * mu);
        const float g = 0.76f;
        float pm = 3f / (8f * Mathf.Pi) * ((1f - g * g) * (1f + mu * mu)) / ((2f + g * g) * Mathf.Pow(1f + g * g - 2f * g * mu, 1.5f));
        return intensity * (sr * BetaRayleigh * pr + sm * (BetaMie * pm));
    }

    /// <summary>Cosine-weighted average sky radiance over the upper hemisphere (a few directions).</summary>
    public static Vector3 Irradiance(Vector3 toLight, float intensity)
    {
        var sum = Scatter(Vector3.Up, toLight, intensity) * 2f;
        for (int k = 0; k < 6; k++)
        {
            float a = k * Mathf.Tau / 6f;
            sum += Scatter(new Vector3(Mathf.Cos(a) * 0.87f, 0.5f, Mathf.Sin(a) * 0.87f), toLight, intensity);
        }
        return sum / 8f;
    }

    private static Vector3 OpticalDepth(Vector3 p, Vector3 l, int steps)
    {
        float dl = RayTop(p, l) / steps;
        var od = Vector3.Zero;
        for (int j = 0; j < steps; j++)
            od += Densities(Mathf.Max((p + l * ((j + 0.5f) * dl)).Length() - PlanetRadius, 0f));
        return od * dl;
    }

    private static Vector3 Densities(float h) =>
        new(Mathf.Exp(-h / 8f), Mathf.Exp(-h / 1.2f), Mathf.Max(0f, 1f - Mathf.Abs(h - 25f) / 15f));

    private static float RayTop(Vector3 o, Vector3 d)
    {
        float b = o.Dot(d);
        float c = o.Dot(o) - TopRadius * TopRadius;
        return -b + Mathf.Sqrt(Mathf.Max(b * b - c, 0f));
    }

    private static bool HitsPlanet(Vector3 o, Vector3 d)
    {
        float b = o.Dot(d);
        return b < 0f && b * b - (o.Dot(o) - PlanetRadius * PlanetRadius) > 0f;
    }

    private static Vector3 Exp(Vector3 v) => new(Mathf.Exp(v.X), Mathf.Exp(v.Y), Mathf.Exp(v.Z));
}
