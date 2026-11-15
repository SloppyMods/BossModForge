using System.Globalization;
using System.Text;
using BossModForge.Core.Model;

namespace BossModForge.Core.Emit;

public sealed class VbmProfile : IEmitterProfile
{
    public VbmProfile(FlavorProfile profile)
    {
        Profile = profile;
        Flavor = profile.Flavor;
    }

    public Flavor Flavor { get; }
    public FlavorProfile Profile { get; }

    public string ModuleInfoAttribute(ModuleInfoArgs args)
    {
        var template = Template("moduleInfo");
        return template
            .Replace("{maturity}", args.Maturity, StringComparison.Ordinal)
            .Replace("{groupType}", args.GroupType, StringComparison.Ordinal)
            .Replace("{groupId}", args.GroupId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{nameId}", args.NameId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    public string ModuleDeclaration(string moduleName, string center, string bounds)
    {
        var parts = center.Split(',');
        var cx = parts.Length > 0 ? parts[0].Trim() : "0";
        var cz = parts.Length > 1 ? parts[1].Trim() : "0";
        return Template("moduleDecl")
            .Replace("{name}", moduleName, StringComparison.Ordinal)
            .Replace("{cx}", cx, StringComparison.Ordinal)
            .Replace("{cz}", cz, StringComparison.Ordinal)
            .Replace("{bounds}", bounds, StringComparison.Ordinal);
    }

    public string ArenaBounds(ArenaModel arena)
    {
        return arena.Kind switch
        {
            "Square" => Template("boundsSquare")
                .Replace("{halfWidth}", ShapeCSharp.Num(arena.HalfWidth), StringComparison.Ordinal),
            "Rect" => Template("boundsRect")
                .Replace("{halfWidth}", ShapeCSharp.Num(arena.HalfWidth), StringComparison.Ordinal)
                .Replace("{halfHeight}", ShapeCSharp.Num(arena.HalfHeight), StringComparison.Ordinal),
            "Custom" => Template("boundsCustom")
                .Replace("{vertices}", FormatVertices(arena), StringComparison.Ordinal),
            _ => Template("boundsCircle")
                .Replace("{radius}", ShapeCSharp.Num(arena.Radius), StringComparison.Ordinal),
        };
    }

    public string StatesClass(StatesPlan plan) => StatesClassRenderer.Render(plan, Profile);

    public string EnumDeclaration(string enumName, string underlying, IEnumerable<(string Name, uint Value, string? Comment)> members)
    {
        var sb = new StringBuilder();
        sb.Append("public enum ").Append(enumName).Append(" : ").Append(underlying).Append('\n');
        sb.Append("{\n");
        foreach (var (name, value, comment) in members)
        {
            sb.Append("    ").Append(name).Append(" = ");
            if (enumName == "OID")
                sb.Append("0x").Append(value.ToString("X", CultureInfo.InvariantCulture));
            else
                sb.Append(value.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            if (!string.IsNullOrEmpty(comment))
                sb.Append(" // ").Append(comment);
            sb.Append('\n');
        }

        sb.Append('}');
        return sb.ToString();
    }

    public string ComponentCtor(string kind, IReadOnlyDictionary<string, string> args)
    {
        return kind switch
        {
            "SimpleAOEs" => Template("componentSimpleAOEs")
                .Replace("{aid}", args["aid"], StringComparison.Ordinal)
                .Replace("{shape}", args["shape"], StringComparison.Ordinal),
            "SimpleAOEGroups" => Template("componentSimpleAOEGroups")
                .Replace("{aids}", args["aids"], StringComparison.Ordinal)
                .Replace("{shape}", args["shape"], StringComparison.Ordinal),
            "LocationTargetedAOEs" => Template("componentLocationTargetedAOEs")
                .Replace("{aid}", args["aid"], StringComparison.Ordinal)
                .Replace("{radius}", args["radius"], StringComparison.Ordinal),
            "RaidwideCast" => Template("componentRaidwideCast")
                .Replace("{aid}", args["aid"], StringComparison.Ordinal),
            "SingleTargetCast" => Template("componentSingleTargetCast")
                .Replace("{aid}", args["aid"], StringComparison.Ordinal),
            "SpreadFromCastTargets" => Template("componentSpreadFromCastTargets")
                .Replace("{aid}", args["aid"], StringComparison.Ordinal)
                .Replace("{radius}", args["radius"], StringComparison.Ordinal),
            "StackWithCastTargets" => Template("componentStackWithCastTargets")
                .Replace("{aid}", args["aid"], StringComparison.Ordinal)
                .Replace("{radius}", args["radius"], StringComparison.Ordinal),
            "KnockbackFromCastTarget" => Template("componentKnockbackFromCastTarget")
                .Replace("{aid}", args["aid"], StringComparison.Ordinal)
                .Replace("{distance}", args["distance"], StringComparison.Ordinal),
            "CastGaze" => Template("componentCastGaze")
                .Replace("{aid}", args["aid"], StringComparison.Ordinal),
            _ => throw new NotSupportedException("Unknown component kind '" + kind + "'."),
        };
    }

    public string IdCast(string enumName, string member)
        => $"{enumName}.{member}";

    private string Template(string key)
        => Profile.Templates.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Flavor template '{key}' is missing.");

    private static string FormatVertices(ArenaModel arena)
    {
        if (arena.Vertices.IsDefaultOrEmpty)
            return "";
        return string.Join(", ", arena.Vertices.Select(v =>
            "new(" + ShapeCSharp.Num(v.X) + ", " + ShapeCSharp.Num(v.Z) + ")"));
    }
}
