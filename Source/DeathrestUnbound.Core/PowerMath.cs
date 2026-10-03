using System;

namespace DeathrestUnbound.Core;

/// <summary>Power and fuel scaling rules. Pure so they can be unit-tested outside the game.</summary>
public static class PowerMath
{
    /// <summary>Upper bound for any draw we report; the power net misbehaves with infinities.</summary>
    public const float MaxDraw = 1e9f;

    /// <summary>Casket draw while deathresting: <c>basePower * exponent^wanted</c>, or the plain base when scaling is off.</summary>
    public static float CasketDraw(float basePower, float exponent, int wantedLinks, bool scaling)
    {
        if (!scaling || wantedLinks <= 0)
        {
            return Clamp(basePower);
        }
        return Clamp((float)(basePower * Math.Pow(Math.Max(exponent, 0d), wantedLinks)));
    }

    /// <summary>Building draw: <c>basePower * activeLinks</c>, the idle draw when nothing uses it, or the plain base when scaling is off.</summary>
    public static float BuildingDraw(float basePower, float idlePower, int activeLinks, bool scaling)
    {
        if (activeLinks <= 0)
        {
            return Clamp(idlePower);
        }
        return Clamp(scaling ? basePower * activeLinks : basePower);
    }

    /// <summary>How many times a refuelable building consumes its per-tick fuel rate this tick.</summary>
    public static int FuelUsesPerTick(int activeLinks, bool scaling)
    {
        if (activeLinks <= 0)
        {
            return 0;
        }
        return scaling ? activeLinks : 1;
    }

    /// <summary>
    /// Ticks until a refuelable building runs dry: its idle burn (vanilla burns fuel whenever it is switched on)
    /// plus one extra burn per use. Returns -1 when nothing is burning.
    /// </summary>
    public static int FuelTicksLeft(float fuel, float ratePerDay, bool idleBurn, int usesPerTick)
    {
        float multiplier = (idleBurn ? 1f : 0f) + Math.Max(usesPerTick, 0);
        if (fuel <= 0f || ratePerDay <= 0f || multiplier <= 0f)
        {
            return -1;
        }
        return (int)(fuel / (ratePerDay * multiplier) * 60000f);
    }

    private static float Clamp(float draw)
    {
        if (float.IsNaN(draw) || draw < 0f)
        {
            return 0f;
        }
        return draw > MaxDraw ? MaxDraw : draw;
    }
}
