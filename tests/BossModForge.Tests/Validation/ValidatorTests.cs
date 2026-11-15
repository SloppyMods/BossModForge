using BossModForge.Core.Inference;
using BossModForge.Core.Validation;
using BossModForge.Tests.Fixtures;
using Xunit;

namespace BossModForge.Tests.Validation;

public sealed class ValidatorTests
{
    [Fact]
    public void SyntheticRotation_OwnRecording_Passes()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, rec) = ValidationFixtures.Rotation();
        var report = Validator.Validate(em, [rec], ValidationFixtures.RotationOptions);
        Assert.True(report.Passed, string.Join("; ", report.Failures));
        Assert.True(report.Timing.Passed);
        Assert.True(report.Timing.P90 <= 0.2, "p90 " + report.Timing.P90);
        Assert.NotEmpty(report.Aoe);
        Assert.All(report.Aoe, a =>
        {
            Assert.Equal(1.0, a.Precision);
            Assert.Equal(1.0, a.Recall);
            Assert.True(a.Passed);
        });
        Assert.Equal(1.0, report.Coverage.Fraction);
        Assert.True(report.PullCount >= 3);
    }

    [Fact]
    public void WrongCircleRadius_RecallFails_AndDowngrades()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, rec) = ValidationFixtures.Rotation();
        var broken = ValidationFixtures.WithCircleRadius(em, 3);
        var report = Validator.Validate(broken, [rec], ValidationFixtures.RotationOptions);
        var circle = Assert.Single(report.Aoe, a => a.Aid == SyntheticRotation.Circle);
        Assert.True(circle.Recall < 0.95, "recall " + circle.Recall);
        Assert.False(circle.Passed);
        Assert.False(report.Passed);
        var down = Validator.ApplyDowngrades(broken, report);
        var ability = Assert.Single(down.Abilities, a => a.ActionId == SyntheticRotation.Circle);
        Assert.Equal(ConfidenceBand.Low, ability.Confidence.Band);
        Assert.Contains(down.Review, r => r.Severity == ReviewSeverity.High && r.Subject == "shape");
    }

    [Fact]
    public void PlantedMissingStep_CountsMiss_AndFailsWhenThresholdExceeded()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, rec) = ValidationFixtures.Rotation();
        var planted = ValidationFixtures.WithPlantedStep(em);
        var report = Validator.Validate(
            planted,
            [rec],
            ValidationFixtures.RotationOptions with { MaxMissedStepFraction = 0 });
        Assert.True(report.Timing.Missed >= 1);
        var step = Assert.Single(report.Timing.PerStep, s => s.StepId == "planted");
        Assert.True(step.Missed >= 1);
        Assert.False(report.Timing.Passed);
        Assert.False(report.Passed);
    }

    [Fact]
    public void JitterRecording_AgainstCleanEm_P90Fails()
    {
        InferenceStages.SkipIfUnimplemented();
        var (clean, _) = ValidationFixtures.Rotation(jitter: false);
        var (_, jitterRec) = ValidationFixtures.Rotation(jitter: true);
        var report = Validator.Validate(clean, [jitterRec], ValidationFixtures.RotationOptions);
        Assert.True(report.Timing.P90 > 1.0, "p90 " + report.Timing.P90);
        Assert.False(report.Timing.Passed);
        Assert.False(report.Passed);
    }

    [Fact]
    public void JitterEm_TimeoutStepsExcluded_Passes()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, rec) = ValidationFixtures.Rotation(jitter: true);
        Assert.Contains(em.Timeline!.Steps, s => s.Kind == "Timeout");
        var report = Validator.Validate(em, [rec], ValidationFixtures.RotationOptions);
        Assert.DoesNotContain(report.Timing.PerStep, s => s.Kind == "Timeout");
        Assert.True(report.Timing.Passed, string.Join("; ", report.Failures));
        Assert.True(report.Passed, string.Join("; ", report.Failures));
    }

    [Fact]
    public void ReportJson_IsDeterministic()
    {
        InferenceStages.SkipIfUnimplemented();
        var (em, rec) = ValidationFixtures.Rotation();
        var report = Validator.Validate(em, [rec], ValidationFixtures.RotationOptions);
        var a = ValidationReportWriter.ToJson(report);
        var b = ValidationReportWriter.ToJson(report);
        Assert.Equal(a, b);
        Assert.Equal(a, ValidationReportWriter.ToJson(report));
        Assert.Contains("\"passed\":", a, StringComparison.Ordinal);
    }
}
