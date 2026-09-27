using Strata.Core.Determinism;

namespace Strata.Core.Tests.Determinism;

public class TrigTests
{
    [Fact]
    public void SinCosMatchSystemMathClosely()
    {
        for (int i = -2000; i <= 2000; i++)
        {
            float turns = i / 500f;
            double radians = turns * 2 * Math.PI;
            Assert.InRange(Trig.Sin(turns) - Math.Sin(radians), -2e-4, 2e-4);
            Assert.InRange(Trig.Cos(turns) - Math.Cos(radians), -2e-4, 2e-4);
        }
    }

    [Fact]
    public void NoiseFieldIsDeterministic()
    {
        var a = new NoiseField(1234, 64f);
        var b = new NoiseField(1234, 64f);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.Sample(i * 3.7f, i * -1.3f, i * 0.9f), b.Sample(i * 3.7f, i * -1.3f, i * 0.9f));
        Assert.NotEqual(a.Sample(10.5f, 3.2f), new NoiseField(4321, 64f).Sample(10.5f, 3.2f));
    }
}
