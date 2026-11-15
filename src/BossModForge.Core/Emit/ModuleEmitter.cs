using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;

namespace BossModForge.Core.Emit;

public sealed record EmitOptions(Flavor Flavor, string? Namespace = null, string Maturity = "WIP", string? Contributors = null);

public sealed record EmittedFile(string RelativePath, string Content);

public sealed record EmitResult(ImmutableArray<EmittedFile> Files, string ForgeMd, ImmutableArray<ReviewItem> Review);

public static class ModuleEmitter
{
    public const uint HelperOid = 0x233C;

    public static EmitResult Emit(EncounterModel em, IEmitterProfile profile, EmitOptions options)
    {
        var ns = options.Namespace ?? em.Encounter.Namespace;
        var moduleName = em.Encounter.ModuleName;
        var extra = ImmutableArray.CreateBuilder<ReviewItem>();
        extra.Add(new ReviewItem(
            "E1",
            ReviewSeverity.Medium,
            "module",
            "Set Expansion/Category",
            ["Set Expansion and Category on ModuleInfo once enum member lists are verified."]));
        if (!profile.Profile.Verified)
        {
            extra.Add(new ReviewItem(
                "E" + (extra.Count + 1).ToString(CultureInfo.InvariantCulture),
                ReviewSeverity.High,
                "flavor",
                "Flavor API unverified — output uses template shapes",
                []));
        }

        if (em.Arena.Kind == "Custom")
        {
            extra.Add(new ReviewItem(
                "E" + (extra.Count + 1).ToString(CultureInfo.InvariantCulture),
                ReviewSeverity.High,
                "arena",
                "Custom arena — ArenaBoundsCustom unverified",
                []));
        }

        var review = em.Review.AddRange(extra);

        var oidMembers = OidMembers(em);
        var aidMembers = em.Enums.Aid.Select(kv => (kv.Value, kv.Key, (string?)null));
        var sidMembers = em.Enums.Sid.Select(kv => (kv.Value, kv.Key, (string?)null)).ToArray();
        var iconMembers = em.Enums.IconId.Select(kv => (kv.Value, kv.Key, (string?)null)).ToArray();
        var tetherMembers = em.Enums.TetherId.Select(kv => (kv.Value, kv.Key, (string?)null)).ToArray();

        var sb = new StringBuilder();
        sb.Append("namespace ").Append(ns).AppendLine(";");
        sb.AppendLine();
        sb.AppendLine(profile.EnumDeclaration("OID", "uint", oidMembers));
        sb.AppendLine();
        sb.AppendLine(profile.EnumDeclaration("AID", "uint", aidMembers));
        if (sidMembers.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(profile.EnumDeclaration("SID", "uint", sidMembers));
        }

        if (iconMembers.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(profile.EnumDeclaration("IconID", "uint", iconMembers));
        }

        if (tetherMembers.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(profile.EnumDeclaration("TetherID", "uint", tetherMembers));
        }

        var components = BuildComponents(em, profile);
        if (components.Count > 0)
        {
            sb.AppendLine();
            foreach (var line in components.Select(c => c.Line))
                sb.AppendLine(line);
        }

        sb.AppendLine();
        var aidEnumNames = em.Enums.Aid.ToDictionary(kv => kv.Key, kv => kv.Value);
        sb.AppendLine(profile.StatesClass(StatesPlanner.Plan(em, aidEnumNames)));
        sb.AppendLine();
        sb.AppendLine("// TODO(forge:E1) Set Expansion/Category");
        var info = new ModuleInfoArgs(
            options.Maturity,
            "CFC",
            em.Encounter.CfcId,
            em.Encounter.BossNameId,
            options.Contributors,
            em.Encounter.PrimaryOid);
        sb.AppendLine(profile.ModuleInfoAttribute(info));
        var center = string.Create(CultureInfo.InvariantCulture, $"{em.Arena.CenterX:F0}, {em.Arena.CenterZ:F0}");
        var bounds = profile.ArenaBounds(em.Arena);
        sb.AppendLine(profile.ModuleDeclaration(moduleName, center, bounds));
        if (sb.Length == 0 || sb[^1] != '\n')
            sb.Append('\n');

        var content = sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        var files = ImmutableArray.Create(new EmittedFile(moduleName + ".cs", content));
        var forgeMd = ForgeMdWriter.Write(em, profile, review);
        return new EmitResult(files, forgeMd, review);
    }

    private static List<(string Name, string Line)> BuildComponents(EncounterModel em, IEmitterProfile profile)
    {
        var planned = ComponentPlanner.Plan(em);
        // One AbilityModel per (SourceOid, ActionId); an aid shared by several OIDs resolves to its first entry.
        var byAid = em.Abilities.GroupBy(a => a.ActionId).ToDictionary(g => g.Key, g => g.First());
        var result = new List<(string Name, string Line)>();
        var classTemplate = profile.Profile.Templates["componentClass"];
        foreach (var unit in planned)
        {
            var args = new Dictionary<string, string>();
            switch (unit.Kind)
            {
                case "RaidwideCast":
                case "SingleTargetCast":
                case "CastGaze":
                    args["aid"] = profile.IdCast("AID", byAid[unit.Aids[0]].EnumName);
                    break;
                case "SimpleAOEs":
                    args["aid"] = profile.IdCast("AID", byAid[unit.Aids[0]].EnumName);
                    args["shape"] = ShapeCSharp.Render(byAid[unit.Aids[0]].Shape!, profile.Profile);
                    break;
                case "LocationTargetedAOEs":
                    args["aid"] = profile.IdCast("AID", byAid[unit.Aids[0]].EnumName);
                    args["radius"] = ShapeCSharp.Num(byAid[unit.Aids[0]].Shape!.Radius);
                    break;
                case "SimpleAOEGroups":
                    args["aids"] = string.Join(", ", unit.Aids.Select(id => profile.IdCast("AID", byAid[id].EnumName)));
                    args["shape"] = ShapeCSharp.Render(byAid[unit.Aids[0]].Shape!, profile.Profile);
                    break;
                case "SpreadFromCastTargets":
                case "StackWithCastTargets":
                    args["aid"] = profile.IdCast("AID", byAid[unit.Aids[0]].EnumName);
                    args["radius"] = MechanicParam(byAid[unit.Aids[0]], "radius", "6");
                    break;
                case "KnockbackFromCastTarget":
                    args["aid"] = profile.IdCast("AID", byAid[unit.Aids[0]].EnumName);
                    args["distance"] = MechanicParam(byAid[unit.Aids[0]], "distance", "10");
                    break;
            }

            var ctor = profile.ComponentCtor(unit.Kind, args);
            var line = classTemplate
                .Replace("{name}", unit.ClassName, StringComparison.Ordinal)
                .Replace("{ctor}", ctor, StringComparison.Ordinal);
            result.Add((unit.ClassName, line));
        }

        foreach (var ability in em.Abilities.OrderBy(a => a.ActionId))
        {
            if (ability.Class is MechanicClass.SharedTankbuster or MechanicClass.Attract or MechanicClass.Towers
                or MechanicClass.Chasing or MechanicClass.Instant or MechanicClass.Interrupt)
            {
                result.Add(("", "// TODO(forge) " + ability.Class + ": " + ability.EnumName + " — no standard component"));
            }
        }

        return result;
    }

    private static string MechanicParam(AbilityModel ability, string key, string fallback)
    {
        foreach (var ev in ability.Evidence.IsDefault ? [] : ability.Evidence)
        {
            if (ev.Kind != MechanicClassifier.ParamsKind)
                continue;
            foreach (var part in ev.Summary.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0)
                    continue;
                if (part[..eq] == key)
                    return part[(eq + 1)..];
            }
        }

        if (key == "radius" && ability.Shape is { Kind: ShapeKind.Circle } circle)
            return ShapeCSharp.Num(circle.Radius);
        return fallback;
    }

    public static IEmitterProfile ProfileFor(Flavor flavor, string flavorsDirectory)
    {
        var file = flavor == Flavor.Bmr ? "bmr.json" : "vbm.json";
        var path = Path.Combine(flavorsDirectory, file);
        var loaded = FlavorProfile.Load(path);
        return flavor == Flavor.Bmr ? new BmrProfile(loaded) : new VbmProfile(loaded);
    }

    public static string ResolveFlavorsDirectory(string? specified = null)
    {
        if (!string.IsNullOrWhiteSpace(specified) && Directory.Exists(specified))
            return specified;

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? dir = new(Path.GetFullPath(start));
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "flavors");
                if (File.Exists(Path.Combine(candidate, "vbm.json")))
                    return candidate;
                dir = dir.Parent;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "flavors");
    }

    private static List<(string Name, uint Value, string? Comment)> OidMembers(EncounterModel em)
    {
        var byOid = em.Participants.ToDictionary(p => p.Oid);
        var ordered = new List<KeyValuePair<uint, string>>();
        foreach (var kv in em.Enums.Oid)
        {
            if (kv.Value == "Boss")
                ordered.Insert(0, kv);
            else
                ordered.Add(kv);
        }

        var helperIndex = ordered.FindIndex(kv => kv.Value == "Helper" || kv.Key == HelperOid);
        if (helperIndex > 1)
        {
            var helper = ordered[helperIndex];
            ordered.RemoveAt(helperIndex);
            ordered.Insert(1, helper);
        }
        else if (helperIndex == 0 && ordered.Count > 1 && ordered[1].Value == "Boss")
        {
            (ordered[0], ordered[1]) = (ordered[1], ordered[0]);
        }

        var result = new List<(string, uint, string?)>();
        foreach (var (oid, name) in ordered)
        {
            byOid.TryGetValue(oid, out var participant);
            var radius = participant?.HitboxRadius ?? 0.5f;
            var count = participant?.InstanceCount ?? 0;
            var comment = string.Create(CultureInfo.InvariantCulture, $"R{radius:F3}, x{count}");
            if (name == "Helper")
                comment += ", Helper type";
            result.Add((name, oid, comment));
        }

        return result;
    }
}
