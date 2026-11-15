using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using BossModForge.Plugin.Services;

namespace BossModForge.Plugin.UI;

public sealed class SettingsWindow : Window
{
    private static readonly string[] Flavors = ["vbm", "bmr"];

    private readonly Configuration _config;
    private readonly ForgeSession _session;
    private readonly AnalysisRunner _runner;
    private string _newDir = "";
    private string _output = "";
    private string _ns = "";
    private string _contributors = "";
    private int _flavorIdx;

    public SettingsWindow(Configuration config, ForgeSession session, AnalysisRunner runner)
        : base("BossModForge — Settings###ForgeSettings")
    {
        _config = config;
        _session = session;
        _runner = runner;
        RespectCloseHotkey = true;
        SizeConstraints = Ui.SizeConstraints;
        Size = new Vector2(640, 560);
    }

    public override void Draw()
    {
        Ui.DrawSessionBar(_session, _runner);
        DrawContent();
    }

    public void DrawContent()
    {
        ImGui.Text("Replay directories (empty list = BossMod / BossModReborn defaults)");
        for (var i = _config.ReplayDirectories.Count - 1; i >= 0; i--)
        {
            ImGui.Text(_config.ReplayDirectories[i]);
            ImGui.SameLine();
            if (ImGui.Button($"Remove##{i}"))
            {
                _config.ReplayDirectories.RemoveAt(i);
                Save();
            }
        }

        Ui.InputText("Add directory", ref _newDir);
        ImGui.SameLine();
        if (ImGui.Button("Add") && _newDir.Length > 0)
        {
            _config.ReplayDirectories.Add(_newDir);
            _newDir = "";
            Save();
        }

        if (ImGui.Button("Reset to defaults"))
        {
            _config.ReplayDirectories.Clear();
            Save();
        }

        _output = _config.OutputDirectory ?? "";
        if (Ui.InputText("Output directory", ref _output))
        {
            _config.OutputDirectory = string.IsNullOrWhiteSpace(_output) ? null : _output;
            Save();
        }

        _flavorIdx = _config.Flavor.Equals("bmr", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (Ui.Combo("Default flavor", ref _flavorIdx, Flavors))
        {
            _config.Flavor = Flavors[_flavorIdx];
            Save();
        }

        _ns = _config.DefaultNamespace ?? "";
        if (Ui.InputText("Default namespace", ref _ns))
        {
            _config.DefaultNamespace = string.IsNullOrWhiteSpace(_ns) ? null : _ns;
            Save();
        }

        var maturity = _config.Maturity;
        if (Ui.InputText("Maturity", ref maturity))
        {
            _config.Maturity = maturity;
            Save();
        }

        _contributors = _config.Contributors ?? "";
        if (Ui.InputText("Contributors", ref _contributors))
        {
            _config.Contributors = string.IsNullOrWhiteSpace(_contributors) ? null : _contributors;
            Save();
        }

        var open = _config.OpenFolderAfterExport;
        if (ImGui.Checkbox("Open folder after export", ref open))
        {
            _config.OpenFolderAfterExport = open;
            Save();
        }

        var toast = _config.ShowRecordingCountToast;
        if (ImGui.Checkbox("Toast recording count after a fight", ref toast))
        {
            _config.ShowRecordingCountToast = toast;
            Save();
        }

        if (toast)
        {
            var secs = _config.ToastMinFightSeconds;
            if (ImGui.InputFloat("Only for fights longer than (s)", ref secs))
            {
                _config.ToastMinFightSeconds = Math.Max(0, secs);
                Save();
            }
        }

        if (ImGui.CollapsingHeader("Advanced"))
        {
            _config.AdvancedThresholds = true;
            ImGui.TextDisabled("takes effect on next Analyze");
            NullableFloat("Min pull duration (s)", () => _config.SegmenterMinPullDurationSeconds, v => _config.SegmenterMinPullDurationSeconds = v, 10);
            NullableFloat("Combat gap (s)", () => _config.SegmenterCombatGapSeconds, v => _config.SegmenterCombatGapSeconds = v, 8);
            NullableFloat("Lag min", () => _config.ShapeLagMinSeconds, v => _config.ShapeLagMinSeconds = v, -0.2f);
            NullableFloat("Lag max", () => _config.ShapeLagMaxSeconds, v => _config.ShapeLagMaxSeconds = v, 1);
            NullableFloat("Lag step", () => _config.ShapeLagStepSeconds, v => _config.ShapeLagStepSeconds = v, 0.1f);
            NullableDouble("Prior weight", () => _config.ShapePriorWeight, v => _config.ShapePriorWeight = v, 0.35);
            NullableDouble("Fit weight", () => _config.ShapeFitWeight, v => _config.ShapeFitWeight = v, 0.55);
            NullableDouble("Ambiguity weight", () => _config.ShapeAmbiguityWeight, v => _config.ShapeAmbiguityWeight = v, 0.10);
            NullableFloat("Ambiguity margin y", () => _config.ShapeAmbiguityMarginY, v => _config.ShapeAmbiguityMarginY = v, 1);
            NullableFloat("Ambiguity margin deg", () => _config.ShapeAmbiguityMarginDeg, v => _config.ShapeAmbiguityMarginDeg = v, 3);
            NullableDouble("Raidwide fraction", () => _config.ShapeRaidwideFraction, v => _config.ShapeRaidwideFraction = v, 0.9);
            NullableFloat("Raidwide arena ratio", () => _config.ShapeRaidwideArenaRatio, v => _config.ShapeRaidwideArenaRatio = v, 0.95f);
            NullableFloat("Radius snap y", () => _config.ShapeRadiusSnapY, v => _config.ShapeRadiusSnapY = v, 0.5f);
            NullableFloat("Angle snap deg", () => _config.ShapeAngleSnapDeg, v => _config.ShapeAngleSnapDeg = v, 3);
            NullableFloat("Bounds radius snap", () => _config.BoundsRadiusSnapTolerance, v => _config.BoundsRadiusSnapTolerance = v, 1);
            NullableFloat("Bounds rect padding", () => _config.BoundsRectPadding, v => _config.BoundsRectPadding = v, 1);
            NullableDouble("Circle fill min", () => _config.BoundsCircleFillMin, v => _config.BoundsCircleFillMin = v, 0.7);
            NullableDouble("Box fill min", () => _config.BoundsBoxFillMin, v => _config.BoundsBoxFillMin = v, 0.8);
            NullableFloat("Polygon epsilon", () => _config.BoundsPolygonEpsilon, v => _config.BoundsPolygonEpsilon = v, 0.5f);
        }

        if (ImGui.Button("Clear stored overrides"))
            ImGui.OpenPopup("clear-overrides");
        if (ImGui.BeginPopupModal("clear-overrides"))
        {
            ImGui.Text("Wipe all OverridesBySha256?");
            if (ImGui.Button("Yes"))
            {
                lock (_config.OverridesBySha256)
                    _config.OverridesBySha256.Clear();
                _session.ClearAllOverrides();
                Save();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("No"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void NullableFloat(string label, Func<float?> get, Action<float?> set, float fallback)
    {
        var v = get() ?? fallback;
        var overridden = get().HasValue;
        if (ImGui.InputFloat(label, ref v))
        {
            set(v);
            Save();
            _session.Message("takes effect on next Analyze");
        }
        ImGui.SameLine();
        if (overridden && ImGui.Button($"reset##{label}"))
        {
            set(null);
            Save();
        }
    }

    private void NullableDouble(string label, Func<double?> get, Action<double?> set, double fallback)
    {
        var v = (float)(get() ?? fallback);
        var overridden = get().HasValue;
        if (ImGui.InputFloat(label, ref v))
        {
            set(v);
            Save();
            _session.Message("takes effect on next Analyze");
        }
        ImGui.SameLine();
        if (overridden && ImGui.Button($"reset##{label}"))
        {
            set(null);
            Save();
        }
    }

    private void Save() => _config.Save(Svc.PluginInterface);
}
