using System.Collections;
using System.Reflection;
using System.Text;

namespace BossModForge.Cli.Conformance;

// Loads a real BossMod.dll (built from the pinned checkout) and counts replay ops through
// BossMod's own parser, keyed by the same four-char tags Forge's parser uses. Only ever
// called from the CLI; Core stays free of BossMod (AGENTS.md).
public static class BossModReflection
{
    private const BindingFlags ParserFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    // Op simple names (leading "Op" stripped, e.g. ActorState.OpCastInfo -> "CastInfo")
    // transcribed from docs/UPSTREAM.md §5. A trailing "±" marks tags that share one op type
    // and are disambiguated at runtime from the op's members (bool flag or null payload).
    private static readonly Dictionary<string, string> OpNameToTag = new(StringComparer.Ordinal)
    {
        ["FrameStart"] = "FRAM",
        ["UserMarker"] = "UMRK",
        ["RSVData"] = "RSV ",
        ["ZoneChange"] = "ZONE",
        ["DirectorUpdate"] = "DIRU",
        ["EnvControl"] = "ENVC",
        ["SystemLog"] = "SLOG",
        ["WaymarkChange"] = "WAY±",
        ["Create"] = "ACT+",
        ["Destroy"] = "ACT-",
        ["Rename"] = "NAME",
        ["ClassChange"] = "CLSR",
        ["Move"] = "MOVE",
        ["SizeChange"] = "ACSZ",
        ["HPMP"] = "HP  ",
        ["Targetable"] = "ATG±",
        ["Ally"] = "ALLY",
        ["Dead"] = "DIE±",
        ["Combat"] = "COM±",
        ["AggroPlayer"] = "NENP",
        ["ModelState"] = "MDLS",
        ["EventState"] = "EVTS",
        ["Target"] = "TARG",
        ["Mount"] = "MNTD",
        ["Foray"] = "FORA",
        ["Tether"] = "TETH±",
        ["CastStart"] = "CST+",
        ["CastEnd"] = "CST-",
        ["CastInfo"] = "CST±",
        ["CastEvent"] = "CST!",
        ["EffectResult"] = "ER  ",
        ["Status"] = "STA±",
        ["StatusLose"] = "STA-",
        ["IncomingEffect"] = "AIE±",
        ["Icon"] = "ICON",
        ["EventObjectStateChange"] = "ESTA",
        ["EventObjectAnimation"] = "EANM",
        ["PlayActionTimelineEvent"] = "PATE",
        ["EventNpcYell"] = "NYEL",
        ["EventOpenTreasure"] = "OPNT",
        ["Modify"] = "PAR ",
        ["Leave"] = "PAR-",
        ["LimitBreak"] = "LB  ",
        ["ActionRequest"] = "CLAR",
        ["ActionReject"] = "CLRJ",
        ["Countdown"] = "CDN±",
        ["AnimationLock"] = "CLAL",
        ["Combo"] = "CLCB",
        ["PlayerStats"] = "CLST",
        ["Movespeed"] = "CLMV",
        ["Cooldown"] = "CLCD",
        ["DutyActions"] = "CLDA",
        ["BozjaHolster"] = "CLBH",
        ["BlueMageSpells"] = "CBLU",
        ["ClassJobLevels"] = "CLVL",
        ["ActiveFate"] = "CLAF",
        ["ActivePet"] = "CPET",
        ["FocusTarget"] = "CLFT",
        ["ForcedMovementDirection"] = "CLFD",
        ["ContentKVData"] = "CLKV",
        ["FateInfo"] = "FATE",
        ["Progress"] = "DDPG",
        ["Map"] = "DDMP",
        ["Party"] = "DDPT",
        ["Pomanders"] = "DDIT",
        ["Chests"] = "DDCT",
        ["Magicite"] = "DDMG",
        ["IDScramble"] = "IPCI",
        ["ServerIPC"] = "IPCS",
    };

    public static IReadOnlyDictionary<string, int> CountOps(string bossModDllPath, string logPath)
    {
        var (assembly, replayType, tagFor) = LoadAndResolve(bossModDllPath);

        object replay;
        try
        {
            replay = Activator.CreateInstance(replayType, logPath)
                ?? throw new InvalidOperationException($"could not construct {replayType.FullName}.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"BossMod's {replayType.Name} could not parse '{logPath}': {ex.GetType().Name}: {ex.Message}", ex);
        }

        var opsProperty = replayType.GetProperty("Ops", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"{replayType.FullName} exposes no public Ops property; run with --selftest against the pinned BossMod build.");
        if (opsProperty.GetValue(replay) is not IEnumerable ops)
            throw new InvalidOperationException($"{replayType.FullName}.Ops is not enumerable.");

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var op in ops)
        {
            if (op is null)
                continue;
            var tag = ResolveTag(op, tagFor);
            counts[tag] = counts.GetValueOrDefault(tag) + 1;
        }
        return counts;
    }

    // Loads the DLL and reports the resolved op-type -> tag map, so the first real run is
    // one command. Returns the report text; throws InvalidOperationException like CountOps.
    public static string SelfTest(string bossModDllPath)
    {
        var (assembly, replayType, tagFor) = LoadAndResolve(bossModDllPath);
        var sb = new StringBuilder();
        sb.AppendLine($"assembly: {assembly.GetName().Name} ({assembly.GetName().Version})");
        sb.AppendLine($"replay type: {replayType.FullName}");
        sb.AppendLine("resolved op-type -> tag map ('±' = disambiguated from op members at count time):");
        foreach (var (opName, tag) in tagFor)
            sb.AppendLine($"  {opName} -> {tag}");
        return sb.ToString();
    }

    private static (Assembly Assembly, Type ReplayType, SortedDictionary<string, string> TagFor) LoadAndResolve(
        string bossModDllPath)
    {
        if (string.IsNullOrWhiteSpace(bossModDllPath) || !File.Exists(bossModDllPath))
            throw new InvalidOperationException(
                $"BossMod.dll not found at '{bossModDllPath}'. Build BossMod from the pinned checkout (docs/UPSTREAM.md §1) and pass --bossmod-dll.");

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(bossModDllPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"failed to load '{bossModDllPath}': {ex.Message}", ex);
        }

        var replayType = assembly.GetType("BossMod.Replay.Replay")
            ?? assembly.GetType("BossMod.Replay.ReplayParserLog")
            ?? throw new InvalidOperationException(
                $"'{bossModDllPath}' contains no BossMod.Replay.Replay/ReplayParserLog type; is this a BossMod build?");

        var tagFor = TryParserDispatchMap(replayType);
        foreach (var (opName, tag) in OpNameToTag)
            tagFor.TryAdd(opName, tag);
        return (assembly, replayType, tagFor);
    }

    // First resolution source: the parser's own fourCC -> handler map, discovered by reflection.
    // Handler method names (ParseActorCastStart, ...) map to the same key space as OpNameToTag
    // once the "Parse" prefix and the Actor/Party/Client/... category infixes are stripped.
    private static SortedDictionary<string, string> TryParserDispatchMap(Type replayType)
    {
        var resolved = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in SelfAndNested(replayType))
        {
            foreach (var field in type.GetFields(ParserFlags))
            {
                if (field.GetValue(null) is not IDictionary map)
                    continue;
                foreach (DictionaryEntry entry in map)
                {
                    if (entry.Key?.ToString() is not { Length: 4 } || entry.Value is not Delegate handler)
                        continue;
                    if (TryMapParseMethod(handler.Method.Name, out var opName, out var tag))
                        resolved.TryAdd(opName, tag);
                }
            }
        }
        return resolved;
    }

    private static bool TryMapParseMethod(string methodName, out string opName, out string tag)
    {
        opName = "";
        tag = "";
        if (!methodName.StartsWith("Parse", StringComparison.Ordinal) || methodName.Length <= "Parse".Length)
            return false;
        var stripped = methodName["Parse".Length..];
        foreach (var prefix in new[] { "Actor", "Party", "Client", "DeepDungeon", "Network" })
        {
            if (stripped.StartsWith(prefix, StringComparison.Ordinal))
            {
                stripped = stripped[prefix.Length..];
                break;
            }
        }
        if (!OpNameToTag.TryGetValue(stripped, out var mapped))
            return false;
        opName = stripped;
        tag = mapped;
        return true;
    }

    private static IEnumerable<Type> SelfAndNested(Type type)
    {
        yield return type;
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            yield return nested;
    }

    private static string ResolveTag(object op, IReadOnlyDictionary<string, string> tagFor)
    {
        var type = op.GetType();
        var name = type.Name;
        if (name.StartsWith("Op", StringComparison.Ordinal))
            name = name[2..];
        if (tagFor.TryGetValue(name, out var tag))
            return ResolveSign(op, tag);
        throw new InvalidOperationException(
            $"no op->tag mapping for BossMod op type '{type.FullName}'. " +
            "Run `forge conform --bossmod-dll <path> --selftest` to inspect the resolved map.");
    }

    private static string ResolveSign(object op, string tag)
    {
        if (!tag.EndsWith('±'))
            return tag;
        var baseTag = tag[..^1];
        if (!TryGetMember(op, out var value))
            throw new InvalidOperationException(
                $"op '{op.GetType().Name}' needs a bool/null member to pick {baseTag}+/- but none of Value/Active/Action/Status exists.");
        return value switch
        {
            bool b => baseTag + (b ? "+" : "-"),
            null => baseTag + "-",
            _ => baseTag + "+",
        };
    }

    private static bool TryGetMember(object op, out object? value)
    {
        var type = op.GetType();
        foreach (var memberName in new[] { "Value", "Active", "Action", "Status" })
        {
            var field = type.GetField(memberName);
            if (field is not null)
            {
                value = field.GetValue(op);
                return true;
            }
            var property = type.GetProperty(memberName);
            if (property is not null)
            {
                value = property.GetValue(op);
                return true;
            }
        }
        value = null;
        return false;
    }
}
