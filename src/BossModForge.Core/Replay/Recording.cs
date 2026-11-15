using System.Linq;

namespace BossModForge.Core.Replay;

public enum ReplayEncoding
{
    Text,
    Binary,
    BinaryBrotli,
}

public sealed record RecordingMeta(string Path, ReplayEncoding Encoding, int Version, ulong Qpf, string GameVersion, DateTime StartTime);

public sealed class Recording
{
    public Recording(
        RecordingMeta meta,
        IReadOnlyList<Op> ops,
        IReadOnlyList<ParseIssue> issues,
        IReadOnlyDictionary<Tag, int> tagCounts,
        IReadOnlyList<ActorTimeline> actors)
    {
        Meta = meta;
        Ops = ops;
        Issues = issues;
        TagCounts = tagCounts;
        Actors = actors;
        Degraded = issues.Any(i => i.Severity == IssueSeverity.Degraded);
        Zones = ops.OfType<ZoneChangeOp>().ToArray();
        End = ops.Count > 0 ? ops[^1].Timestamp : meta.StartTime;
    }

    public RecordingMeta Meta { get; }
    public IReadOnlyList<Op> Ops { get; }
    public IReadOnlyList<ParseIssue> Issues { get; }
    public bool Degraded { get; }
    public IReadOnlyDictionary<Tag, int> TagCounts { get; }
    public IReadOnlyList<ActorTimeline> Actors { get; }
    public IReadOnlyList<ZoneChangeOp> Zones { get; }
    public DateTime Start => Meta.StartTime;
    public DateTime End { get; }

    public ActorTimeline? ActorAt(ulong instanceId, DateTime t)
    {
        ActorTimeline? match = null;
        foreach (var actor in Actors)
        {
            if (actor.InstanceId == instanceId && actor.IsAliveAt(t))
                match = actor;
        }
        return match;
    }

    // Merges several recordings of the same logical fight into one stream for analysis.
    // Meta: Path = "+"-joined paths, Encoding = first, Version = max, Qpf = first,
    // GameVersion = first non-empty, StartTime = min. Ops and issues are concatenated in input
    // order; per-file header blocks (FRAM/ZONE/party ACT+) repeat inside the stream — they are
    // idempotent for inference (zone-set with identical values, actor re-create as a new
    // incarnation), so no ops are dropped. TagCounts are summed; actors are rebuilt from the
    // concatenated ops, which requires actor instance ids to be distinct across recordings
    // (colliding ids merge into extra incarnations and can corrupt pull segmentation).
    public static Recording Concat(IReadOnlyList<Recording> recordings)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        if (recordings.Count == 0)
            throw new ArgumentException("At least one recording is required.", nameof(recordings));

        var ops = new List<Op>();
        var issues = new List<ParseIssue>();
        var tagCounts = new SortedDictionary<Tag, int>();
        foreach (var recording in recordings)
        {
            ops.AddRange(recording.Ops);
            issues.AddRange(recording.Issues);
            foreach (var (tag, count) in recording.TagCounts)
                tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + count;
        }

        var first = recordings[0].Meta;
        var meta = new RecordingMeta(
            Path: string.Join("+", recordings.Select(r => r.Meta.Path)),
            Encoding: first.Encoding,
            Version: recordings.Max(r => r.Meta.Version),
            Qpf: first.Qpf,
            GameVersion: recordings.Select(r => r.Meta.GameVersion).FirstOrDefault(g => !string.IsNullOrEmpty(g)) ?? "",
            StartTime: recordings.Min(r => r.Meta.StartTime));
        return new Recording(meta, ops, issues, tagCounts, BuildActors(ops));
    }

    internal static IReadOnlyList<ActorTimeline> BuildActors(IReadOnlyList<Op> ops)
    {
        var actors = new List<ActorTimeline>();
        var live = new Dictionary<ulong, ActorTimeline>();
        var incarnations = new Dictionary<ulong, int>();

        foreach (var op in ops)
        {
            switch (op)
            {
                case ActorCreateOp create:
                {
                    incarnations.TryGetValue(create.InstanceId, out var n);
                    var timeline = new ActorTimeline(create, n);
                    incarnations[create.InstanceId] = n + 1;
                    live[create.InstanceId] = timeline;
                    actors.Add(timeline);
                    break;
                }
                case ActorDestroyOp destroy:
                    if (live.Remove(destroy.InstanceId, out var dying))
                        dying.Destroy(destroy.Timestamp);
                    break;
                case ActorRenameOp rename:
                    if (live.TryGetValue(rename.InstanceId, out var named))
                        named.Rename(rename.Name, rename.NameId);
                    break;
                case ActorClassChangeOp cls:
                    if (live.TryGetValue(cls.InstanceId, out var job))
                        job.ClassChange(cls.Class, cls.Level);
                    break;
                case ActorMoveOp move:
                    if (live.TryGetValue(move.InstanceId, out var moving))
                        moving.AddMove(move.Timestamp, move.PosRot);
                    break;
                case ActorSizeChangeOp size:
                    if (live.TryGetValue(size.InstanceId, out var sized))
                        sized.SizeChange(size.HitboxRadius);
                    break;
                case ActorHpMpOp hp:
                    if (live.TryGetValue(hp.InstanceId, out var hpd))
                        hpd.AddHpMp(hp.Timestamp, hp.HpMp);
                    break;
                case ActorTargetableOp atg:
                    if (live.TryGetValue(atg.InstanceId, out var tgt))
                        tgt.AddTargetable(atg.Timestamp, atg.Value);
                    break;
                case ActorDeadOp dead:
                    if (live.TryGetValue(dead.InstanceId, out var corpse))
                        corpse.AddDead(dead.Timestamp, dead.Value);
                    break;
                case ActorCombatOp combat:
                    if (live.TryGetValue(combat.InstanceId, out var fighter))
                        fighter.AddCombat(combat.Timestamp, combat.Value);
                    break;
            }
        }

        return actors;
    }
}
