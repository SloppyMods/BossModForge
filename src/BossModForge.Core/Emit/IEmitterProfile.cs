using BossModForge.Core.Model;

namespace BossModForge.Core.Emit;

public sealed record ModuleInfoArgs(string Maturity, string GroupType, uint GroupId, uint NameId, string? Contributors, uint? PrimaryActorOid);

public interface IEmitterProfile
{
    Flavor Flavor { get; }
    FlavorProfile Profile { get; }
    string ModuleInfoAttribute(ModuleInfoArgs args);
    string ModuleDeclaration(string moduleName, string center, string bounds);
    string ArenaBounds(ArenaModel arena);
    string StatesClass(StatesPlan plan);
    string EnumDeclaration(string enumName, string underlying, IEnumerable<(string Name, uint Value, string? Comment)> members);
    string ComponentCtor(string kind, IReadOnlyDictionary<string, string> args);
    string IdCast(string enumName, string member);
}
