using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using ECommons.DalamudServices;
using BossModForge.Plugin.Services;
using BossModForge.Plugin.UI;

namespace BossModForge.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "BossModForge"; // VERIFY-4: manifest DalamudApiLevel must match TankCooldownHelper.json (15)

    private readonly WindowSystem _windowSystem;
    private readonly MainWindow _mainWindow;
    private readonly RecordingsWindow _recordings;
    private readonly EncounterWindow _encounter;
    private readonly AbilitiesWindow _abilities;
    private readonly TimelineWindow _timeline;
    private readonly ExportWindow _export;
    private readonly SettingsWindow _settings;
    private readonly AnalysisRunner _runner;
    private readonly ForgeSession _session;
    private readonly RecordingCountToast _countToast;
    private readonly Action _openMain;
    private readonly Action _openConfig;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this);

        var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var gameData = new DalamudGameData(Svc.Data);
        var discovery = new ReplayDiscovery(config);
        _runner = new AnalysisRunner(gameData, config);
        _session = new ForgeSession(config);
        var ipc = new BossModIpc();
        _countToast = new RecordingCountToast(config, discovery);
        var exporter = new Exporter(config, _session);

        _runner.Completed += OnRunnerCompleted;

        _windowSystem = new WindowSystem("BossModForge");
        _recordings = new RecordingsWindow(config, discovery, _runner, _session, ipc);
        _encounter = new EncounterWindow(_session, _runner);
        _abilities = new AbilitiesWindow(_session, _runner);
        _timeline = new TimelineWindow(_session, _runner);
        _export = new ExportWindow(config, _session, _runner, exporter);
        _settings = new SettingsWindow(config, _session, _runner);
        _mainWindow = new MainWindow(_runner, _session, _recordings, _encounter, _abilities, _timeline, _export, _settings);

        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_recordings);
        _windowSystem.AddWindow(_encounter);
        _windowSystem.AddWindow(_abilities);
        _windowSystem.AddWindow(_timeline);
        _windowSystem.AddWindow(_export);
        _windowSystem.AddWindow(_settings);

        pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _openMain = () => _mainWindow.IsOpen = true;
        _openConfig = () => _settings.IsOpen = true;
        pluginInterface.UiBuilder.OpenMainUi += _openMain;
        pluginInterface.UiBuilder.OpenConfigUi += _openConfig;

        Svc.Commands.AddHandler("/forge", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open BossModForge (/forge recordings|encounter|abilities|timeline|export|settings)",
            ShowInHelp = true,
        });
    }

    private void OnRunnerCompleted(RunnerSnapshot snapshot)
    {
        if (snapshot.State == RunnerState.Ready && snapshot.Result is { } result)
            _session.Accept(result);
    }

    private void OnCommand(string command, string args)
    {
        var key = args.Trim().ToLowerInvariant();
        switch (key)
        {
            case "":
                _mainWindow.IsOpen = !_mainWindow.IsOpen;
                break;
            case "recordings":
                _recordings.IsOpen = true;
                break;
            case "encounter":
                _encounter.IsOpen = true;
                break;
            case "abilities":
                _abilities.IsOpen = true;
                break;
            case "timeline":
                _timeline.IsOpen = true;
                break;
            case "export":
                _export.IsOpen = true;
                break;
            case "settings":
            case "config":
                _settings.IsOpen = true;
                break;
            default:
                Svc.Log.Info("BossModForge: /forge recordings|encounter|abilities|timeline|export|settings");
                _mainWindow.IsOpen = true;
                break;
        }
    }

    public void Dispose()
    {
        Svc.Commands.RemoveHandler("/forge");
        Svc.PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= _openMain;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= _openConfig;
        _runner.Completed -= OnRunnerCompleted;
        _windowSystem.RemoveAllWindows();
        _countToast.Dispose();
        _runner.Dispose();
        ECommonsMain.Dispose();
    }
}
