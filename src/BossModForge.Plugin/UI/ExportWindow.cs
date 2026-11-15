using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using BossModForge.Core.Emit;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Core.Pipeline;
using BossModForge.Core.Validation;
using BossModForge.Plugin.Services;

namespace BossModForge.Plugin.UI;

public sealed class ExportWindow : Window
{
    private static readonly string[] Flavors = ["vbm", "bmr"];
    private static readonly string[] Maturities = ["WIP", "Contributed", "Verified"];

    private readonly Configuration _config;
    private readonly ForgeSession _session;
    private readonly AnalysisRunner _runner;
    private readonly Exporter _exporter;
    private string _ns = "";
    private string _module = "";
    private string _contributors = "";
    private int _flavorIdx;
    private int _maturityIdx;
    private bool _copyEm;
    private string _outDir = "";
    private string? _syncedFor;

    public ExportWindow(Configuration config, ForgeSession session, AnalysisRunner runner, Exporter exporter)
        : base("BossModForge — Export###ForgeExport")
    {
        _config = config;
        _session = session;
        _runner = runner;
        _exporter = exporter;
        RespectCloseHotkey = true;
        SizeConstraints = Ui.SizeConstraints;
        Size = new Vector2(780, 640);
    }

    public override void Draw()
    {
        Ui.DrawSessionBar(_session, _runner);
        DrawContent();
    }

    public void DrawContent()
    {
        var em = _session.Effective;
        var analysis = _session.Result;
        if (em is null || analysis is null)
        {
            ImGui.TextDisabled("Analyze a recording first.");
            return;
        }

        if (!_exporter.FlavorsPresent())
            ImGui.TextColored(Ui.Band(ConfidenceBand.Low), "flavors/vbm.json missing next to the plugin DLL — Emit is disabled.");

        SyncFields(em);

        if (_flavorIdx == 1)
            ImGui.TextColored(Ui.Band(ConfidenceBand.Medium), "BMR flavor is unverified (flavors/bmr.json verified: false).");

        if (Ui.Combo("Flavor", ref _flavorIdx, Flavors))
        {
            _config.Flavor = Flavors[_flavorIdx];
            _config.Save(Svc.PluginInterface);
        }

        if (Ui.InputTextEnter("Namespace", ref _ns))
            _session.SetOverride(OverridesApplier.NamespaceKey, OverridesApplier.ToElement(_ns));
        if (Ui.InputTextEnter("Class name", ref _module))
            _session.SetOverride(OverridesApplier.ModuleNameKey, OverridesApplier.ToElement(_module));
        Ui.Combo("Maturity", ref _maturityIdx, Maturities);
        Ui.InputText("Contributors", ref _contributors);
        Ui.InputText("Output dir", ref _outDir);
        if (ImGui.Button("Use this output dir"))
        {
            _config.OutputDirectory = _outDir;
            _config.Save(Svc.PluginInterface);
        }

        var busy = _exporter.IsBusy;
        var flavor = _flavorIdx == 1 ? Flavor.Bmr : Flavor.Vbm;
        var maturity = Maturities[Math.Clamp(_maturityIdx, 0, Maturities.Length - 1)];
        var options = new EmitOptions(flavor, _ns, maturity, string.IsNullOrWhiteSpace(_contributors) ? null : _contributors);

        DrawButton("Write EM JSON", busy, () => _exporter.WriteEm(em));
        ImGui.SameLine();
        var emitDisabled = busy || !_exporter.FlavorsPresent();
        DrawButton("Emit C#", emitDisabled, () => _exporter.EmitCsharp(em, flavor, options));
        ImGui.SameLine();
        ImGui.Checkbox("copy EM JSON", ref _copyEm);
        ImGui.SameLine();
        if (ImGui.Button("Copy to clipboard"))
        {
            if (_copyEm)
                ImGui.SetClipboardText(EncounterModelJson.Serialize(em));
            else if (_session.LastEmit is { } emit)
                ImGui.SetClipboardText(Exporter.ConcatenateCsharp(emit));
            else
                _session.Message("Emit C# first, or enable copy EM JSON");
        }

        DrawButton("Open output folder", busy, () =>
        {
            var dir = _session.LastOutputDir ?? _exporter.ModuleOutputDir(em);
            Directory.CreateDirectory(dir);
            Exporter.OpenFolder(dir);
        });
        ImGui.SameLine();
        DrawButton("Validate", busy, () => _exporter.Validate(em, analysis));
        ImGui.SameLine();
        DrawButton("Write report", busy || _session.LastValidation is null, () =>
        {
            if (_session.LastValidation is { } report)
                _exporter.WriteReport(report, _exporter.ModuleOutputDir(em));
        });
        ImGui.SameLine();
        if (ImGui.Button("Apply downgrades") && _session.LastValidation is { } v && _session.Result is { } r)
        {
            var next = DryValidator.ApplyDowngrades(em, v);
            _session.Accept(r with { Effective = next });
        }

        ImGui.Separator();
        ImGui.Text("Messages");
        foreach (var msg in _session.Messages)
            ImGui.TextDisabled(msg);

        if (_session.LastValidation is { } report)
            DrawReport(report);
    }

    private void DrawReport(ValidationReport report)
    {
        ImGui.Separator();
        ImGui.Text(report.Passed ? "Passed" : "Failed");
        ImGui.Text($"Timing P50={report.Timing.P50:0.00} P90={report.Timing.P90:0.00} Max={report.Timing.Max:0.00} missed={report.Timing.Missed}");
        ImGui.Text($"Coverage {report.Coverage.Covered}/{report.Coverage.DamagingCasts} ({report.Coverage.Fraction:P0})");
        if (ImGui.BeginTable("aoe", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("AID");
            ImGui.TableSetupColumn("Inst");
            ImGui.TableSetupColumn("Prec");
            ImGui.TableSetupColumn("Recall");
            ImGui.TableSetupColumn("Pass");
            ImGui.TableSetupColumn("Component");
            ImGui.TableHeadersRow();
            foreach (var aoe in report.Aoe)
            {
                ImGui.TableNextRow();
                var bad = aoe.Precision < 0.95 || aoe.Recall < 0.95;
                var col = bad ? Ui.Band(ConfidenceBand.Low) : new Vector4(1, 1, 1, 1);
                ImGui.TableNextColumn();
                ImGui.TextColored(col, aoe.Aid.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.Text(aoe.Instances.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.TextColored(col, aoe.Precision.ToString("0.00"));
                ImGui.TableNextColumn();
                ImGui.TextColored(col, aoe.Recall.ToString("0.00"));
                ImGui.TableNextColumn();
                ImGui.Text(aoe.Passed ? "yes" : "no");
                ImGui.TableNextColumn();
                ImGui.Text(aoe.Component);
            }

            ImGui.EndTable();
        }

        foreach (var f in report.Failures)
            ImGui.TextColored(Ui.Band(ConfidenceBand.Low), f);
        foreach (var r in report.SuggestedReview)
            ImGui.TextDisabled($"{r.Severity} {r.Subject}: {r.Question}");
    }

    private void SyncFields(EncounterModel em)
    {
        var key = em.Encounter.ModuleName + "|" + em.Encounter.Namespace;
        if (_syncedFor == key)
            return;
        _syncedFor = key;
        _ns = em.Encounter.Namespace;
        _module = em.Encounter.ModuleName;
        _contributors = _config.Contributors ?? "";
        _outDir = _exporter.OutputRoot;
        _flavorIdx = _config.Flavor.Equals("bmr", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _maturityIdx = Math.Max(0, Array.IndexOf(Maturities, _config.Maturity));
    }

    private static void DrawButton(string label, bool disabled, Action action)
    {
        if (disabled)
            ImGui.TextDisabled(label);
        else if (ImGui.Button(label))
            action();
    }
}
