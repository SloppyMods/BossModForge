using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;
using BossModForge.Plugin.Services;

namespace BossModForge.Plugin.UI;

public sealed class AbilitiesWindow : Window
{
    private static readonly string[] ShapeKinds = Enum.GetNames<ShapeKind>();
    private static readonly string[] Origins = Enum.GetNames<ShapeOrigin>();
    private static readonly string[] Directions = Enum.GetNames<ShapeDirection>();
    private static readonly string[] Classes = Enum.GetNames<MechanicClass>();

    private readonly ForgeSession _session;
    private readonly AnalysisRunner _runner;
    private string _filter = "";
    private bool _hideLow;
    private bool _onlyOverridden;
    private uint? _evidenceAid;
    private int _instanceIndex;
    private uint _shapeAid;
    private int _shapeKind;
    private float _radius = 8, _inner, _halfAngle = 45, _halfWidth = 2, _lengthBack;
    private int _origin;
    private int _direction;

    public AbilitiesWindow(ForgeSession session, AnalysisRunner runner)
        : base("BossModForge — Abilities###ForgeAbilities")
    {
        _session = session;
        _runner = runner;
        RespectCloseHotkey = true;
        SizeConstraints = Ui.SizeConstraints;
        Size = new Vector2(1100, 620);
    }

    public override void Draw()
    {
        Ui.DrawSessionBar(_session, _runner);
        DrawContent();
    }

    public void DrawContent()
    {
        var em = _session.Effective;
        var result = _session.Result;
        if (em is null || result is null)
        {
            ImGui.TextDisabled("Analyze a recording first.");
            return;
        }

        Ui.InputText("Filter", ref _filter);
        ImGui.SameLine();
        ImGui.Checkbox("hide Low-band", ref _hideLow);
        ImGui.SameLine();
        ImGui.Checkbox("only overridden", ref _onlyOverridden);

        var avail = ImGui.GetContentRegionAvail();
        var leftW = MathF.Max(420, avail.X * 0.62f);
        // VERIFY-5: BeginChild(string, Vector2, bool border)
        if (ImGui.BeginChild("abil-left", new Vector2(leftW, 0), true))
            DrawTable(em);
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("abil-right", new Vector2(0, 0), true))
        {
            var selected = em.Abilities.FirstOrDefault(a => a.ActionId == _evidenceAid);
            if (selected is null)
                ImGui.TextDisabled("Select Evidence on a row.");
            else
                EvidencePlot.Draw(result, selected, _instanceIndex, i => _instanceIndex = i);
        }
        ImGui.EndChild();

        DrawShapePopup();
    }

    private void DrawTable(EncounterModel em)
    {
        if (!ImGui.BeginTable("abilities", 9, ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("AID");
        ImGui.TableSetupColumn("Name");
        ImGui.TableSetupColumn("Source");
        ImGui.TableSetupColumn("Cast");
        ImGui.TableSetupColumn("Shape");
        ImGui.TableSetupColumn("Class");
        ImGui.TableSetupColumn("Confidence");
        ImGui.TableSetupColumn("Inst");
        ImGui.TableSetupColumn("Actions");
        ImGui.TableHeadersRow();

        foreach (var a in em.Abilities)
        {
            if (_hideLow && a.Confidence.Band == ConfidenceBand.Low)
                continue;
            var shapeKey = OverridesApplier.AbilityKey(a.ActionId, "shape");
            var classKey = OverridesApplier.AbilityKey(a.ActionId, "class");
            var enumKey = OverridesApplier.AbilityKey(a.ActionId, "enumName");
            var ignoreKey = OverridesApplier.AbilityKey(a.ActionId, "ignore");
            var overridden = _session.IsOverridden(shapeKey) || _session.IsOverridden(classKey)
                             || _session.IsOverridden(enumKey) || _session.IsOverridden(ignoreKey);
            if (_onlyOverridden && !overridden)
                continue;
            if (_filter.Length > 0
                && !a.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                && !a.ActionId.ToString(CultureInfo.InvariantCulture).Contains(_filter, StringComparison.Ordinal)
                && !a.EnumName.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                continue;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(a.ActionId.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.Text(a.Name);
            ImGui.TableNextColumn();
            var src = em.Enums.Oid.TryGetValue(a.SourceOid, out var oidName) ? oidName : $"0x{a.SourceOid:X}";
            ImGui.Text(src);
            ImGui.TableNextColumn();
            ImGui.Text(a.CastTime.ToString("0.00"));
            ImGui.TableNextColumn();
            ImGui.Text(Ui.ShapeSummary(a.Shape));
            if (_session.IsOverridden(shapeKey))
            {
                ImGui.SameLine();
                ImGui.Text("✎");
            }
            ImGui.TableNextColumn();
            var classIdx = Math.Max(0, Array.IndexOf(Classes, a.Class.ToString()));
            if (Ui.Combo($"##class{a.ActionId}", ref classIdx, Classes) && classIdx >= 0 && classIdx < Classes.Length)
                _session.SetOverride(classKey, OverridesApplier.ToElement(Classes[classIdx]));
            ImGui.TableNextColumn();
            Ui.ConfidenceBadge(a.Confidence, _session.ReviewedAids.Contains(a.ActionId));
            ImGui.TableNextColumn();
            ImGui.Text(a.InstanceCount.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            if (ImGui.Button($"Accept##{a.ActionId}"))
                _session.MarkReviewed(a.ActionId);
            ImGui.SameLine();
            if (ImGui.Button($"Shape##{a.ActionId}"))
            {
                _shapeAid = a.ActionId;
                _shapeKind = a.Shape is null ? 0 : (int)a.Shape.Kind;
                _radius = a.Shape?.Radius ?? 8;
                _inner = a.Shape?.InnerRadius ?? 0;
                _halfAngle = a.Shape?.HalfAngleDeg ?? 45;
                _halfWidth = a.Shape?.HalfWidth ?? 2;
                _lengthBack = a.Shape?.LengthBack ?? 0;
                _origin = a.Shape is null ? 0 : (int)a.Shape.Origin;
                _direction = a.Shape is null ? 0 : (int)a.Shape.Direction;
                ImGui.OpenPopup("override-shape");
            }
            ImGui.SameLine();
            var enumName = a.EnumName;
            if (Ui.InputTextEnter($"##en{a.ActionId}", ref enumName, 64))
                _session.SetOverride(enumKey, OverridesApplier.ToElement(enumName));
            ImGui.SameLine();
            var ignore = _session.IsOverridden(ignoreKey);
            if (ImGui.Checkbox($"Ignore##{a.ActionId}", ref ignore))
            {
                if (ignore)
                    _session.SetOverride(ignoreKey, OverridesApplier.ToElement(true));
                else
                    _session.ClearOverride(ignoreKey);
            }
            ImGui.SameLine();
            if (ImGui.Button($"Evidence##{a.ActionId}"))
            {
                _evidenceAid = a.ActionId;
                _instanceIndex = 0;
            }
            if (overridden && ImGui.Button($"reset##{a.ActionId}"))
            {
                _session.ClearOverride(shapeKey);
                _session.ClearOverride(classKey);
                _session.ClearOverride(enumKey);
                _session.ClearOverride(ignoreKey);
            }
        }

        ImGui.EndTable();
    }

    private void DrawShapePopup()
    {
        if (!ImGui.BeginPopupModal("override-shape"))
            return;

        Ui.Combo("Kind", ref _shapeKind, ShapeKinds);
        var kind = (ShapeKind)_shapeKind;
        if (kind is ShapeKind.Circle or ShapeKind.Donut or ShapeKind.Cone or ShapeKind.Rect or ShapeKind.Cross)
            ImGui.InputFloat("Radius / length", ref _radius);
        if (kind == ShapeKind.Donut)
            ImGui.InputFloat("Inner radius", ref _inner);
        if (kind == ShapeKind.Cone)
            ImGui.InputFloat("Half angle", ref _halfAngle);
        if (kind is ShapeKind.Rect or ShapeKind.Cross)
            ImGui.InputFloat("Half width", ref _halfWidth);
        if (kind == ShapeKind.Rect)
            ImGui.InputFloat("Length back", ref _lengthBack);
        Ui.Combo("Origin", ref _origin, Origins);
        Ui.Combo("Direction", ref _direction, Directions);

        if (ImGui.Button("OK"))
        {
            var shape = BuildShape(kind);
            _session.SetOverride(OverridesApplier.AbilityKey(_shapeAid, "shape"), OverridesApplier.ToElement(shape));
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear shape"))
        {
            _session.SetOverride(OverridesApplier.AbilityKey(_shapeAid, "shape"), OverridesApplier.ToElement((Shape?)null));
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private Shape BuildShape(ShapeKind kind)
    {
        var origin = (ShapeOrigin)Math.Clamp(_origin, 0, Origins.Length - 1);
        var dir = (ShapeDirection)Math.Clamp(_direction, 0, Directions.Length - 1);
        return kind switch
        {
            ShapeKind.Circle => Shape.Circle(_radius, origin),
            ShapeKind.Donut => Shape.Donut(_inner, _radius, origin),
            ShapeKind.Cone => Shape.Cone(_radius, _halfAngle, origin, dir),
            ShapeKind.Rect => Shape.Rect(_radius, _halfWidth, _lengthBack, origin, dir),
            ShapeKind.Cross => Shape.Cross(_radius, _halfWidth, origin, dir),
            ShapeKind.RaidwideNoShape => Shape.Raidwide,
            _ => Shape.SingleTarget,
        };
    }
}
