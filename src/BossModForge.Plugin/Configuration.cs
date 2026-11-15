using Dalamud.Configuration;
using Dalamud.Plugin;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Core.Replay;

namespace BossModForge.Plugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<string> ReplayDirectories { get; set; } = [];
    public string? OutputDirectory { get; set; }
    public string Flavor { get; set; } = "vbm";
    public string? DefaultNamespace { get; set; }
    public string Maturity { get; set; } = "WIP";
    public string? Contributors { get; set; }
    public bool OpenFolderAfterExport { get; set; } = true;
    public Dictionary<string, string> OverridesBySha256 { get; set; } = new(StringComparer.Ordinal);
    public bool AdvancedThresholds { get; set; }
    public bool ShowRecordingCountToast { get; set; }
    public float ToastMinFightSeconds { get; set; } = 45;

    public float? SegmenterMinPullDurationSeconds { get; set; }
    public float? SegmenterCombatGapSeconds { get; set; }

    public float? ShapeLagMinSeconds { get; set; }
    public float? ShapeLagMaxSeconds { get; set; }
    public float? ShapeLagStepSeconds { get; set; }
    public double? ShapePriorWeight { get; set; }
    public double? ShapeFitWeight { get; set; }
    public double? ShapeAmbiguityWeight { get; set; }
    public float? ShapeAmbiguityMarginY { get; set; }
    public float? ShapeAmbiguityMarginDeg { get; set; }
    public double? ShapeRaidwideFraction { get; set; }
    public float? ShapeRaidwideArenaRatio { get; set; }
    public float? ShapeRadiusSnapY { get; set; }
    public float? ShapeAngleSnapDeg { get; set; }

    public float? BoundsRadiusSnapTolerance { get; set; }
    public float? BoundsRectPadding { get; set; }
    public double? BoundsCircleFillMin { get; set; }
    public double? BoundsBoxFillMin { get; set; }
    public float? BoundsPolygonEpsilon { get; set; }

    public PipelineOptions ToPipelineOptions()
    {
        SegmenterOptions? segmenter = null;
        if (SegmenterMinPullDurationSeconds is { } minPull || SegmenterCombatGapSeconds is { } gap)
        {
            var d = SegmenterOptions.Default;
            segmenter = new SegmenterOptions(
                TimeSpan.FromSeconds(SegmenterMinPullDurationSeconds ?? (float)d.MinPullDuration.TotalSeconds),
                TimeSpan.FromSeconds(SegmenterCombatGapSeconds ?? (float)d.CombatGapToSplit.TotalSeconds));
        }

        ShapeInferenceOptions? shape = null;
        if (HasShapeOverrides())
        {
            var d = ShapeInferenceOptions.Default;
            shape = new ShapeInferenceOptions(
                ShapeLagMinSeconds ?? d.LagMinSeconds,
                ShapeLagMaxSeconds ?? d.LagMaxSeconds,
                ShapeLagStepSeconds ?? d.LagStepSeconds,
                ShapePriorWeight ?? d.PriorWeight,
                ShapeFitWeight ?? d.FitWeight,
                ShapeAmbiguityWeight ?? d.AmbiguityWeight,
                ShapeAmbiguityMarginY ?? d.AmbiguityMarginY,
                ShapeAmbiguityMarginDeg ?? d.AmbiguityMarginDeg,
                ShapeRaidwideFraction ?? d.RaidwideFraction,
                ShapeRaidwideArenaRatio ?? d.RaidwideArenaRatio,
                ShapeRadiusSnapY ?? d.RadiusSnapY,
                ShapeAngleSnapDeg ?? d.AngleSnapDeg);
        }

        BoundsFitterOptions? bounds = null;
        if (HasBoundsOverrides())
        {
            var d = BoundsFitterOptions.Default;
            bounds = new BoundsFitterOptions(
                BoundsRadiusSnapTolerance ?? d.RadiusSnapTolerance,
                BoundsRectPadding ?? d.RectPadding,
                BoundsCircleFillMin ?? d.CircleFillMin,
                BoundsBoxFillMin ?? d.BoxFillMin,
                BoundsPolygonEpsilon ?? d.PolygonEpsilon);
        }

        BuildOptions? build = null;
        if (!string.IsNullOrWhiteSpace(DefaultNamespace) || shape is not null || bounds is not null)
            build = new BuildOptions(Namespace: DefaultNamespace, Shape: shape, Bounds: bounds);

        return new PipelineOptions(Segmenter: segmenter, Build: build);
    }

    public void Save(IDalamudPluginInterface pluginInterface) => pluginInterface.SavePluginConfig(this);

    private bool HasShapeOverrides() =>
        ShapeLagMinSeconds.HasValue || ShapeLagMaxSeconds.HasValue || ShapeLagStepSeconds.HasValue
        || ShapePriorWeight.HasValue || ShapeFitWeight.HasValue || ShapeAmbiguityWeight.HasValue
        || ShapeAmbiguityMarginY.HasValue || ShapeAmbiguityMarginDeg.HasValue
        || ShapeRaidwideFraction.HasValue || ShapeRaidwideArenaRatio.HasValue
        || ShapeRadiusSnapY.HasValue || ShapeAngleSnapDeg.HasValue;

    private bool HasBoundsOverrides() =>
        BoundsRadiusSnapTolerance.HasValue || BoundsRectPadding.HasValue
        || BoundsCircleFillMin.HasValue || BoundsBoxFillMin.HasValue || BoundsPolygonEpsilon.HasValue;
}
