using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using BossModForge.Core.Replay;
using LuminaTerritoryType = Lumina.Excel.Sheets.TerritoryType;

namespace BossModForge.Plugin.Services;

/// <summary>After a long fight in a duty, shows a toast with how many recordings of that duty exist.</summary>
public sealed class RecordingCountToast : IDisposable
{
    private readonly Configuration _config;
    private readonly ReplayDiscovery _discovery;
    private bool _wasInCombat;
    private DateTime _combatStartUtc;

    public RecordingCountToast(Configuration config, ReplayDiscovery discovery)
    {
        _config = config;
        _discovery = discovery;
        Svc.Framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        var inCombat = Svc.Condition[ConditionFlag.InCombat];
        if (inCombat == _wasInCombat)
            return;
        _wasInCombat = inCombat;

        if (inCombat)
        {
            _combatStartUtc = DateTime.UtcNow;
            return;
        }

        if (!_config.ShowRecordingCountToast || !Svc.Condition[ConditionFlag.BoundByDuty])
            return;
        if ((DateTime.UtcNow - _combatStartUtc).TotalSeconds < _config.ToastMinFightSeconds)
            return;

        try
        {
            ShowToast();
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "Recording count toast failed");
        }
    }

    private void ShowToast()
    {
        var name = Svc.Data.GetExcelSheet<LuminaTerritoryType>()
            ?.GetRowOrDefault(Svc.ClientState.TerritoryType)
            ?.ContentFinderCondition.ValueNullable?.Name.ExtractText();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var count = RecordingNames.CountFor(_discovery.Scan().Select(f => f.Path), name);
        Svc.Toasts.ShowNormal($"{name}: {count} recording{(count == 1 ? "" : "s")}");
    }

    public void Dispose() => Svc.Framework.Update -= OnUpdate;
}
