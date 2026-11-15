using System.Collections.Immutable;
using System.Globalization;

namespace BossModForge.Core.Inference.Timeline;

internal static class ConsensusBuilder
{
    public static ImmutableArray<ConsensusStep> Build(
        ImmutableArray<AlignmentColumn> profile,
        Dictionary<int, Pull> pullByIndex,
        Dictionary<int, DateTime> pullStarts,
        int pullCount,
        IReadOnlyDictionary<uint, float> castTimeByAid,
        TimelineAlignerOptions options)
    {
        var raw = profile.Select(col => col.Members.ToList()).ToList();
        var minBranch = pullCount >= 4 ? 2 : 1;
        var expanded = ExpandBranches(raw, minBranch);
        var steps = ImmutableArray.CreateBuilder<ConsensusStep>(expanded.Count);
        for (var i = 0; i < expanded.Count; i++)
        {
            var members = expanded[i].OrderBy(m => m.PullIndex).ThenBy(m => m.T).ToImmutableArray();
            var col = new AlignmentColumn(members);
            var delays = MemberDelays(members, expanded, i, pullByIndex, pullStarts);
            var delayMedian = TimelineStats.Round1(TimelineStats.Median(delays));
            var delayMad = TimelineStats.Round1(TimelineStats.Median(delays.Select(d => MathF.Abs(d - delayMedian)).ToList()));
            var of = members.Where(m => m.Kind == AnchorKind.Cast).Select(m => m.Aid).Distinct().OrderBy(a => a).ToImmutableArray();
            if (of.IsDefaultOrEmpty && col.Kind == AnchorKind.Cast)
                of = [col.ConsensusAid];
            var aid = of.Length > 0 ? of[0] : col.ConsensusAid;
            var castTime = ResolveCastTime(of, aid, col.Kind, members, castTimeByAid);
            var kind = col.Kind == AnchorKind.Cast
                ? (of.Length >= 2 ? StepKind.CastMulti : StepKind.Cast)
                : StepKind.Targetable;
            bool? targetable = kind == StepKind.Targetable ? col.Kind == AnchorKind.TargetableOn : null;
            string? review = null;
            if (kind == StepKind.CastMulti)
            {
                var parts = of.Select(a => $"{a} {members.Count(m => m.Aid == a)}/{pullCount}");
                review = "branch: pulls diverge (" + string.Join(", ", parts) + ")";
            }

            if (kind is StepKind.Cast or StepKind.CastMulti)
            {
                if (delayMad > options.MadTimeoutSeconds)
                {
                    kind = StepKind.Timeout;
                    review = $"timing jitter MAD {delayMad.ToString("0.0", CultureInfo.InvariantCulture)}s > 1.5s — emitted Timeout";
                }
                else if (pullCount > 0 && members.Length < options.MinSupportFraction * pullCount)
                {
                    kind = StepKind.Timeout;
                    review = $"seen in {members.Length}/{pullCount} pulls";
                }
            }

            steps.Add(new ConsensusStep(
                i, kind, aid, of.IsDefaultOrEmpty ? [aid] : of, targetable,
                delayMedian, delayMad, castTime, members.Length, pullCount, members, review));
        }

        return steps.ToImmutable();
    }

    private static List<List<AnchorEvent>> ExpandBranches(List<List<AnchorEvent>> raw, int minBranch)
    {
        var expanded = new List<List<AnchorEvent>>();
        foreach (var members in raw)
        {
            var aids = members.Where(m => m.Kind == AnchorKind.Cast).GroupBy(m => m.Aid)
                .Select(g => (Aid: g.Key, Count: g.Count()))
                .OrderBy(g => g.Aid)
                .ToList();
            if (aids.Count < 2 || aids.All(a => a.Count >= minBranch))
            {
                expanded.Add(members);
                continue;
            }

            var majority = members.Where(m => m.Kind != AnchorKind.Cast || aids.First(a => a.Aid == m.Aid).Count >= minBranch).ToList();
            if (majority.Count > 0)
                expanded.Add(majority);
            foreach (var extra in members.Where(m => m.Kind == AnchorKind.Cast && aids.First(a => a.Aid == m.Aid).Count < minBranch))
                expanded.Add([extra]);
        }

        return expanded;
    }

    internal static List<float> MemberDelays(
        ImmutableArray<AnchorEvent> members,
        List<List<AnchorEvent>> columns,
        int columnIndex,
        Dictionary<int, Pull> pullByIndex,
        Dictionary<int, DateTime> pullStarts)
    {
        var delays = new List<float>(members.Length);
        foreach (var member in members)
        {
            DateTime prevT = default;
            float prevCast = 0;
            var found = false;
            for (var k = columnIndex - 1; k >= 0; k--)
            {
                foreach (var earlier in columns[k])
                {
                    if (earlier.PullIndex != member.PullIndex)
                        continue;
                    prevT = earlier.T;
                    prevCast = earlier.Kind == AnchorKind.Cast ? earlier.CastTime : 0;
                    found = true;
                    break;
                }

                if (found)
                    break;
            }

            if (!found)
            {
                prevT = pullByIndex.TryGetValue(member.PullIndex, out var pull) ? pull.Start
                    : pullStarts.TryGetValue(member.PullIndex, out var start) ? start
                    : member.T;
                prevCast = 0;
            }

            var gap = (float)(member.T - prevT).TotalSeconds;
            delays.Add(member.Kind == AnchorKind.Cast ? gap - prevCast : gap);
        }

        return delays;
    }

    private static float ResolveCastTime(
        ImmutableArray<uint> of,
        uint aid,
        AnchorKind kind,
        ImmutableArray<AnchorEvent> members,
        IReadOnlyDictionary<uint, float> castTimeByAid)
    {
        if (kind != AnchorKind.Cast)
            return 0;
        if (of.Length >= 2)
        {
            var times = of.Select(id =>
                castTimeByAid.TryGetValue(id, out var t) ? t : members.FirstOrDefault(m => m.Aid == id)?.CastTime ?? 0).ToList();
            return TimelineStats.Median(times);
        }

        return castTimeByAid.TryGetValue(aid, out var known) ? known : members.IsDefaultOrEmpty ? 0 : members[0].CastTime;
    }
}

internal static class LoopFolder
{
    public static (ImmutableArray<ConsensusStep> Steps, Loop? Loop) Fold(
        ImmutableArray<ConsensusStep> unfolded,
        TimelineAlignerOptions options,
        int pullCount)
    {
        var n = unfolded.Length;
        if (n < options.MinLoopLength * options.MinLoopRepeats)
            return (unfolded, null);

        var symbols = unfolded.Select(s =>
        {
            if (s.Kind == StepKind.Targetable)
                return "T:" + (s.Targetable == true);
            var of = string.Join(",", s.Of.IsDefaultOrEmpty ? [s.Aid] : s.Of);
            return of;
        }).ToArray();

        Loop? found = null;
        var bestCoverage = -1;
        for (var length = n / 2; length >= options.MinLoopLength; length--)
        {
            for (var p = 0; p < length; p++)
            {
                var bodyStart = n - p - length;
                if (bodyStart < 0)
                    continue;
                var trailingOk = true;
                for (var t = 0; t < p; t++)
                {
                    if (symbols[n - p + t] != symbols[bodyStart + t])
                    {
                        trailingOk = false;
                        break;
                    }
                }

                if (!trailingOk)
                    continue;

                var k = 0;
                var end = n - p;
                while (end - length >= 0)
                {
                    var ok = true;
                    for (var b = 0; b < length; b++)
                    {
                        if (symbols[end - length + b] != symbols[bodyStart + b])
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (!ok)
                        break;
                    k++;
                    end -= length;
                }

                if (k < options.MinLoopRepeats)
                    continue;
                var coverage = k * length + p;
                if (found is null || coverage > bestCoverage
                    || (coverage == bestCoverage && (length > found.Length || (length == found.Length && p < found.TrailingPartial))))
                {
                    bestCoverage = coverage;
                    found = new Loop(n - p - k * length, length, k, p, unfolded[n - p - k * length].DelayMedian);
                }
            }
        }

        if (found is null)
            return (unfolded, null);

        var prefix = unfolded.Take(found.StartStep);
        var pooledBody = new List<ConsensusStep>(found.Length);
        for (var b = 0; b < found.Length; b++)
        {
            var members = new List<AnchorEvent>();
            for (var r = 0; r < found.Repeats; r++)
                members.AddRange(unfolded[found.StartStep + r * found.Length + b].Members);
            var pooledMembers = members.OrderBy(m => m.PullIndex).ThenBy(m => m.T).ToImmutableArray();
            var template = unfolded[found.StartStep + b];
            var src = Enumerable.Range(0, found.Repeats)
                .Select(r => unfolded[found.StartStep + r * found.Length + b])
                .ToArray();
            var delays = src.Select(s => s.DelayMedian).ToList();
            if (b == 0 && found.Repeats >= 2)
                delays = delays.Skip(1).ToList();
            float delayMedian;
            float delayMad;
            if (b == 0 && delays.Count == 0)
            {
                delayMedian = found.EntryDelay;
                delayMad = src.Max(s => s.DelayMad);
            }
            else
            {
                delayMedian = TimelineStats.Round1(TimelineStats.Median(delays));
                delayMad = TimelineStats.Round1(Math.Max(
                    TimelineStats.Median(delays.Select(d => MathF.Abs(d - delayMedian)).ToList()),
                    src.Max(s => s.DelayMad)));
            }

            pooledBody.Add(template with
            {
                Members = pooledMembers,
                Support = pooledMembers.Length,
                DelayMedian = delayMedian,
                DelayMad = delayMad,
                PullCount = pullCount,
            });
        }

        var folded = prefix.Concat(pooledBody).Select((s, i) => s with { Index = i }).ToImmutableArray();
        return (folded, found);
    }
}
