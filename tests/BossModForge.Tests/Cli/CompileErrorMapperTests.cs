using BossModForge.Cli.Validation;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Cli;

public sealed class CompileErrorMapperTests
{
    private const string ScratchFile = "/checkout/BossMod/Modules/ForgeScratch/TestBoss/TestBossStates.cs";

    [Fact]
    public void UnknownEnumMember_MapsToAbility()
    {
        const string output = ScratchFile + "(26,42): error CS0103: The name 'AID.HeavySlsh' does not exist in the current context [/checkout/BossMod/BossMod.csproj]\n";
        var em = HandBuiltEncounter.Create();

        var errors = CompileErrorMapper.Map(output, em);

        var item = Assert.Single(errors);
        Assert.Equal(ScratchFile, item.File);
        Assert.Equal(26, item.Line);
        Assert.Equal("CS0103", item.Code);
        Assert.Equal("ability:40001", item.EmTarget);
    }

    [Fact]
    public void UnknownEnumMember_ViaDefinitionFor_MapsToAbility()
    {
        const string output = ScratchFile + "(12,30): error CS0117: 'AID' does not contain a definition for 'HeavySlsh'\n";
        var em = HandBuiltEncounter.Create();

        var item = Assert.Single(CompileErrorMapper.Map(output, em));

        Assert.Equal("ability:40001", item.EmTarget);
    }

    [Fact]
    public void ComponentCtorMismatch_MapsToFlavorProfile()
    {
        const string output = ScratchFile + "(40,20): error CS1729: 'StandardAOEs' does not contain a constructor that takes 3 arguments\n";
        var em = HandBuiltEncounter.Create();

        var item = Assert.Single(CompileErrorMapper.Map(output, em));

        Assert.Equal("flavor-profile", item.EmTarget);
    }

    [Fact]
    public void UnrelatedFile_MapsToNull()
    {
        const string output = "/checkout/BossMod/Components/GenericAOEs.cs(96,13): error CS0103: The name 'nope' does not exist in the current context\n";
        var em = HandBuiltEncounter.Create();

        var item = Assert.Single(CompileErrorMapper.Map(output, em));

        Assert.Null(item.EmTarget);
    }

    [Fact]
    public void UnknownToken_NoCloseEnum_MapsToNull()
    {
        const string output = ScratchFile + "(26,42): error CS0103: The name 'AID.Zzqxwv' does not exist in the current context\n";
        var em = HandBuiltEncounter.Create();

        var item = Assert.Single(CompileErrorMapper.Map(output, em));

        Assert.Null(item.EmTarget);
    }

    [Fact]
    public void NonErrorLines_AreIgnored()
    {
        const string output = "  Determining projects to restore...\n" + ScratchFile + "(1,1): warning CS8618: Non-nullable field is uninitialized\n";
        var em = HandBuiltEncounter.Create();

        Assert.Empty(CompileErrorMapper.Map(output, em));
    }

    [Fact]
    public void Map_IsDeterministic_SameOutputSameResult()
    {
        const string output = ScratchFile + "(26,42): error CS0103: The name 'AID.HeavySlsh' does not exist in the current context\n"
            + ScratchFile + "(40,20): error CS1729: 'StandardAOEs' does not contain a constructor that takes 3 arguments\n";
        var em = HandBuiltEncounter.Create();

        var first = CompileErrorMapper.Map(output, em);
        var second = CompileErrorMapper.Map(output, em);

        Assert.Equal(first.ToArray(), second.ToArray());
    }
}
