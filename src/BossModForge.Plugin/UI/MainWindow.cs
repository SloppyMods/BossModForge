using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using BossModForge.Plugin.Services;

namespace BossModForge.Plugin.UI;

public sealed class MainWindow : Window
{
    private readonly AnalysisRunner _runner;
    private readonly ForgeSession _session;
    private readonly RecordingsWindow _recordings;
    private readonly EncounterWindow _encounter;
    private readonly AbilitiesWindow _abilities;
    private readonly TimelineWindow _timeline;
    private readonly ExportWindow _export;
    private readonly SettingsWindow _settings;

    public MainWindow(
        AnalysisRunner runner,
        ForgeSession session,
        RecordingsWindow recordings,
        EncounterWindow encounter,
        AbilitiesWindow abilities,
        TimelineWindow timeline,
        ExportWindow export,
        SettingsWindow settings)
        : base("BossModForge###ForgeMain")
    {
        _runner = runner;
        _session = session;
        _recordings = recordings;
        _encounter = encounter;
        _abilities = abilities;
        _timeline = timeline;
        _export = export;
        _settings = settings;
        RespectCloseHotkey = true;
        SizeConstraints = Ui.SizeConstraints;
        Size = new Vector2(1100, 680);
    }

    public override void OnOpen() => _recordings.OnOpen();

    public override void Draw()
    {
        Ui.DrawSessionBar(_session, _runner);
        if (!ImGui.BeginTabBar("ForgeTabs"))
            return;
        if (ImGui.BeginTabItem("Recordings"))
        {
            _recordings.DrawContent();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Encounter"))
        {
            _encounter.DrawContent();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Abilities"))
        {
            _abilities.DrawContent();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Timeline"))
        {
            _timeline.DrawContent();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Export"))
        {
            _export.DrawContent();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Settings"))
        {
            _settings.DrawContent();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }
}
