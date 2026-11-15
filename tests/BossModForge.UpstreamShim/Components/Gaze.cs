namespace BossMod.Components;

public class GenericGaze(BossModule module, Enum? aid = default, bool inverted = false) : CastCounter(module, aid);

public class CastGaze(BossModule module, Enum aid, bool inverted = false, float range = 10000) : GenericGaze(module, aid, inverted);
