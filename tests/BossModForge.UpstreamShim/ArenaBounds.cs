namespace BossMod;

public abstract record class ArenaBounds(float Radius, float MapResolution);

public record class ArenaBoundsCircle(float Radius, float MapResolution = 0.5f) : ArenaBounds(Radius, MapResolution);

public record class ArenaBoundsSquare(float Radius, float MapResolution = 0.5f) : ArenaBounds(Radius, MapResolution);

public record class ArenaBoundsRect(float HalfWidth, float HalfHeight, Angle Rotation = default, float Radius = 0, float MapResolution = 0.5f)
    : ArenaBounds(Radius > 0 ? Radius : MathF.Max(HalfWidth, HalfHeight), MapResolution);

public record class RelSimplifiedComplexPolygon;

public record class ArenaBoundsCustom(float Radius, RelSimplifiedComplexPolygon Poly, float MapResolution = 0.5f) : ArenaBounds(Radius, MapResolution);
