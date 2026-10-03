using DeathrestUnbound.Core;
using NUnit.Framework;

namespace DeathrestUnbound.Core.Tests;

public class PowerMathTests
{
    [TestCase(0, 100f)]
    [TestCase(1, 150f)]
    [TestCase(2, 225f)]
    [TestCase(4, 506.25f)]
    [TestCase(10, 5766.504f)]
    public void CasketDraw_IsExponentialInWantedLinks(int n, float expected)
    {
        Assert.That(PowerMath.CasketDraw(100f, 1.5f, n, scaling: true), Is.EqualTo(expected).Within(0.01f));
    }

    [Test]
    public void CasketDraw_UsesConfiguredExponent()
    {
        Assert.That(PowerMath.CasketDraw(100f, 2f, 3, scaling: true), Is.EqualTo(800f).Within(0.001f));
        Assert.That(PowerMath.CasketDraw(100f, 1f, 30, scaling: true), Is.EqualTo(100f).Within(0.001f));
    }

    [Test]
    public void CasketDraw_ScalingOffIsBase()
    {
        Assert.That(PowerMath.CasketDraw(100f, 1.5f, 12, scaling: false), Is.EqualTo(100f));
    }

    [Test]
    public void CasketDraw_ClampsHugeValues()
    {
        Assert.That(PowerMath.CasketDraw(100f, 3f, 500, scaling: true), Is.EqualTo(PowerMath.MaxDraw));
    }

    [Test]
    public void BuildingDraw_IsLinearInActiveLinks()
    {
        Assert.That(PowerMath.BuildingDraw(800f, 0f, 0, scaling: true), Is.EqualTo(0f));
        Assert.That(PowerMath.BuildingDraw(800f, 0f, 1, scaling: true), Is.EqualTo(800f));
        Assert.That(PowerMath.BuildingDraw(800f, 0f, 2, scaling: true), Is.EqualTo(1600f));
        Assert.That(PowerMath.BuildingDraw(800f, 0f, 5, scaling: false), Is.EqualTo(800f));
        Assert.That(PowerMath.BuildingDraw(800f, 25f, 0, scaling: true), Is.EqualTo(25f));
    }

    [Test]
    public void FuelUses_ScaleWithActiveLinks()
    {
        Assert.That(PowerMath.FuelUsesPerTick(0, true), Is.EqualTo(0));
        Assert.That(PowerMath.FuelUsesPerTick(3, true), Is.EqualTo(3));
        Assert.That(PowerMath.FuelUsesPerTick(3, false), Is.EqualTo(1));
    }

    [Test]
    public void FuelTicksLeft_CountsIdleBurnPlusUses()
    {
        // Vanilla hemopump: 5 fuel at 0.5/day = 10 days for one user; serving 3 caskets burns 3x (no idle burn).
        Assert.That(PowerMath.FuelTicksLeft(5f, 0.5f, false, 1), Is.EqualTo(600000));
        Assert.That(PowerMath.FuelTicksLeft(5f, 0.5f, false, 3), Is.EqualTo(200000));
        Assert.That(PowerMath.FuelTicksLeft(5f, 0.5f, true, 0), Is.EqualTo(600000));
        Assert.That(PowerMath.FuelTicksLeft(5f, 0.5f, true, 3), Is.EqualTo(150000));
        Assert.That(PowerMath.FuelTicksLeft(5f, 0.5f, false, 2), Is.EqualTo(300000));
        Assert.That(PowerMath.FuelTicksLeft(5f, 0.5f, false, 0), Is.EqualTo(-1));
        Assert.That(PowerMath.FuelTicksLeft(0f, 0.5f, true, 3), Is.EqualTo(-1));
    }
}
