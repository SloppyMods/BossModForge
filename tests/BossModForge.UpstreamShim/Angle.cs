namespace BossMod;

public record struct Angle(float Rad)
{
    public const float RadToDeg = 180 / MathF.PI;
    public const float DegToRad = MathF.PI / 180;

    public readonly float Deg => Rad * RadToDeg;

    public static Angle FromDirection(WDir dir) => new(MathF.Atan2(dir.X, dir.Z));
    public readonly WDir ToDirection() => new(Sin(), Cos());

    public static Angle operator +(Angle a, Angle b) => new(a.Rad + b.Rad);
    public static Angle operator -(Angle a, Angle b) => new(a.Rad - b.Rad);
    public static Angle operator -(Angle a) => new(-a.Rad);
    public static Angle operator *(Angle a, float b) => new(a.Rad * b);
    public static Angle operator *(float a, Angle b) => new(a * b.Rad);
    public static Angle operator /(Angle a, float b) => new(a.Rad / b);
    public readonly Angle Abs() => new(Math.Abs(Rad));
    public readonly float Sin() => MathF.Sin(Rad);
    public readonly float Cos() => MathF.Cos(Rad);
    public readonly float Tan() => MathF.Tan(Rad);
    public static Angle Asin(float x) => new(MathF.Asin(x));
    public static Angle Acos(float x) => new(MathF.Acos(x));

    public override readonly string ToString() => Deg.ToString("f0");
}

public static class AngleExtensions
{
    public static Angle Radians(this float radians) => new(radians);
    public static Angle Degrees(this float degrees) => new(degrees * Angle.DegToRad);
    public static Angle Degrees(this int degrees) => new(degrees * Angle.DegToRad);
}
