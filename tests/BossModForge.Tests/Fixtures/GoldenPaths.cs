using Xunit;

namespace BossModForge.Tests.Fixtures;

public static class GoldenPaths
{
    public static string Find(string fileName)
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? dir = new(Path.GetFullPath(start));
            while (dir is not null)
            {
                var nested = Path.Combine(dir.FullName, "tests", "BossModForge.Tests", "Golden", fileName);
                if (File.Exists(nested))
                    return nested;
                var csproj = Path.Combine(dir.FullName, "BossModForge.Tests.csproj");
                var direct = Path.Combine(dir.FullName, "Golden", fileName);
                if (File.Exists(csproj))
                    return direct;
                dir = dir.Parent;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "Golden", fileName);
    }

    public static void AssertEqualOrUpdate(string fileName, string actual)
    {
        actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal);
        var path = Find(fileName);
        var update = Environment.GetEnvironmentVariable("FORGE_UPDATE_GOLDEN") == "1" || !File.Exists(path);
        if (update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        var expected = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }
}
