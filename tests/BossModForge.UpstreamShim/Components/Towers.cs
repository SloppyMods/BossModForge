namespace BossMod.Components;

public class GenericTowers(BossModule module, Enum? aid = default) : CastCounter(module, aid);

public class CastTowers(BossModule module, Enum aid, float radius, int minSoakers = 1, int maxSoakers = 1) : GenericTowers(module, aid);
