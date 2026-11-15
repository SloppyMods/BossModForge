using System.Collections.Immutable;
using System.Globalization;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Validation;

public sealed record RecordingContext(
    Recording Recording,
    ImmutableArray<Pull> Pulls,
    AbilityCatalog Catalog);

public static class Validator
{
    public static ValidationReport Validate(
        EncounterModel em,
        IReadOnlyList<Recording> recordings,
        ValidationOptions? o = null)
    {
        o ??= ValidationOptions.Default;
        var segmenter = o.Segmenter ?? SegmenterOptions.Default;
        var gameData = o.GameData ?? NullGameData.Instance;
        var contexts = new List<RecordingContext>(recordings.Count);
        var perRecording = ImmutableArray.CreateBuilder<RecordingValidation>(recordings.Count);
        var allVisits = new List<(int RecIndex, string Path, Pull Pull, SimulatedVisit Visit)>();
        var pullCount = 0;

        for (var r = 0; r < recordings.Count; r++)
        {
            var recording = recordings[r];
            var pulls = PullSegmenter.Segment(recording, segmenter);
            var matching = pulls.IsDefaultOrEmpty
                ? ImmutableArray<Pull>.Empty
                : pulls.Where(p => p.PrimaryOid == em.Encounter.PrimaryOid).ToImmutableArray();
            var participants = ParticipantClassifier.Classify(recording, matching, gameData);
            var catalog = AbilityCataloger.Catalog(recording, matching, participants, gameData);
            contexts.Add(new RecordingContext(recording, matching, catalog));

            var pullReports = ImmutableArray.CreateBuilder<PullValidation>(matching.Length);
            foreach (var pull in matching)
            {
                pullCount++;
                var casts = CollectPrimaryCasts(catalog, pull);
                var simulated = TimelineSimulator.Walk(em, recording, pull, casts, o);
                var deltas = simulated.Visits.Where(v => v.AbsErrorSeconds is not null).Select(v => v.AbsErrorSeconds!.Value).ToImmutableArray();
                var observed = simulated.Visits.Count(v => v.Expected && v.Matched);
                var missed = simulated.Visits.Count(v => v.Expected && !v.Matched);
                pullReports.Add(new PullValidation(r, recording.Meta.Path, pull.Index, pull.Start, deltas, observed, missed));
                foreach (var visit in simulated.Visits)
                    allVisits.Add((r, recording.Meta.Path, pull, visit));
            }

            perRecording.Add(new RecordingValidation(recording.Meta.Path, matching.Length, pullReports.ToImmutable()));
        }

        var timing = BuildTiming(allVisits, o);
        var aoe = AoeValidator.Evaluate(em, contexts, o);
        var coverage = CoverageValidator.Evaluate(em, contexts);
        var failures = ImmutableArray.CreateBuilder<string>();
        if (!timing.Passed)
        {
            failures.Add(
                "timing p90 " + timing.P90.ToString("0.000", CultureInfo.InvariantCulture)
                + " missed " + timing.Missed.ToString(CultureInfo.InvariantCulture)
                + "/" + (timing.Samples + timing.Missed).ToString(CultureInfo.InvariantCulture));
        }

        foreach (var report in aoe.Where(a => !a.Passed))
        {
            failures.Add(
                "aoe " + report.Component + " AID " + report.Aid.ToString(CultureInfo.InvariantCulture)
                + " P=" + report.Precision.ToString("0.000", CultureInfo.InvariantCulture)
                + " R=" + report.Recall.ToString("0.000", CultureInfo.InvariantCulture));
        }

        var review = ImmutableArray.CreateBuilder<ReviewItem>();
        foreach (var failed in aoe.Where(a => !a.Passed))
            review.Add(AoeValidator.ShapeReview(failed));
        if (CoverageValidator.CoverageReview(coverage) is { } coverageReview)
            review.Add(coverageReview);

        var paths = recordings.Select(rec => rec.Meta.Path).ToImmutableArray();
        var passed = timing.Passed && aoe.All(a => a.Passed);
        return new ValidationReport(
            "",
            paths,
            pullCount,
            timing,
            aoe,
            coverage,
            passed,
            failures.ToImmutable(),
            review.ToImmutable(),
            perRecording.ToImmutable(),
            new ValidationThresholds(
                o.TimingP90MaxSeconds,
                o.TimingMatchWindowSeconds,
                o.MaxMissedStepFraction,
                o.AoeMinPrecision,
                o.AoeMinRecall));
    }

    public static EncounterModel ApplyDowngrades(EncounterModel em, ValidationReport report)
    {
        var failed = (report.Aoe.IsDefaultOrEmpty ? [] : report.Aoe)
            .Where(a => !a.Passed)
            .Select(a => a.Aid)
            .ToHashSet();
        var abilities = em.Abilities.Select(a =>
            failed.Contains(a.ActionId)
                ? a with { Confidence = Confidence.LowBecause("dry-validate shape precision/recall") }
                : a).ToImmutableArray();
        var review = em.Review.IsDefault ? ImmutableArray<ReviewItem>.Empty : em.Review;
        if (!report.SuggestedReview.IsDefaultOrEmpty)
            review = review.AddRange(report.SuggestedReview);
        return em with { Abilities = abilities, Review = review };
    }

    private static List<PrimaryCast> CollectPrimaryCasts(AbilityCatalog catalog, Pull pull)
    {
        var list = new List<PrimaryCast>();
        foreach (var ability in catalog.Abilities.IsDefaultOrEmpty ? [] : catalog.Abilities)
        {
            foreach (var inst in ability.Instances.IsDefaultOrEmpty ? [] : ability.Instances)
            {
                if (inst.PullIndex != pull.Index)
                    continue;
                if (inst.SourceInstanceId != pull.PrimaryInstanceId)
                    continue;
                if (inst.CastStart is null)
                    continue;
                list.Add(new PrimaryCast(ability.Action.Id, inst));
            }
        }

        return list;
    }

    private static TimingReport BuildTiming(
        List<(int RecIndex, string Path, Pull Pull, SimulatedVisit Visit)> visits,
        ValidationOptions options)
    {
        var byStep = visits
            .GroupBy(v => v.Visit.StepId, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);
        var perStep = ImmutableArray.CreateBuilder<StepTiming>();
        var allErrors = new List<double>();
        var totalMissed = 0;
        foreach (var group in byStep)
        {
            var first = group.First().Visit;
            var expected = group.Count(v => v.Visit.Expected);
            var observed = group.Count(v => v.Visit.Matched);
            var missed = group.Count(v => v.Visit.Expected && !v.Visit.Matched);
            var errors = group.Where(v => v.Visit.AbsErrorSeconds is not null).Select(v => v.Visit.AbsErrorSeconds!.Value).ToList();
            errors.Sort();
            allErrors.AddRange(errors);
            totalMissed += missed;
            perStep.Add(new StepTiming(
                first.StepId,
                first.Kind,
                first.Aid,
                expected,
                observed,
                missed,
                Percentile(errors, 0.5),
                Percentile(errors, 0.9),
                errors.Count == 0 ? 0 : errors[^1]));
        }

        allErrors.Sort();
        var samples = allErrors.Count;
        var denom = samples + totalMissed;
        var missedFrac = denom == 0 ? 0 : (double)totalMissed / denom;
        var p50 = Percentile(allErrors, 0.5);
        var sampleP90 = Percentile(allErrors, 0.9);
        var stepP90 = perStep.Count == 0 ? 0 : perStep.Max(s => s.P90Error);
        var p90 = Math.Max(sampleP90, stepP90);
        var max = allErrors.Count == 0 ? 0 : allErrors[^1];
        var passed = p90 <= options.TimingP90MaxSeconds && missedFrac <= options.MaxMissedStepFraction;
        return new TimingReport(perStep.Count, samples, totalMissed, p50, p90, max, passed, perStep.ToImmutable());
    }

    internal static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0)
            return 0;
        if (sorted.Count == 1)
            return sorted[0];
        var rank = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi)
            return sorted[lo];
        var w = rank - lo;
        return sorted[lo] * (1 - w) + sorted[hi] * w;
    }
}

public static class DryValidator
{
    public static ValidationReport Validate(
        EncounterModel em,
        IReadOnlyList<(string Path, Recording Recording)> recordings,
        ValidationOptions? options = null)
    {
        var list = recordings.Select(r => r.Recording).ToList();
        var report = Validator.Validate(em, list, options);
        return report with { Recordings = recordings.Select(r => r.Path).ToImmutableArray() };
    }

    public static string ToJson(ValidationReport report) => ValidationReportWriter.ToJson(report);

    public static string ToMarkdown(ValidationReport report) => ValidationReportWriter.ToMarkdown(report);

    public static EncounterModel ApplyDowngrades(EncounterModel em, ValidationReport report)
        => Validator.ApplyDowngrades(em, report);
}
