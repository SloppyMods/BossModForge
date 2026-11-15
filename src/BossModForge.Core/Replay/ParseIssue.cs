namespace BossModForge.Core.Replay;

public enum IssueSeverity
{
    Info,
    Warning,
    Degraded,
    Error,
}

public sealed record ParseIssue(IssueSeverity Severity, string Code, string Message, int? Line = null, long? ByteOffset = null);

public sealed record ParseOptions(IProgress<float>? Progress = null, bool KeepRawOps = true, ReplayLineage Lineage = ReplayLineage.Auto);

/// <summary>
/// Which plugin wrote the recording. vbm and bmr share version numbers but diverge from v27 on:
/// bmr appends <c>int Renderflags</c> to <c>ACT+</c> (v>=27) and gates the <c>CLAF</c> objective-npc uint at v29 (vbm: v28).
/// <see cref="Auto"/> detects from the first v27+ <c>ACT+</c> payload (docs/UPSTREAM.md §12).
/// </summary>
public enum ReplayLineage
{
    Auto,
    Vbm,
    Bmr,
}
