using System.Collections.Immutable;

namespace BossModForge.Cli.Conformance;

public sealed record TagCountDiff(string Tag, int ForgeCount, int BossModCount);

public static class ConformComparer
{
    // Sorted union of both key sets, ordinal order; a diff is any key whose counts differ
    // (a tag present on only one side counts as 0 on the other).
    public static ImmutableArray<TagCountDiff> Compare(
        IReadOnlyDictionary<string, int> forgeCounts,
        IReadOnlyDictionary<string, int> bossModCounts)
    {
        var keys = forgeCounts.Keys
            .Union(bossModCounts.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var diffs = ImmutableArray.CreateBuilder<TagCountDiff>();
        foreach (var key in keys)
        {
            var forge = forgeCounts.GetValueOrDefault(key);
            var bossMod = bossModCounts.GetValueOrDefault(key);
            if (forge != bossMod)
                diffs.Add(new TagCountDiff(key, forge, bossMod));
        }
        return diffs.ToImmutable();
    }
}
