using System.Text.RegularExpressions;

namespace BossModForge.Core.Replay;

/// <summary>Reads the zone name out of BossMod recording file names (<c>Zone_JOB100_yyyy_MM_dd_HH_mm_ss.log</c>).</summary>
public static partial class RecordingNames
{
    [GeneratedRegex(@"^(?<zone>.+?)(?:_[A-Za-z]{3}\d+(?:_.*?)?)?_(?:19|20)\d\d_\d\d_\d\d_\d\d_\d\d_\d\d$")]
    private static partial Regex NamePattern();

    /// <summary>Zone part of a recording file name, or null when the name does not match.</summary>
    public static string? ZoneOf(string fileNameOrPath)
    {
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath);
        var m = NamePattern().Match(name);
        return m.Success ? m.Groups["zone"].Value : null;
    }

    /// <summary>Lower-cased letters and digits only, so "The Clyteum" equals "TheClyteum".</summary>
    public static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>How many of the given recording files belong to the duty with this display name.</summary>
    public static int CountFor(IEnumerable<string> recordingPaths, string dutyName)
    {
        var want = Normalize(dutyName);
        if (want.Length == 0)
            return 0;
        return recordingPaths.Count(p => ZoneOf(p) is { } z && Normalize(z) == want);
    }
}
