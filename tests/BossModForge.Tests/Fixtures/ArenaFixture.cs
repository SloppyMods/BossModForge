using System.Collections.Immutable;
using System.Numerics;
using BossModForge.Core.Inference;

namespace BossModForge.Tests.Fixtures;

public static class ArenaFixture
{
    public static ImmutableArray<Vector2> UniformDisc(int seed, int count, float radius, Vector2 center)
    {
        var rng = new Random(seed);
        var pts = ImmutableArray.CreateBuilder<Vector2>(count);
        for (var i = 0; i < count; i++)
        {
            var a = rng.NextDouble() * Math.PI * 2;
            var r = radius * Math.Sqrt(rng.NextDouble());
            pts.Add(center + new Vector2((float)(r * Math.Sin(a)), (float)(r * Math.Cos(a))));
        }

        return pts.ToImmutable();
    }

    public static ImmutableArray<Vector2> UniformRect(int seed, int count, float halfW, float halfH, Vector2 center)
    {
        var rng = new Random(seed);
        var pts = ImmutableArray.CreateBuilder<Vector2>(count);
        for (var i = 0; i < count; i++)
        {
            var x = (float)((rng.NextDouble() * 2 - 1) * halfW);
            var z = (float)((rng.NextDouble() * 2 - 1) * halfH);
            pts.Add(center + new Vector2(x, z));
        }

        return pts.ToImmutable();
    }

    public static ImmutableArray<Vector2> LShape(int seed, int count, Vector2 origin)
    {
        var rng = new Random(seed);
        var pts = ImmutableArray.CreateBuilder<Vector2>(count);
        for (var i = 0; i < count; i++)
        {
            if (rng.Next(2) == 0)
            {
                var x = (float)(rng.NextDouble() * 20);
                var z = (float)(rng.NextDouble() * 40);
                pts.Add(origin + new Vector2(x, z));
            }
            else
            {
                var x = (float)(rng.NextDouble() * 40);
                var z = (float)(rng.NextDouble() * 20);
                pts.Add(origin + new Vector2(x, z));
            }
        }

        return pts.ToImmutable();
    }
}
