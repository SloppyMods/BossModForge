using System.Text.Json;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;

namespace BossModForge.Tests.Fixtures;

public static class OverrideFixtures
{
    public static Overrides Shape(uint aid, Shape? shape)
        => Overrides.Empty.With(OverridesApplier.AbilityKey(aid, "shape"), OverridesApplier.ToElement(shape));

    public static Overrides Class(uint aid, MechanicClass cls)
        => Overrides.Empty.With(OverridesApplier.AbilityKey(aid, "class"), OverridesApplier.ToElement(cls.ToString()));

    public static Overrides EnumName(uint aid, string name)
        => Overrides.Empty.With(OverridesApplier.AbilityKey(aid, "enumName"), OverridesApplier.ToElement(name));

    public static Overrides Ignore(uint aid)
        => Overrides.Empty.With(OverridesApplier.AbilityKey(aid, "ignore"), OverridesApplier.ToElement(true));

    public static Overrides ParticipantEnumName(uint oid, string name)
        => Overrides.Empty.With(OverridesApplier.ParticipantKey(oid, "enumName"), OverridesApplier.ToElement(name));

    public static Overrides Of(params (string Key, JsonElement Value)[] pairs)
    {
        var result = Overrides.Empty;
        foreach (var (key, value) in pairs)
            result = result.With(key, value);
        return result;
    }
}
