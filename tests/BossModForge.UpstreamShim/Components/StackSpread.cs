namespace BossMod.Components;

public class CastStackSpread(BossModule module, Enum? stackAID, Enum? spreadAID, float stackRadius, float spreadRadius, int minStackSize = 2, int maxStackSize = int.MaxValue, bool alwaysShowSpreads = false)
    : CastCounter(module, stackAID ?? spreadAID);

public class SpreadFromCastTargets(BossModule module, Enum aid, float radius, bool drawAllSpreads = true)
    : CastStackSpread(module, default, aid, 0, radius, alwaysShowSpreads: drawAllSpreads);

public class StackWithCastTargets(BossModule module, Enum aid, float radius, int minStackSize = 2, int maxStackSize = int.MaxValue)
    : CastStackSpread(module, aid, default, radius, 0, minStackSize, maxStackSize);
