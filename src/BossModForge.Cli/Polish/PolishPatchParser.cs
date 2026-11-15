using System.Text.Json;
using BossModForge.Core.Polish;

namespace BossModForge.Cli.Polish;

public static class PolishPatchParser
{
    public static IReadOnlyList<PolishPatch> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("patches must be a JSON array of { op, path, value } objects.");

        var patches = new List<PolishPatch>();
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("op", out var op) || op.ValueKind != JsonValueKind.String
                || !element.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("each patch must be an object with string 'op' and 'path'.");
            }

            var value = element.TryGetProperty("value", out var v) ? v.Clone() : default;
            patches.Add(new PolishPatch(op.GetString() ?? "", path.GetString() ?? "", value));
        }

        return patches;
    }
}
