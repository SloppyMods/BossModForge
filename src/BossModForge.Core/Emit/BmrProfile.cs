using BossModForge.Core.Model;

namespace BossModForge.Core.Emit;

public sealed class BmrProfile : IEmitterProfile
{
    private readonly VbmProfile _inner;

    public BmrProfile(FlavorProfile profile)
    {
        Profile = profile;
        Flavor = Flavor.Bmr;
        _inner = new VbmProfile(profile);
    }

    public Flavor Flavor { get; }
    public FlavorProfile Profile { get; }

    public string ModuleInfoAttribute(ModuleInfoArgs args) => _inner.ModuleInfoAttribute(args);

    public string ModuleDeclaration(string moduleName, string center, string bounds)
        => _inner.ModuleDeclaration(moduleName, center, bounds);

    public string ArenaBounds(ArenaModel arena) => _inner.ArenaBounds(arena);

    public string StatesClass(StatesPlan plan) => _inner.StatesClass(plan);

    public string EnumDeclaration(string enumName, string underlying, IEnumerable<(string Name, uint Value, string? Comment)> members)
        => _inner.EnumDeclaration(enumName, underlying, members);

    public string ComponentCtor(string kind, IReadOnlyDictionary<string, string> args)
        => _inner.ComponentCtor(kind, args);

    public string IdCast(string enumName, string member) => "(uint)" + enumName + "." + member;
}
