namespace BossMod.Components;

public class RaidwideCast(BossModule module, Enum aid, string hint = "Raidwide") : CastHint(module, aid, hint);

public class SingleTargetCast(BossModule module, Enum aid, string hint = "Tankbuster") : CastHint(module, aid, hint);
