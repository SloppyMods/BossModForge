namespace BossMod.Components;

public class GenericAOEs(BossModule module, Enum? aid = default, string warningText = "GTFO from aoe!") : CastCounter(module, aid);

public class SelfTargetedAOEs(BossModule module, Enum aid, AOEShape shape, int maxCasts = int.MaxValue) : GenericAOEs(module, aid);

public class LocationTargetedAOEs(BossModule module, Enum aid, float radius, string warningText = "GTFO from puddle!", int maxCasts = int.MaxValue)
    : GenericAOEs(module, aid, warningText);

public class StandardAOEs(BossModule module, Enum aid, AOEShape shape, int maxCasts = int.MaxValue) : SelfTargetedAOEs(module, aid, shape, maxCasts);

public class GroupedAOEs(BossModule module, Enum[] aids, AOEShape shape, int maxCasts = int.MaxValue) : GenericAOEs(module);
