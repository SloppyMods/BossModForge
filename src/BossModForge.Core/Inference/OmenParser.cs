using System.Globalization;
using System.Text.RegularExpressions;

namespace BossModForge.Core.Inference;

public static partial class OmenParser
{
    public static bool TryParseFan(string? omenPath, out float halfAngleDeg)
    {
        halfAngleDeg = 0;
        if (string.IsNullOrEmpty(omenPath))
            return false;
        var match = FanRegex().Match(omenPath);
        if (!match.Success)
            return false;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var total) || total == 0)
            return false;
        halfAngleDeg = total / 2f;
        return true;
    }

    public static bool TryParseDonut(string? omenPath, out float innerOverOuter)
    {
        innerOverOuter = 0;
        _ = omenPath;
        return false;
    }

    [GeneratedRegex(@"fan(\d{3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FanRegex();
}
