namespace BossMod;

public abstract record class AOEShape;

public sealed record class AOEShapeCone(float Radius, Angle HalfAngle, Angle DirectionOffset = default) : AOEShape;

public sealed record class AOEShapeCircle(float Radius) : AOEShape;

public sealed record class AOEShapeDonut(float InnerRadius, float OuterRadius) : AOEShape;

public sealed record class AOEShapeRect(float LengthFront, float HalfWidth, float LengthBack = 0, Angle DirectionOffset = default) : AOEShape;

public sealed record class AOEShapeCross(float Length, float HalfWidth, Angle DirectionOffset = default) : AOEShape;
