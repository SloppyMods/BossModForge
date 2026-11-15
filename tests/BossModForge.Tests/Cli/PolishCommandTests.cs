using BossModForge.Cli.Commands;
using BossModForge.Core.Model;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Cli;

public sealed class PolishCommandTests
{
    [Fact]
    public void Polish_FileProvider_AcceptedAndRejected_Exit0_WithValidOutJson()
    {
        var emPath = WriteEm();
        var patchesPath = WritePatches(
            "[{\"op\":\"replace\",\"path\":\"/abilities/40001/enumName\",\"value\":\"Big Slash\"}," +
            "{\"op\":\"replace\",\"path\":\"/abilities/40001/shape\",\"value\":{\"kind\":\"circle\"}}]");
        var outPath = TempPath("out");
        try
        {
            var stdout = new StringWriter();
            var err = new StringWriter();
            var exit = PolishCommand.Run(emPath, "file", patchesPath, null, null, null, outPath, stdout, err);
            Assert.Equal(0, exit);

            var json = File.ReadAllText(outPath);
            Assert.Empty(EncounterModelJson.Validate(json));
            var em = EncounterModelJson.Deserialize(json);
            Assert.Equal("BigSlash", em.Enums.Aid[40001]);
            Assert.Equal("BigSlash", Assert.Single(em.Abilities).EnumName);

            var output = stdout.ToString();
            Assert.Contains("accepted", output, StringComparison.Ordinal);
            Assert.Contains("rejected", output, StringComparison.Ordinal);
            Assert.Contains("/abilities/40001/shape", output, StringComparison.Ordinal);
            Assert.Contains("allowlist", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(emPath);
            File.Delete(patchesPath);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Polish_HttpProvider_WithoutEndpoint_Exit4()
    {
        var emPath = WriteEm();
        var outPath = TempPath("out");
        try
        {
            var err = new StringWriter();
            var exit = PolishCommand.Run(emPath, "http", null, null, null, null, outPath, new StringWriter(), err);
            Assert.Equal(4, exit);
            Assert.Contains("--endpoint", err.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(outPath));
        }
        finally
        {
            File.Delete(emPath);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Polish_BadPatchesFile_Exit4()
    {
        var emPath = WriteEm();
        var patchesPath = WritePatches("{ not an array }");
        var outPath = TempPath("out");
        try
        {
            var err = new StringWriter();
            var exit = PolishCommand.Run(emPath, "file", patchesPath, null, null, null, outPath, new StringWriter(), err);
            Assert.Equal(4, exit);
            Assert.Contains("error:", err.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(outPath));
        }
        finally
        {
            File.Delete(emPath);
            File.Delete(patchesPath);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Polish_FileProvider_WithoutPatches_Exit4()
    {
        var emPath = WriteEm();
        var outPath = TempPath("out");
        try
        {
            var err = new StringWriter();
            var exit = PolishCommand.Run(emPath, "file", null, null, null, null, outPath, new StringWriter(), err);
            Assert.Equal(4, exit);
            Assert.Contains("--patches", err.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(emPath);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Polish_UnknownProvider_Exit4()
    {
        var emPath = WriteEm();
        var outPath = TempPath("out");
        try
        {
            var err = new StringWriter();
            var exit = PolishCommand.Run(emPath, "carrier-pigeon", null, null, null, null, outPath, new StringWriter(), err);
            Assert.Equal(4, exit);
            Assert.Contains("--provider", err.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(emPath);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Polish_MissingEmFile_Exit4()
    {
        var outPath = TempPath("out");
        try
        {
            var err = new StringWriter();
            var exit = PolishCommand.Run("/nonexistent/forge-missing.json", "file", null, null, null, null, outPath, new StringWriter(), err);
            Assert.Equal(4, exit);
            Assert.Contains("error:", err.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    private static string WriteEm()
    {
        var path = TempPath("em");
        File.WriteAllText(path, EncounterModelJson.Serialize(HandBuiltEncounter.Create()));
        return path;
    }

    private static string WritePatches(string json)
    {
        var path = TempPath("patches");
        File.WriteAllText(path, json);
        return path;
    }

    private static string TempPath(string stem)
        => Path.Combine(Path.GetTempPath(), $"forge-polish-{stem}-{Guid.NewGuid():N}.json");
}
