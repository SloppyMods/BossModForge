using System.Globalization;
using BossModForge.Core.Inference;

namespace BossModForge.Core.Emit;

public static class ShapeCSharp
{
    public static string Render(Shape shape, FlavorProfile profile)
    {
        return shape.Kind switch
        {
            ShapeKind.Circle => T(profile, "shapeCircle").Replace("{r}", Num(shape.Radius), StringComparison.Ordinal),
            ShapeKind.Donut => T(profile, "shapeDonut")
                .Replace("{rIn}", Num(shape.InnerRadius), StringComparison.Ordinal)
                .Replace("{rOut}", Num(shape.Radius), StringComparison.Ordinal),
            ShapeKind.Cone => T(profile, "shapeCone")
                .Replace("{r}", Num(shape.Radius), StringComparison.Ordinal)
                .Replace("{halfAngle}", Num(shape.HalfAngleDeg), StringComparison.Ordinal),
            ShapeKind.Rect when shape.LengthBack > 0 => T(profile, "shapeRectBack")
                .Replace("{length}", Num(shape.Radius), StringComparison.Ordinal)
                .Replace("{halfWidth}", Num(shape.HalfWidth), StringComparison.Ordinal)
                .Replace("{lengthBack}", Num(shape.LengthBack), StringComparison.Ordinal),
            ShapeKind.Rect => T(profile, "shapeRect")
                .Replace("{length}", Num(shape.Radius), StringComparison.Ordinal)
                .Replace("{halfWidth}", Num(shape.HalfWidth), StringComparison.Ordinal),
            ShapeKind.Cross => T(profile, "shapeCross")
                .Replace("{length}", Num(shape.Radius), StringComparison.Ordinal)
                .Replace("{halfWidth}", Num(shape.HalfWidth), StringComparison.Ordinal),
            _ => throw new NotSupportedException("Cannot emit C# for shape kind " + shape.Kind),
        };
    }

    internal static string Num(float value)
    {
        var rounded = MathF.Round(value, 1, MidpointRounding.AwayFromZero);
        var asInt = MathF.Round(rounded, MidpointRounding.AwayFromZero);
        if (MathF.Abs(rounded - asInt) < 0.05f)
            return ((int)asInt).ToString(CultureInfo.InvariantCulture);
        return rounded.ToString("0.0", CultureInfo.InvariantCulture) + "f";
    }

    private static string T(FlavorProfile profile, string key)
        => profile.Templates.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException("Flavor template '" + key + "' is missing.");
}
