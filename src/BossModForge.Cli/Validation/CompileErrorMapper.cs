using System.Collections.Immutable;
using System.Text.RegularExpressions;
using BossModForge.Core.Model;

namespace BossModForge.Cli.Validation;

public sealed record CompileErrorItem(string File, int Line, string Code, string Message, string? EmTarget);

public static class CompileErrorMapper
{
    // /path/File.cs(26,42): error CS0103: message [optional project suffix]
    private static readonly Regex ErrorLine = new(
        @"^(?<file>.+)\((?<line>\d+),(?<col>\d+)\):\s+error\s+(?<code>CS\d+):\s+(?<msg>.+?)(?:\s+\[[^\]]+\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NameToken = new(
        @"The name '(?<tok>[^']+)'", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DefinitionToken = new(
        @"does not contain a definition for '(?<tok>[^']+)'", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CtorToken = new(
        @"'(?<type>[^']+)' does not contain a constructor", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const int MaxDistance = 3;

    public static ImmutableArray<CompileErrorItem> Map(string buildOutput, EncounterModel em)
    {
        var items = ImmutableArray.CreateBuilder<CompileErrorItem>();
        foreach (var raw in buildOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ErrorLine.Match(raw);
            if (!match.Success)
                continue;

            var file = match.Groups["file"].Value;
            var line = int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var code = match.Groups["code"].Value;
            var message = match.Groups["msg"].Value;
            var target = IsScratchFile(file) ? MapTarget(code, message, em) : null;
            items.Add(new CompileErrorItem(file, line, code, message, target));
        }

        return items.ToImmutable();
    }

    private static bool IsScratchFile(string file)
        => file.Contains("ForgeScratch", StringComparison.Ordinal);

    private static string? MapTarget(string code, string message, EncounterModel em)
    {
        if (CtorToken.Match(message).Success || code == "CS1729")
            return "flavor-profile";

        var token = NameToken.Match(message) is { Success: true } byName
            ? byName.Groups["tok"].Value
            : DefinitionToken.Match(message) is { Success: true } byDefinition
                ? byDefinition.Groups["tok"].Value
                : null;
        if (token is null)
            return null;

        var dot = token.IndexOf('.');
        var prefix = dot >= 0 ? token[..dot] : null;
        var name = dot >= 0 ? token[(dot + 1)..] : token;
        if (name.Length == 0)
            return null;

        return prefix?.ToUpperInvariant() switch
        {
            "OID" => MatchEnum(name, em.Enums.Oid, "participant"),
            "SID" or "TETHERID" or "ICONID" => null,
            _ => MatchEnum(name, em.Enums.Aid, "ability") ?? MatchEnum(name, em.Enums.Oid, "participant"),
        };
    }

    // Candidates are ordinal-sorted by name; the first match wins (deterministic).
    private static string? MatchEnum(
        string token,
        IReadOnlyDictionary<uint, string> names,
        string targetKind)
    {
        foreach (var (id, enumName) in names.OrderBy(kv => kv.Value, StringComparer.Ordinal))
        {
            if (IsMatch(enumName, token))
                return $"{targetKind}:{id.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        return null;
    }

    private static bool IsMatch(string enumName, string token)
    {
        if (enumName.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            return token.Length >= 2;
        if (token.StartsWith(enumName, StringComparison.OrdinalIgnoreCase))
            return enumName.Length >= 2;
        return Levenshtein(enumName, token) <= MaxDistance;
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
            return b.Length;
        if (b.Length == 0)
            return a.Length;

        var prev = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
            }

            prev = cur;
        }

        return prev[b.Length];
    }
}
