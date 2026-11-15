using System.Collections.Immutable;
using BossModForge.Core.Emit;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Pipeline;

public enum PipelineStage { Parsing, Segmenting, Classifying, Cataloging, Building, ApplyingOverrides, Done }

public sealed record PipelineProgress(PipelineStage Stage, float Fraction);

public sealed record PipelineOptions(
    ParseOptions? Parse = null,
    SegmenterOptions? Segmenter = null,
    CatalogOptions? Catalog = null,
    BuildOptions? Build = null);

public sealed record AnalysisResult(
    Recording Recording,
    ImmutableArray<Pull> Pulls,
    ImmutableArray<Participant> Participants,
    AbilityCatalog Catalog,
    EncounterModel Inferred,
    EncounterModel Effective);

public static class ForgePipeline
{
    public static Recording Parse(string path, PipelineOptions? options = null, CancellationToken ct = default)
        => ReplayParser.Parse(path, options?.Parse ?? new ParseOptions(KeepRawOps: false), ct);

    public static Recording Parse(Stream stream, string displayPath, PipelineOptions? options = null, CancellationToken ct = default)
        => ReplayParser.Parse(stream, displayPath, options?.Parse ?? new ParseOptions(KeepRawOps: false), ct);

    public static GameDataKeys CollectKeys(Recording recording) => GameDataKeys.Collect(recording);

    public static AnalysisResult Analyze(
        Recording recording,
        IGameData gameData,
        PipelineOptions? options = null,
        Overrides? overrides = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken ct = default)
        => AnalyzeCore(recording, gameData, null, options, overrides, progress, ct);

    // Multi-recording merge: the concatenated op stream drives segmentation, catalog and model
    // building (identical to parsing one recording that contains all pulls), while sources[] lists
    // one SourceInfo per input recording in input order. Actor instance ids must be distinct
    // across recordings; see FORGE.md output for the caveat.
    public static AnalysisResult Analyze(
        IReadOnlyList<Recording> recordings,
        IGameData gameData,
        PipelineOptions? options = null,
        Overrides? overrides = null,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        if (recordings.Count == 0)
            throw new ArgumentException("At least one recording is required.", nameof(recordings));
        return AnalyzeCore(Recording.Concat(recordings), gameData, recordings, options, overrides, progress, ct);
    }

    private static AnalysisResult AnalyzeCore(
        Recording recording,
        IGameData gameData,
        IReadOnlyList<Recording>? sources,
        PipelineOptions? options,
        Overrides? overrides,
        IProgress<PipelineProgress>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(new PipelineProgress(PipelineStage.Segmenting, 0.15f));
        var pulls = PullSegmenter.Segment(recording, options?.Segmenter);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new PipelineProgress(PipelineStage.Classifying, 0.30f));
        var participants = ParticipantClassifier.Classify(recording, pulls, gameData);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new PipelineProgress(PipelineStage.Cataloging, 0.45f));
        var catalog = AbilityCataloger.Catalog(recording, pulls, participants, gameData, options?.Catalog);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new PipelineProgress(PipelineStage.Building, 0.60f));
        var inferred = EncounterModelBuilder.Build(recording, pulls, participants, catalog, gameData, options?.Build, sources);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new PipelineProgress(PipelineStage.ApplyingOverrides, 0.85f));
        var effective = ApplyOverrides(inferred, overrides);

        ct.ThrowIfCancellationRequested();
        progress?.Report(new PipelineProgress(PipelineStage.Done, 1.0f));
        return new AnalysisResult(recording, pulls, participants, catalog, inferred, effective);
    }

    public static AnalysisResult WithOverrides(AnalysisResult result, Overrides overrides)
        => result with { Effective = ApplyOverrides(result.Inferred, overrides) };

    public static EmitResult Emit(EncounterModel effective, Flavor flavor, string flavorsDirectory, EmitOptions? options = null)
        => ModuleEmitter.Emit(
            effective,
            ModuleEmitter.ProfileFor(flavor, flavorsDirectory),
            options ?? new EmitOptions(flavor, effective.Encounter.Namespace));

    private static EncounterModel ApplyOverrides(EncounterModel inferred, Overrides? overrides)
    {
        if (overrides is null || overrides.IsEmpty)
            return inferred;
        return OverridesApplier.Apply(inferred with { Overrides = overrides });
    }
}
