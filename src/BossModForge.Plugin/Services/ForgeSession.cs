using System.Text.Json;
using ECommons.DalamudServices;
using BossModForge.Core.Emit;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Core.Validation;

namespace BossModForge.Plugin.Services;

public sealed class ForgeSession
{
    private readonly Configuration _config;
    private readonly object _gate = new();
    private readonly List<string> _messages = [];
    private readonly HashSet<uint> _reviewedAids = [];

    public ForgeSession(Configuration config) => _config = config;

    public AnalysisResult? Result { get; private set; }
    public Overrides Overrides { get; private set; } = Overrides.Empty;
    public EncounterModel? Effective => Result?.Effective;
    public EmitResult? LastEmit { get; internal set; }
    public ValidationReport? LastValidation { get; internal set; }
    public string? LastOutputDir { get; internal set; }
    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
                return _messages.ToArray();
        }
    }
    public IReadOnlySet<uint> ReviewedAids => _reviewedAids;

    public void Accept(AnalysisResult r)
    {
        Result = r;
        Overrides = r.Effective.Overrides;
        LastEmit = null;
        LastValidation = null;
        Message($"Analyzed {Path.GetFileName(r.Recording.Meta.Path)}");
    }

    public void SetOverride(string key, JsonElement value)
    {
        Overrides = Overrides.With(key, value);
        if (Result is { } result)
            Result = ForgePipeline.WithOverrides(result, Overrides);
        PersistOverrides();
    }

    public void ClearOverride(string key)
    {
        Overrides = Overrides.Without(key);
        if (Result is { } result)
            Result = ForgePipeline.WithOverrides(result, Overrides);
        else
            Message("re-analyze to restore inferred value");
        PersistOverrides();
    }

    public void ClearAllOverrides()
    {
        Overrides = Overrides.Empty;
        if (Result is { } result)
            Result = ForgePipeline.WithOverrides(result, Overrides);
        PersistOverrides();
        _reviewedAids.Clear();
    }

    public void LoadEncounterModel(string emJsonPath)
        => Message($"Loading {emJsonPath} without a recording is out of scope for M5");

    public void MarkReviewed(uint aid) => _reviewedAids.Add(aid);

    public bool IsOverridden(string key) => Overrides.Values.ContainsKey(key);

    public void SetLastOutputDir(string dir) => LastOutputDir = dir;

    public void SetLastEmit(EmitResult emit) => LastEmit = emit;

    public void SetLastValidation(ValidationReport report) => LastValidation = report;

    public void Message(string text)
    {
        lock (_gate)
        {
            _messages.Add(text);
            while (_messages.Count > 20)
                _messages.RemoveAt(0);
        }
        Svc.Log.Info($"[BossModForge] {text}");
    }

    private void PersistOverrides()
    {
        var sha = CurrentSha256();
        if (string.IsNullOrEmpty(sha))
            return;
        lock (_config.OverridesBySha256)
        {
            if (Overrides.IsEmpty)
                _config.OverridesBySha256.Remove(sha);
            else
                _config.OverridesBySha256[sha] = OverrideJson.Serialize(Overrides);
        }
        _config.Save(Svc.PluginInterface);
    }

    private string CurrentSha256()
    {
        if (Result is { Inferred.Sources: { Length: > 0 } sources })
            return sources[0].Sha256;
        return "";
    }
}
