using System.Collections.Immutable;
using System.Globalization;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Inference.Timeline;

public enum PhaseCueKind
{
    HpThresholdCastingStop,
    PrimaryDeath,
    DirectorOncePerPull,
}

public sealed record PhaseCue(
    PhaseCueKind Kind,
    double HpRatio,
    ImmutableArray<(int PullIndex, DateTime T)> Occurrences,
    Confidence Confidence);

public sealed record PhaseSplit(
    ImmutableArray<PhaseCue> Cues,
    ImmutableArray<double> HpThresholds,
    ImmutableArray<Evidence> Evidence);

public static class PhaseDetector
{
    public static PhaseSplit Detect(
        Recording recording,
        ImmutableArray<Pull> pulls,
        ImmutableArray<ImmutableArray<AnchorEvent>> anchorsPerPull,
        TimelineAlignerOptions? options = null)
    {
        options ??= TimelineAlignerOptions.Default;
        if (pulls.IsDefault)
            pulls = [];
        if (anchorsPerPull.IsDefault)
            anchorsPerPull = [];

        var cues = ImmutableArray.CreateBuilder<PhaseCue>();
        var evidence = ImmutableArray.CreateBuilder<Evidence>();
        var hp = DetectHpThresholds(recording, pulls, anchorsPerPull, options, cues, evidence);
        DetectPrimaryDeath(recording, pulls, cues, evidence);
        DetectDirector(recording, pulls, cues, evidence);
        return new PhaseSplit(cues.ToImmutable(), hp, evidence.ToImmutable());
    }

    public static ImmutableArray<ImmutableArray<ImmutableArray<AnchorEvent>>> SplitByPhase(
        Recording recording,
        ImmutableArray<Pull> pulls,
        ImmutableArray<ImmutableArray<AnchorEvent>> anchorsPerPull,
        PhaseSplit split)
    {
        if (pulls.IsDefault)
            pulls = [];
        if (anchorsPerPull.IsDefault)
            anchorsPerPull = [];
        var thresholds = split.HpThresholds.IsDefault ? ImmutableArray<double>.Empty : split.HpThresholds;
        var phaseCount = thresholds.Length + 1;
        var phases = new List<AnchorEvent>[phaseCount, pulls.Length];
        for (var p = 0; p < phaseCount; p++)
        {
            for (var i = 0; i < pulls.Length; i++)
                phases[p, i] = [];
        }

        for (var i = 0; i < pulls.Length; i++)
        {
            var pull = pulls[i];
            var actor = recording.ActorAt(pull.PrimaryInstanceId, pull.Start)
                ?? recording.Actors.LastOrDefault(a => a.InstanceId == pull.PrimaryInstanceId);
            var bounds = new DateTime[phaseCount + 1];
            bounds[0] = pull.Start;
            bounds[phaseCount] = pull.End.AddTicks(1);
            for (var t = 0; t < thresholds.Length; t++)
            {
                var hit = actor is null ? pull.End : FirstHpAtOrBelow(actor, pull, thresholds[t]);
                bounds[t + 1] = hit;
            }

            var seq = i < anchorsPerPull.Length && !anchorsPerPull[i].IsDefault ? anchorsPerPull[i] : [];
            foreach (var anchor in seq)
            {
                var phase = 0;
                for (var p = 0; p < phaseCount; p++)
                {
                    if (anchor.T >= bounds[p] && anchor.T < bounds[p + 1])
                    {
                        phase = p;
                        break;
                    }
                }

                phases[phase, i].Add(anchor);
            }
        }

        var result = ImmutableArray.CreateBuilder<ImmutableArray<ImmutableArray<AnchorEvent>>>(phaseCount);
        for (var p = 0; p < phaseCount; p++)
        {
            var perPull = ImmutableArray.CreateBuilder<ImmutableArray<AnchorEvent>>(pulls.Length);
            for (var i = 0; i < pulls.Length; i++)
                perPull.Add([.. phases[p, i]]);
            result.Add(perPull.ToImmutable());
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<double> DetectHpThresholds(
        Recording recording,
        ImmutableArray<Pull> pulls,
        ImmutableArray<ImmutableArray<AnchorEvent>> anchorsPerPull,
        TimelineAlignerOptions options,
        ImmutableArray<PhaseCue>.Builder cues,
        ImmutableArray<Evidence>.Builder evidence)
    {
        if (pulls.Length == 0)
            return [];

        var votes = new Dictionary<double, List<(int PullIndex, DateTime T, double Raw)>>();
        for (var i = 0; i < pulls.Length; i++)
        {
            var pull = pulls[i];
            var seq = i < anchorsPerPull.Length && !anchorsPerPull[i].IsDefault ? anchorsPerPull[i] : [];
            var actor = recording.ActorAt(pull.PrimaryInstanceId, pull.Start)
                ?? recording.Actors.LastOrDefault(a => a.InstanceId == pull.PrimaryInstanceId);
            if (actor is null)
                continue;

            var casts = seq.Where(a => a.Kind == AnchorKind.Cast).OrderBy(a => a.T).ToArray();
            var candidates = new List<(DateTime T, double Raw)>();
            for (var c = 0; c < casts.Length - 1; c++)
            {
                var idle = casts[c + 1].T - (casts[c].CastEnd ?? casts[c].T.AddSeconds(casts[c].CastTime));
                if (idle.TotalSeconds >= options.CastingStopSeconds)
                    candidates.Add((casts[c].CastEnd ?? casts[c].T, HpRatioAt(actor, casts[c].CastEnd ?? casts[c].T)));
            }

            foreach (var off in seq.Where(a => a.Kind == AnchorKind.TargetableOff))
                candidates.Add((off.T, HpRatioAt(actor, off.T)));

            foreach (var (t, raw) in candidates)
            {
                var rounded = Math.Round(raw / 0.05) * 0.05;
                if (rounded is >= 0.95 or <= 0.05)
                    continue;
                if (Math.Abs(raw - rounded) > options.HpThresholdTolerance)
                    continue;
                if (!votes.TryGetValue(rounded, out var list))
                {
                    list = [];
                    votes[rounded] = list;
                }

                if (list.All(v => v.PullIndex != pull.Index))
                    list.Add((pull.Index, t, raw));
            }
        }

        var thresholds = ImmutableArray.CreateBuilder<double>();
        foreach (var (rounded, occ) in votes.OrderByDescending(kv => kv.Key))
        {
            if (occ.Count < options.MinSupportFraction * pulls.Length)
                continue;
            if (occ.Count != occ.Select(o => o.PullIndex).Distinct().Count())
                continue;
            if (!VocabularyOk(pulls, anchorsPerPull, occ, options))
                continue;
            thresholds.Add(rounded);
            var cueOcc = occ.Select(o => (o.PullIndex, o.T)).ToImmutableArray();
            cues.Add(new PhaseCue(PhaseCueKind.HpThresholdCastingStop, rounded, cueOcc, Confidence.Of(occ.Count / (double)pulls.Length)));
            evidence.Add(new Evidence(
                "HpThreshold",
                $"HP {rounded.ToString("P0", CultureInfo.InvariantCulture)} casting-stop / untargetable in {occ.Count}/{pulls.Length} pulls"));
        }

        return thresholds.ToImmutable();
    }

    private static bool VocabularyOk(
        ImmutableArray<Pull> pulls,
        ImmutableArray<ImmutableArray<AnchorEvent>> anchorsPerPull,
        List<(int PullIndex, DateTime T, double Raw)> occ,
        TimelineAlignerOptions options)
    {
        var jaccards = new List<double>();
        foreach (var (pullIndex, t, _) in occ)
        {
            var i = 0;
            while (i < pulls.Length && pulls[i].Index != pullIndex)
                i++;
            if (i >= pulls.Length || i >= anchorsPerPull.Length || anchorsPerPull[i].IsDefault)
                continue;
            var before = anchorsPerPull[i].Where(a => a.Kind == AnchorKind.Cast && a.T < t).Select(a => a.Aid).ToHashSet();
            var after = anchorsPerPull[i].Where(a => a.Kind == AnchorKind.Cast && a.T >= t).Select(a => a.Aid).ToHashSet();
            if (before.Count == 0 && after.Count == 0)
                continue;
            var union = before.Count + after.Count - before.Intersect(after).Count();
            var inter = before.Intersect(after).Count();
            jaccards.Add(union == 0 ? 0 : inter / (double)union);
        }

        if (jaccards.Count == 0)
            return true;
        return jaccards.Average() <= options.VocabularyJaccardMax;
    }

    private static void DetectPrimaryDeath(
        Recording recording,
        ImmutableArray<Pull> pulls,
        ImmutableArray<PhaseCue>.Builder cues,
        ImmutableArray<Evidence>.Builder evidence)
    {
        var occ = ImmutableArray.CreateBuilder<(int, DateTime)>();
        foreach (var pull in pulls)
        {
            foreach (var op in recording.Ops.OfType<ActorDeadOp>())
            {
                if (!op.Value || op.InstanceId != pull.PrimaryInstanceId)
                    continue;
                if (op.Timestamp < pull.Start || op.Timestamp > pull.End)
                    continue;
                occ.Add((pull.Index, op.Timestamp));
                break;
            }
        }

        if (occ.Count == 0)
            return;
        cues.Add(new PhaseCue(PhaseCueKind.PrimaryDeath, 0, occ.ToImmutable(), Confidence.Of(occ.Count / (double)Math.Max(1, pulls.Length))));
        evidence.Add(new Evidence("PrimaryDeath", $"DIE+ in {occ.Count}/{pulls.Length} pulls"));
    }

    private static void DetectDirector(
        Recording recording,
        ImmutableArray<Pull> pulls,
        ImmutableArray<PhaseCue>.Builder cues,
        ImmutableArray<Evidence>.Builder evidence)
    {
        if (pulls.Length == 0)
            return;
        var perPull = new List<HashSet<(uint, uint, uint)>>();
        foreach (var pull in pulls)
        {
            var counts = new Dictionary<(uint, uint, uint), int>();
            foreach (var op in recording.Ops.OfType<DirectorUpdateOp>())
            {
                if (op.Timestamp < pull.Start || op.Timestamp > pull.End)
                    continue;
                var key = (op.DirectorId, op.UpdateId, op.Param1);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }

            perPull.Add(counts.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToHashSet());
        }

        var shared = perPull[0];
        for (var i = 1; i < perPull.Count; i++)
            shared.IntersectWith(perPull[i]);
        foreach (var key in shared.OrderBy(k => k.Item1).ThenBy(k => k.Item2).ThenBy(k => k.Item3))
        {
            var occ = ImmutableArray.CreateBuilder<(int, DateTime)>();
            foreach (var pull in pulls)
            {
                var hit = recording.Ops.OfType<DirectorUpdateOp>().FirstOrDefault(op =>
                    op.Timestamp >= pull.Start && op.Timestamp <= pull.End
                    && op.DirectorId == key.Item1 && op.UpdateId == key.Item2 && op.Param1 == key.Item3);
                if (hit is not null)
                    occ.Add((pull.Index, hit.Timestamp));
            }

            cues.Add(new PhaseCue(PhaseCueKind.DirectorOncePerPull, 0, occ.ToImmutable(), Confidence.Of(1)));
            evidence.Add(new Evidence(
                "DirectorOncePerPull",
                $"DIRU ({key.Item1:X8},{key.Item2:X8},{key.Item3:X8}) once per pull"));
        }
    }

    internal static double HpRatioAt(ActorTimeline actor, DateTime t)
    {
        if (actor.HpMp.Count == 0)
            return 1;
        var best = actor.HpMp[0];
        foreach (var sample in actor.HpMp)
        {
            if (sample.T <= t)
                best = sample;
            else
                break;
        }

        return best.Value.MaxHp == 0 ? 1 : (double)best.Value.CurHp / best.Value.MaxHp;
    }

    private static DateTime FirstHpAtOrBelow(ActorTimeline actor, Pull pull, double threshold)
    {
        foreach (var sample in actor.HpMp)
        {
            if (sample.T < pull.Start || sample.T > pull.End)
                continue;
            var ratio = sample.Value.MaxHp == 0 ? 1 : (double)sample.Value.CurHp / sample.Value.MaxHp;
            if (ratio <= threshold)
                return sample.T;
        }

        return pull.End;
    }
}
