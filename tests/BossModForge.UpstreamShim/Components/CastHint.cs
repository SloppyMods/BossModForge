namespace BossMod.Components;

public class CastHint(BossModule module, Enum? aid, string hint, bool showCastTimeLeft = false) : CastCounter(module, aid);
