using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ECommons.DalamudServices;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Core.Replay;

namespace BossModForge.Plugin.Services;

public enum RunnerState
{
    Idle,
    Parsing,
    FetchingGameData,
    Inferring,
    Ready,
    Failed,
    Cancelled,
}

public sealed record RunnerSnapshot(
    RunnerState State,
    float Progress,
    string StageText,
    string? Error,
    AnalysisResult? Result,
    string? SourcePath,
    DateTime StartedUtc,
    DateTime? FinishedUtc);

public sealed class AnalysisRunner : IDisposable
{
    private readonly DalamudGameData _gameData;
    private readonly Configuration _config;
    private RunnerSnapshot _snapshot = new(RunnerState.Idle, 0, "Idle", null, null, null, DateTime.MinValue, null);
    private CancellationTokenSource? _cts;
    private int _runId;

    public AnalysisRunner(DalamudGameData gameData, Configuration config)
    {
        _gameData = gameData;
        _config = config;
    }

    public RunnerSnapshot Snapshot => Volatile.Read(ref _snapshot!);

    public bool IsBusy
    {
        get
        {
            var state = Snapshot.State;
            return state is RunnerState.Parsing or RunnerState.FetchingGameData or RunnerState.Inferring;
        }
    }

    public event Action<RunnerSnapshot>? Completed;

    public void Start(string path, PipelineOptions options, Overrides carryOver)
    {
        if (IsBusy)
            throw new InvalidOperationException("Analysis is already running.");

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var runId = Interlocked.Increment(ref _runId);
        var started = DateTime.UtcNow;
        var sizeMb = SafeFileLength(path) / (1024f * 1024f);
        Publish(new RunnerSnapshot(RunnerState.Parsing, 0, $"Parsing {sizeMb:0} MB…", null, null, path, started, null));
        var ct = _cts.Token;
        _ = Task.Run(() => RunAsync(runId, path, options, carryOver, started, ct), ct);
    }

    public void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(
        int runId,
        string path,
        PipelineOptions options,
        Overrides carryOver,
        DateTime started,
        CancellationToken ct)
    {
        try
        {
            var sha = FileSha256(path);
            var overrides = carryOver.IsEmpty ? LoadStoredOverrides(sha) : carryOver;

            var lastParsePublish = 0L;
            var parseProgress = new Progress<float>(fraction =>
            {
                if (Environment.TickCount64 - lastParsePublish < 50)
                    return;
                lastParsePublish = Environment.TickCount64;
                if (!StillCurrent(runId))
                    return;
                Publish(new RunnerSnapshot(
                    RunnerState.Parsing,
                    Math.Clamp(fraction, 0, 1) * 0.15f,
                    $"Parsing {fraction:P0}…",
                    null,
                    null,
                    path,
                    started,
                    null));
            });
            var parseOptions = new ParseOptions(parseProgress, KeepRawOps: false);
            var parsePipeline = options with { Parse = parseOptions };

            ct.ThrowIfCancellationRequested();
            var recording = ForgePipeline.Parse(path, parsePipeline, ct);
            ct.ThrowIfCancellationRequested();
            var keys = ForgePipeline.CollectKeys(recording);

            if (!StillCurrent(runId))
                return;
            Publish(new RunnerSnapshot(
                RunnerState.FetchingGameData,
                0.15f,
                $"Fetching {keys.ActionIds.Count} action rows",
                null,
                null,
                path,
                started,
                null));

            var cache = await SnapshotOnFramework(keys, ct).ConfigureAwait(false);

            if (!StillCurrent(runId))
                return;
            Publish(new RunnerSnapshot(RunnerState.Inferring, 0.15f, "Inferring", null, null, path, started, null));

            var inferProgress = new Progress<PipelineProgress>(p =>
            {
                if (!StillCurrent(runId))
                    return;
                Publish(new RunnerSnapshot(
                    RunnerState.Inferring,
                    p.Fraction,
                    $"Inferring: {p.Stage}",
                    null,
                    null,
                    path,
                    started,
                    null));
            });

            var result = ForgePipeline.Analyze(recording, cache, options, overrides, inferProgress, ct);
            if (!StillCurrent(runId))
                return;
            var ready = new RunnerSnapshot(RunnerState.Ready, 1f, "Ready", null, result, path, started, DateTime.UtcNow);
            Publish(ready);
            await RaiseCompleted(ready).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!StillCurrent(runId))
                return;
            var cancelled = new RunnerSnapshot(RunnerState.Cancelled, Snapshot.Progress, "Cancelled", null, null, path, started, DateTime.UtcNow);
            Publish(cancelled);
            await RaiseCompleted(cancelled).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "BossModForge analysis failed");
            if (!StillCurrent(runId))
                return;
            var failed = new RunnerSnapshot(RunnerState.Failed, Snapshot.Progress, "Failed", ex.Message, null, path, started, DateTime.UtcNow);
            Publish(failed);
            await RaiseCompleted(failed).ConfigureAwait(false);
        }
    }

    private async Task<Core.GameData.CachedGameData> SnapshotOnFramework(Core.GameData.GameDataKeys keys, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        var cache = await RunOnFramework(() => _gameData.Snapshot(keys)).ConfigureAwait(false);
        sw.Stop();
        if (sw.Elapsed.TotalMilliseconds > 8)
            Svc.Log.Warning($"DalamudGameData.Snapshot took {sw.Elapsed.TotalMilliseconds:0.0} ms (budget 8 ms)");
        return cache;
    }

    private async Task RaiseCompleted(RunnerSnapshot snapshot)
    {
        await RunOnFramework(() =>
        {
            Completed?.Invoke(snapshot);
            return 0;
        }).ConfigureAwait(false);
    }

    // VERIFY-6: IFramework.RunOnFrameworkThread<T>(Func<T>) generic/async shape.
    private static Task<T> RunOnFramework<T>(Func<T> func) => Svc.Framework.RunOnFrameworkThread(func);

    private bool StillCurrent(int runId) => Volatile.Read(ref _runId) == runId;

    private void Publish(RunnerSnapshot snapshot) => Interlocked.Exchange(ref _snapshot, snapshot);

    private Overrides LoadStoredOverrides(string sha256)
    {
        if (string.IsNullOrEmpty(sha256))
            return Overrides.Empty;
        string? json;
        lock (_config.OverridesBySha256)
            _config.OverridesBySha256.TryGetValue(sha256, out json);
        if (string.IsNullOrWhiteSpace(json))
            return Overrides.Empty;
        return OverrideJson.Parse(json);
    }

    internal static string FileSha256(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "";
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static long SafeFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}

internal static class OverrideJson
{
    public static string Serialize(Overrides overrides)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var kv in overrides.Values)
            {
                writer.WritePropertyName(kv.Key);
                kv.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static Overrides Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var current = Overrides.Empty;
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return current;
        foreach (var prop in doc.RootElement.EnumerateObject())
            current = current.With(prop.Name, prop.Value.Clone());
        return current;
    }
}
