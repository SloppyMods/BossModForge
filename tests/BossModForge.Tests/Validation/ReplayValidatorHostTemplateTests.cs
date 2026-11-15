using BossModForge.Cli.Commands;
using Xunit;

namespace BossModForge.Tests.Validation;

public class ReplayValidatorHostTemplateTests
{
    [Fact]
    public void HostTemplate_InstantiatesModuleWithEmittedCtorShape()
    {
        const string resourceName = "BossModForge.Cli.ReplayValidator.Program.cs";
        var asm = typeof(ConformCommand).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"embedded resource '{resourceName}' not found");
        using var reader = new StreamReader(stream);
        var host = reader.ReadToEnd();

        // Emitted modules have a 2-arg ctor (WorldState ws, Actor primary) per the
        // moduleDecl flavor template; center/bounds are embedded in the module itself.
        // The host must match that shape or the first real --bossmod run throws
        // MissingMethodException before validating anything.
        Assert.Contains("Activator.CreateInstance(moduleType, ws, primary)", host, StringComparison.Ordinal);
        Assert.DoesNotContain("ArenaBoundsCircle", host, StringComparison.Ordinal);
    }
}
