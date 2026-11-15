using System.Text.Json;
using BossModForge.Core.Polish;

namespace BossModForge.Cli.Polish;

public static class FilePatchProvider
{
    public static IReadOnlyList<PolishPatch> Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return PolishPatchParser.Parse(doc.RootElement);
    }
}
