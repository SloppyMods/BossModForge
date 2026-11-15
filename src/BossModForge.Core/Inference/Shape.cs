using System.Collections.Immutable;
using System.Numerics;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference;

public enum ShapeKind { Circle, Donut, Cone, Rect, Cross, RaidwideNoShape, SingleTarget }
public enum ShapeOrigin { Source, CastLocation, TargetActor }
public enum ShapeDirection { SourceRotation, ToTarget, CastRotation }

public sealed record Shape(
    ShapeKind Kind,
    float Radius,
    float InnerRadius,
    float HalfAngleDeg,
    float HalfWidth,
    float LengthBack,
    ShapeOrigin Origin,
    ShapeDirection Direction)
{
    public static Shape Circle(float r, ShapeOrigin origin = ShapeOrigin.Source)
        => new(ShapeKind.Circle, r, 0, 0, 0, 0, origin, ShapeDirection.SourceRotation);

    public static Shape Donut(float rIn, float rOut, ShapeOrigin origin = ShapeOrigin.Source)
        => new(ShapeKind.Donut, rOut, rIn, 0, 0, 0, origin, ShapeDirection.SourceRotation);

    public static Shape Cone(float r, float halfAngleDeg, ShapeOrigin origin = ShapeOrigin.Source, ShapeDirection dir = ShapeDirection.SourceRotation)
        => new(ShapeKind.Cone, r, 0, halfAngleDeg, 0, 0, origin, dir);

    public static Shape Rect(float length, float halfWidth, float lengthBack = 0, ShapeOrigin origin = ShapeOrigin.Source, ShapeDirection dir = ShapeDirection.SourceRotation)
        => new(ShapeKind.Rect, length, 0, 0, halfWidth, lengthBack, origin, dir);

    public static Shape Cross(float length, float halfWidth, ShapeOrigin origin = ShapeOrigin.Source, ShapeDirection dir = ShapeDirection.SourceRotation)
        => new(ShapeKind.Cross, length, 0, 0, halfWidth, 0, origin, dir);

    public static readonly Shape Raidwide = new(ShapeKind.RaidwideNoShape, 0, 0, 0, 0, 0, ShapeOrigin.Source, ShapeDirection.SourceRotation);
    public static readonly Shape SingleTarget = new(ShapeKind.SingleTarget, 0, 0, 0, 0, 0, ShapeOrigin.TargetActor, ShapeDirection.ToTarget);

    public bool Contains(Vector2 point, Vector2 origin, Angle direction)
    {
        var rel = point - origin;
        var dist = rel.Length();
        var facing = Geometry2D.Facing(direction);
        var fwd = Vector2.Dot(rel, facing);
        var lat = Vector2.Dot(rel, Geometry2D.Right(facing));
        switch (Kind)
        {
            case ShapeKind.Circle:
                return dist <= Radius;
            case ShapeKind.Donut:
                return dist >= InnerRadius && dist <= Radius;
            case ShapeKind.Cone:
                if (dist > Radius)
                    return false;
                if (dist < 1e-5f)
                    return true;
                return MathF.Abs(Geometry2D.SignedAngleDeg(rel, facing)) <= HalfAngleDeg;
            case ShapeKind.Rect:
                return fwd >= -LengthBack && fwd <= Radius && MathF.Abs(lat) <= HalfWidth;
            case ShapeKind.Cross:
                return (MathF.Abs(fwd) <= Radius && MathF.Abs(lat) <= HalfWidth)
                    || (MathF.Abs(lat) <= Radius && MathF.Abs(fwd) <= HalfWidth);
            case ShapeKind.RaidwideNoShape:
                return true;
            default:
                return false;
        }
    }
}

public sealed record ShapePrior(
    ShapeKind Kind,
    float? Radius,
    float? InnerRadius,
    float? HalfAngleDeg,
    float? HalfWidth,
    ShapeOrigin Origin,
    ShapeDirection Direction,
    bool FromOmen);

public sealed record ShapeObservation(
    Vector2 Source,
    Angle SourceRotation,
    Vector2 CastLocation,
    Vector2? TargetActor,
    ImmutableArray<(Vector2 Pos, bool Hit)> Players);

public sealed record ShapeFit(
    Shape Shape,
    Shape RawShape,
    double Score,
    double FitConsistency,
    double PriorMatch,
    double Ambiguity,
    float MinMargin,
    float LagSeconds,
    int Instances,
    Confidence Confidence,
    ImmutableArray<Evidence> Evidence);
