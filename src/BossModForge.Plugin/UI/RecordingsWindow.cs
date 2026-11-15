using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Plugin.Services;

namespace BossModForge.Plugin.UI;

public sealed class RecordingsWindow : Window
{
    private readonly Configuration _config;
    private readonly ReplayDiscovery _discovery;
    private readonly AnalysisRunner _runner;
    private readonly ForgeSession _session;
    private readonly BossModIpc _ipc;
    private IReadOnlyList<ReplayFile> _files = [];
    private readonly Dictionary<string, RecordingSummary> _summaries = new(StringComparer.Ordinal);
    private int _selected = -1;

    public RecordingsWindow(
        Configuration config,
        ReplayDiscovery discovery,
        AnalysisRunner runner,
        ForgeSession session,
        BossModIpc ipc)
        : base("BossModForge — Recordings###ForgeRecordings")
    {
        _config = config;
        _discovery = discovery;
        _runner = runner;
        _session = session;
        _ipc = ipc;
        RespectCloseHotkey = true;
        SizeConstraints = Ui.SizeConstraints;
        Size = new Vector2(900, 520);
    }

    public override void OnOpen()
    {
        _ipc.Probe();
        _files = _discovery.Scan();
        CacheFromSession();
    }

    public override void Draw()
    {
        Ui.DrawSessionBar(_session, _runner);
        DrawContent();
    }

    public void DrawContent()
    {
        CacheFromSession();
        ImGui.Text(_ipc.Available ? "BossMod IPC: available" : "BossMod IPC: not found");
        ImGui.TextDisabled("Recording is controlled in BossMod's own settings (/vbm → Replays); Forge does not record.");

        foreach (var dir in _discovery.Directories)
        {
            var exists = Directory.Exists(dir);
            ImGui.Text(exists ? dir : $"{dir} (missing)");
        }

        if (ImGui.Button("Rescan"))
        {
            _ipc.Probe();
            _files = _discovery.Scan();
        }

        ImGui.SameLine();
        var canAnalyze = _selected >= 0 && _selected < _files.Count && !_runner.IsBusy;
        if (ImGui.Button("Analyze") && canAnalyze)
        {
            var file = _files[_selected];
            var same = string.Equals(_runner.Snapshot.SourcePath, file.Path, StringComparison.Ordinal)
                       || string.Equals(_session.Result?.Recording.Meta.Path, file.Path, StringComparison.Ordinal);
            var carry = same ? _session.Overrides : Overrides.Empty;
            _runner.Start(file.Path, _config.ToPipelineOptions(), carry);
        }
        if (!canAnalyze && ImGui.IsItemHovered())
            ImGui.SetTooltip(_runner.IsBusy ? "Analysis running" : "Select a recording. Merging recordings arrives in M6");

        if (ImGui.BeginTable("replays", 9, ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("File");
            ImGui.TableSetupColumn("Size");
            ImGui.TableSetupColumn("Modified");
            ImGui.TableSetupColumn("Version");
            ImGui.TableSetupColumn("Game ver");
            ImGui.TableSetupColumn("Zone/CFC");
            ImGui.TableSetupColumn("Boss");
            ImGui.TableSetupColumn("Pulls");
            ImGui.TableSetupColumn("Duration");
            ImGui.TableHeadersRow();

            for (var i = 0; i < _files.Count; i++)
            {
                var file = _files[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var selected = i == _selected;
                if (ImGui.Selectable(Path.GetFileName(file.Path), selected, ImGuiSelectableFlags.SpanAllColumns))
                    _selected = i;
                ImGui.TableNextColumn();
                ImGui.Text($"{file.SizeBytes / 1024f:0} KB");
                ImGui.TableNextColumn();
                ImGui.Text(file.LastWriteUtc.ToLocalTime().ToString("g"));
                _summaries.TryGetValue(file.Path, out var summary);
                ImGui.TableNextColumn();
                ImGui.Text(summary?.Version?.ToString() ?? "—");
                ImGui.TableNextColumn();
                ImGui.Text(summary?.GameVersion ?? "—");
                ImGui.TableNextColumn();
                ImGui.Text(summary is null ? "—" : $"{summary.Zone}/{summary.CfcId}");
                ImGui.TableNextColumn();
                ImGui.Text(summary?.Boss ?? "—");
                ImGui.TableNextColumn();
                ImGui.Text(summary?.Pulls?.ToString() ?? "—");
                ImGui.TableNextColumn();
                ImGui.Text(summary?.DurationSeconds is { } d ? $"{d:0.0}s" : "—");
            }

            ImGui.EndTable();
        }
    }

    private void CacheFromSession()
    {
        if (_session.Result is not { } r)
            return;
        var path = r.Recording.Meta.Path;
        var enc = r.Effective.Encounter;
        _summaries[path] = new RecordingSummary(
            r.Recording.Meta.Version,
            r.Recording.Meta.GameVersion,
            enc.Zone,
            enc.CfcId,
            enc.BossName,
            r.Effective.Pulls.Length,
            (r.Recording.End - r.Recording.Start).TotalSeconds);
    }

    private sealed record RecordingSummary(
        int? Version,
        string? GameVersion,
        ushort? Zone,
        ushort? CfcId,
        string? Boss,
        int? Pulls,
        double? DurationSeconds);
}
