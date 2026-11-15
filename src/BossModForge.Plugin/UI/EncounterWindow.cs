using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using BossModForge.Core.Model;
using BossModForge.Plugin.Services;

namespace BossModForge.Plugin.UI;

public sealed class EncounterWindow : Window
{
    private readonly ForgeSession _session;
    private readonly AnalysisRunner _runner;
    private string _moduleName = "";
    private string _ns = "";
    private float _arenaCx, _arenaCz, _arenaR, _arenaHw, _arenaHh;
    private string _arenaKind = "Circle";

    public EncounterWindow(ForgeSession session, AnalysisRunner runner)
        : base("BossModForge — Encounter###ForgeEncounter")
    {
        _session = session;
        _runner = runner;
        RespectCloseHotkey = true;
        SizeConstraints = Ui.SizeConstraints;
        Size = new Vector2(820, 560);
    }

    public override void Draw()
    {
        Ui.DrawSessionBar(_session, _runner);
        DrawContent();
    }

    public void DrawContent()
    {
        var em = _session.Effective;
        if (em is null)
        {
            ImGui.TextDisabled("Analyze a recording first.");
            return;
        }

        var enc = em.Encounter;
        ImGui.Text($"Boss: {enc.BossName}");
        ImGui.Text($"Duty: {enc.DutyName ?? "—"}");
        ImGui.Text($"Zone {enc.Zone}  CFC {enc.CfcId}  Primary OID 0x{enc.PrimaryOid:X}");

        if (_moduleName.Length == 0)
            _moduleName = enc.ModuleName;
        if (_ns.Length == 0)
            _ns = enc.Namespace;
        if (Ui.InputTextEnter("Module name", ref _moduleName))
            _session.SetOverride(OverridesApplier.ModuleNameKey, OverridesApplier.ToElement(_moduleName));
        if (_session.IsOverridden(OverridesApplier.ModuleNameKey))
        {
            ImGui.SameLine();
            ImGui.Text("✎");
            ImGui.SameLine();
            if (ImGui.Button("reset##mod"))
                _session.ClearOverride(OverridesApplier.ModuleNameKey);
        }

        if (Ui.InputTextEnter("Namespace", ref _ns))
            _session.SetOverride(OverridesApplier.NamespaceKey, OverridesApplier.ToElement(_ns));
        if (_session.IsOverridden(OverridesApplier.NamespaceKey))
        {
            ImGui.SameLine();
            ImGui.Text("✎");
            ImGui.SameLine();
            if (ImGui.Button("reset##ns"))
                _session.ClearOverride(OverridesApplier.NamespaceKey);
        }

        ImGui.Separator();
        ImGui.Text("Sources");
        foreach (var src in em.Sources)
            ImGui.TextDisabled($"{src.Path}  sha={src.Sha256}  v{src.Version}  {src.GameVersion}  {src.Start:u}–{src.End:u}");

        ImGui.Separator();
        if (ImGui.BeginTable("pulls", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Pull");
            ImGui.TableSetupColumn("Start");
            ImGui.TableSetupColumn("Duration");
            ImGui.TableSetupColumn("Outcome");
            ImGui.TableSetupColumn("Confidence");
            ImGui.TableHeadersRow();
            foreach (var pull in em.Pulls)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(pull.Index.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.Text(pull.Start.ToString("HH:mm:ss"));
                ImGui.TableNextColumn();
                ImGui.Text($"{pull.DurationSeconds:0.0}s");
                ImGui.TableNextColumn();
                ImGui.Text(pull.Outcome);
                ImGui.TableNextColumn();
                Ui.ConfidenceBadge(pull.Confidence);
            }

            ImGui.EndTable();
        }

        ImGui.Separator();
        if (ImGui.BeginTable("parts", 10, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("OID");
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("NameId");
            ImGui.TableSetupColumn("Type");
            ImGui.TableSetupColumn("Role");
            ImGui.TableSetupColumn("Hitbox");
            ImGui.TableSetupColumn("Inst");
            ImGui.TableSetupColumn("Mid");
            ImGui.TableSetupColumn("Conf");
            ImGui.TableSetupColumn("Enum");
            ImGui.TableHeadersRow();
            foreach (var p in em.Participants)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"0x{p.Oid:X}");
                ImGui.TableNextColumn();
                ImGui.Text(p.Name);
                ImGui.TableNextColumn();
                ImGui.Text(p.NameId.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.Text(p.Type);
                ImGui.TableNextColumn();
                ImGui.Text(p.Role);
                ImGui.TableNextColumn();
                ImGui.Text(p.HitboxRadius.ToString("0.##"));
                ImGui.TableNextColumn();
                ImGui.Text(p.InstanceCount.ToString(CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                ImGui.Text(p.SpawnsDuringFight ? "yes" : "");
                ImGui.TableNextColumn();
                Ui.ConfidenceBadge(p.Confidence);
                ImGui.TableNextColumn();
                var key = OverridesApplier.ParticipantKey(p.Oid, "enumName");
                if (p.Oid == enc.PrimaryOid)
                {
                    ImGui.TextDisabled("Boss");
                }
                else
                {
                    var enumName = p.EnumName;
                    if (Ui.InputTextEnter($"##enum{p.Oid}", ref enumName))
                        _session.SetOverride(key, OverridesApplier.ToElement(enumName));
                    if (_session.IsOverridden(key))
                    {
                        ImGui.SameLine();
                        ImGui.Text("✎");
                        ImGui.SameLine();
                        if (ImGui.Button($"reset##p{p.Oid}"))
                            _session.ClearOverride(key);
                    }
                }
            }

            ImGui.EndTable();
        }

        ImGui.Separator();
        var arena = em.Arena;
        ImGui.Text($"Arena {arena.Kind}  center ({arena.CenterX:0.##},{arena.CenterZ:0.##})  r={arena.Radius:0.##}  half=({arena.HalfWidth:0.##},{arena.HalfHeight:0.##})  verts={arena.Vertices.Length}");
        Ui.ConfidenceBadge(arena.Confidence);
        if (ImGui.Button("Edit arena…"))
        {
            _arenaKind = arena.Kind;
            _arenaCx = arena.CenterX;
            _arenaCz = arena.CenterZ;
            _arenaR = arena.Radius;
            _arenaHw = arena.HalfWidth;
            _arenaHh = arena.HalfHeight;
            ImGui.OpenPopup("edit-arena");
        }

        if (ImGui.BeginPopupModal("edit-arena"))
        {
            Ui.InputText("Kind", ref _arenaKind);
            ImGui.InputFloat("Center X", ref _arenaCx);
            ImGui.InputFloat("Center Z", ref _arenaCz);
            ImGui.InputFloat("Radius", ref _arenaR);
            ImGui.InputFloat("Half width", ref _arenaHw);
            ImGui.InputFloat("Half height", ref _arenaHh);
            if (ImGui.Button("OK"))
            {
                var edited = arena with
                {
                    Kind = _arenaKind,
                    CenterX = _arenaCx,
                    CenterZ = _arenaCz,
                    Radius = _arenaR,
                    HalfWidth = _arenaHw,
                    HalfHeight = _arenaHh,
                };
                _session.SetOverride(OverridesApplier.ArenaKey, OverridesApplier.ToElement(edited));
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
}
