using ECommons.DalamudServices;

namespace BossModForge.Plugin.Services;

public sealed class BossModIpc
{
    public bool Available { get; private set; }

    public bool Probe()
    {
        try
        {
            // VERIFY-10: BossModReborn may publish the same IPC name; this is a presence probe only.
            InvokeHasModule(0);
            Available = true;
        }
        catch
        {
            Available = false;
        }

        return Available;
    }

    private static bool InvokeHasModule(uint dataId)
    {
        return Svc.PluginInterface
            .GetIpcSubscriber<uint, bool>("BossMod.HasModuleByDataId")
            .InvokeFunc(dataId);
    }
}
