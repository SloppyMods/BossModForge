using System.Reflection;
using Xunit;

namespace BossModForge.Tests.Emit;

public sealed class GoldenCompileSmokeTests
{
    [Fact]
    public void GoldenCompileAssembly_ExistsBesideTestBinary()
    {
        Assert.True(File.Exists(AssemblyPath), AssemblyPath);
    }

    [Theory]
    [InlineData("SyntheticDungeon")]
    [InlineData("SyntheticPhases")]
    [InlineData("SyntheticRotation")]
    [InlineData("SyntheticShapes")]
    public void GoldenCompileAssembly_ContainsBossModule(string goldenName)
    {
        Assert.True(File.Exists(AssemblyPath), AssemblyPath);
        var asm = Assembly.LoadFrom(AssemblyPath);
        var type = asm.GetType("BossMod.Forge." + goldenName + "." + goldenName);
        Assert.NotNull(type);
        Assert.True(DerivesFromBossModule(type));
    }

    private static string AssemblyPath => Path.Combine(AppContext.BaseDirectory, "BossModForge.GoldenCompile.dll");

    private static bool DerivesFromBossModule(Type type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (b.FullName == "BossMod.BossModule")
                return true;
        }

        return false;
    }
}
