using System.Collections.Immutable;
using BossModForge.Core.Inference;
using BossModForge.Core.Inference.Timeline;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Validation;

public sealed record PrimaryCast(uint Aid, AbilityInstance Instance);

public sealed record SimulatedVisit(
    string StepId,
    string Kind,
    uint? Aid,
    bool Expected,
    bool Matched,
    double? AbsErrorSeconds);

public sealed record SimulatedPull(int PullIndex, ImmutableArray<SimulatedVisit> Visits);

public static class TimelineSimulator
{
    public const double CastLookbackSeconds = 0.5;

    public static SimulatedPull Walk(
        EncounterModel em,
        Recording recording,
        Pull pull,
        IReadOnlyList<PrimaryCast> primaryCasts,
        ValidationOptions options)
    {
        var visits = ImmutableArray.CreateBuilder<SimulatedVisit>();
        var timeline = em.Timeline;
        if (timeline is null || timeline.Steps.IsDefaultOrEmpty)
            return new SimulatedPull(pull.Index, visits.ToImmutable());

        var walker = new Walker(recording, pull, primaryCasts, options, visits);
        var phases = timeline.Phases.IsDefaultOrEmpty
            ? [new PhaseModel(0, "Phase0", "Death", null, 0, timeline.Steps.Length, [])]
            : timeline.Phases;
        foreach (var phase in phases)
            walker.WalkPhase(timeline, phase);
        return new SimulatedPull(pull.Index, visits.ToImmutable());
    }

    private sealed class Walker
    {
        private readonly Recording _recording;
        private readonly Pull _pull;
        private readonly List<PrimaryCast> _casts;
        private readonly HashSet<int> _consumed = [];
        private readonly ValidationOptions _options;
        private readonly ImmutableArray<SimulatedVisit>.Builder _visits;
        private DateTime _tPrev;

        public Walker(
            Recording recording,
            Pull pull,
            IReadOnlyList<PrimaryCast> primaryCasts,
            ValidationOptions options,
            ImmutableArray<SimulatedVisit>.Builder visits)
        {
            _recording = recording;
            _pull = pull;
            _casts = primaryCasts
                .Where(c => c.Instance.CastStart is not null)
                .OrderBy(c => c.Instance.CastStart)
                .ToList();
            _options = options;
            _visits = visits;
            _tPrev = pull.Start;
        }

        public void WalkPhase(TimelineModel timeline, PhaseModel phase)
        {
            if (!TryPhaseStart(phase, out var start))
            {
                MissWholePhase(timeline, phase);
                return;
            }

            _tPrev = start;
            var stepStart = phase.FirstStep;
            var stepEnd = Math.Min(timeline.Steps.Length, stepStart + phase.StepCount);
            var loop = timeline.Loop;
            var loopInPhase = loop is not null
                && loop.StartStep >= stepStart
                && loop.StartStep < stepEnd;

            if (!loopInPhase)
            {
                for (var i = stepStart; i < stepEnd; i++)
                    Visit(timeline.Steps[i], expected: true);
                return;
            }

            for (var i = stepStart; i < loop!.StartStep && i < stepEnd; i++)
                Visit(timeline.Steps[i], expected: true);

            for (var r = 0; r < loop.Repeats; r++)
            {
                for (var j = 0; j < loop.Length; j++)
                    Visit(timeline.Steps[loop.StartStep + j], expected: true);
            }

            for (var t = 0; t < loop.TrailingPartial; t++)
                Visit(timeline.Steps[loop.StartStep + t], expected: true);

            var firstBody = timeline.Steps[loop.StartStep];
            for (var extra = 0; extra < 32 && PeekCast(firstBody) is not null; extra++)
            {
                for (var j = 0; j < loop.Length; j++)
                    Visit(timeline.Steps[loop.StartStep + j], expected: false);
            }

            var loopEnd = loop.StartStep + loop.Length;
            for (var i = loopEnd; i < stepEnd; i++)
                Visit(timeline.Steps[i], expected: true);
        }

        private void MissWholePhase(TimelineModel timeline, PhaseModel phase)
        {
            foreach (var step in UnrollExpected(timeline, phase))
            {
                if (IsTimeout(step.Kind))
                    continue;
                _visits.Add(new SimulatedVisit(step.Id, step.Kind, step.Aid, true, false, null));
            }
        }

        private static IEnumerable<TimelineStep> UnrollExpected(TimelineModel timeline, PhaseModel phase)
        {
            var stepStart = phase.FirstStep;
            var stepEnd = Math.Min(timeline.Steps.Length, stepStart + phase.StepCount);
            var loop = timeline.Loop;
            var loopInPhase = loop is not null
                && loop.StartStep >= stepStart
                && loop.StartStep < stepEnd;
            if (!loopInPhase)
            {
                for (var i = stepStart; i < stepEnd; i++)
                    yield return timeline.Steps[i];
                yield break;
            }

            for (var i = stepStart; i < loop!.StartStep && i < stepEnd; i++)
                yield return timeline.Steps[i];
            for (var r = 0; r < loop.Repeats; r++)
            {
                for (var j = 0; j < loop.Length; j++)
                    yield return timeline.Steps[loop.StartStep + j];
            }

            for (var t = 0; t < loop.TrailingPartial; t++)
                yield return timeline.Steps[loop.StartStep + t];
            var loopEnd = loop.StartStep + loop.Length;
            for (var i = loopEnd; i < stepEnd; i++)
                yield return timeline.Steps[i];
        }

        private bool TryPhaseStart(PhaseModel phase, out DateTime start)
        {
            if (phase.Index == 0)
            {
                start = _pull.Start;
                return true;
            }

            if (phase.HpThreshold is { } hp)
                return TryHpTime(hp, out start);

            start = _pull.Start;
            return true;
        }

        private bool TryHpTime(double threshold, out DateTime start)
        {
            start = default;
            var actor = _recording.ActorAt(_pull.PrimaryInstanceId, _pull.Start)
                ?? _recording.Actors.LastOrDefault(a => a.InstanceId == _pull.PrimaryInstanceId);
            if (actor is null)
                return false;
            foreach (var sample in actor.HpMp)
            {
                if (sample.T < _pull.Start || sample.T > _pull.End)
                    continue;
                var ratio = sample.Value.MaxHp == 0 ? 1 : (double)sample.Value.CurHp / sample.Value.MaxHp;
                if (ratio <= threshold)
                {
                    start = sample.T;
                    return true;
                }
            }

            return false;
        }

        private void Visit(TimelineStep step, bool expected)
        {
            if (IsTimeout(step.Kind))
            {
                AdvanceTimeout(step);
                return;
            }

            if (string.Equals(step.Kind, "Targetable", StringComparison.Ordinal))
            {
                VisitTargetable(step, expected);
                return;
            }

            VisitCast(step, expected);
        }

        private void AdvanceTimeout(TimelineStep step)
        {
            var match = FindCast(step, _tPrev + TimeSpan.FromSeconds(step.Delay));
            if (match is { } found)
            {
                _consumed.Add(found.Index);
                _tPrev = EndTime(found.Cast.Instance, step.CastTime);
                return;
            }

            _tPrev += TimeSpan.FromSeconds(step.Delay + step.CastTime);
        }

        private void VisitCast(TimelineStep step, bool expected)
        {
            var expectedTime = _tPrev + TimeSpan.FromSeconds(step.Delay);
            var match = FindCast(step, expectedTime);
            if (match is { } found)
            {
                _consumed.Add(found.Index);
                var actual = found.Cast.Instance.CastStart!.Value;
                var error = Math.Abs((actual - expectedTime).TotalSeconds);
                _visits.Add(new SimulatedVisit(step.Id, step.Kind, step.Aid ?? AidOf(step), expected, true, error));
                _tPrev = EndTime(found.Cast.Instance, step.CastTime);
                return;
            }

            _visits.Add(new SimulatedVisit(step.Id, step.Kind, step.Aid ?? AidOf(step), expected, false, null));
            if (expected)
                _tPrev = expectedTime + TimeSpan.FromSeconds(step.CastTime);
        }

        private void VisitTargetable(TimelineStep step, bool expected)
        {
            var expectedTime = _tPrev + TimeSpan.FromSeconds(step.Delay);
            var actual = FindTargetable(step.Targetable == true, expectedTime);
            if (actual is { } t)
            {
                var error = Math.Abs((t - expectedTime).TotalSeconds);
                _visits.Add(new SimulatedVisit(step.Id, step.Kind, null, expected, true, error));
                _tPrev = t;
                return;
            }

            _visits.Add(new SimulatedVisit(step.Id, step.Kind, null, expected, false, null));
            if (expected)
                _tPrev = expectedTime;
        }

        private AbilityInstance? PeekCast(TimelineStep step)
        {
            var expectedTime = _tPrev + TimeSpan.FromSeconds(step.Delay);
            var match = FindCast(step, expectedTime);
            return match?.Cast.Instance;
        }

        private (int Index, PrimaryCast Cast)? FindCast(TimelineStep step, DateTime expected)
        {
            var lookback = TimeSpan.FromSeconds(CastLookbackSeconds);
            var window = TimeSpan.FromSeconds(_options.TimingMatchWindowSeconds);
            (int Index, PrimaryCast Cast)? best = null;
            for (var i = 0; i < _casts.Count; i++)
            {
                if (_consumed.Contains(i))
                    continue;
                var cast = _casts[i];
                var start = cast.Instance.CastStart!.Value;
                if (start < _tPrev - lookback)
                    continue;
                if (!AidMatches(step, cast.Aid))
                    continue;
                var delta = start - expected;
                if (delta < TimeSpan.Zero)
                    delta = -delta;
                if (delta > window)
                    continue;
                if (best is null || start < best.Value.Cast.Instance.CastStart)
                    best = (i, cast);
            }

            return best;
        }

        private DateTime? FindTargetable(bool value, DateTime expected)
        {
            var actor = _recording.ActorAt(_pull.PrimaryInstanceId, _pull.Start)
                ?? _recording.Actors.LastOrDefault(a => a.InstanceId == _pull.PrimaryInstanceId);
            if (actor is null)
                return null;
            var lookback = TimeSpan.FromSeconds(CastLookbackSeconds);
            var window = TimeSpan.FromSeconds(_options.TimingMatchWindowSeconds);
            foreach (var sample in actor.Targetable)
            {
                if (sample.Value != value)
                    continue;
                if (sample.T < _tPrev - lookback)
                    continue;
                var delta = sample.T - expected;
                if (delta < TimeSpan.Zero)
                    delta = -delta;
                if (delta > window)
                    continue;
                return sample.T;
            }

            return null;
        }

        private static DateTime EndTime(AbilityInstance inst, float castTime)
            => inst.CastEnd ?? inst.CastStart!.Value + TimeSpan.FromSeconds(castTime);

        private static bool IsTimeout(string kind)
            => string.Equals(kind, "Timeout", StringComparison.Ordinal);

        private static uint? AidOf(TimelineStep step)
            => step.Aid ?? (step.Of.IsDefaultOrEmpty ? null : step.Of[0]);

        private static bool AidMatches(TimelineStep step, uint aid)
        {
            if (!step.Of.IsDefaultOrEmpty)
                return step.Of.Contains(aid);
            return step.Aid is { } stepAid && stepAid == aid;
        }
    }
}
