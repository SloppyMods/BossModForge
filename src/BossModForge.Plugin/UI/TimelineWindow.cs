using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using BossModForge.Core.Inference;
using BossModForge.Plugin.Services;

namespace BossModForge.Plugin.UI;

public sealed class TimelineWindow : Window
{
    private readonly ForgeSession _session;
    private readonly AnalysisRunner _runner;
    private float _pxPerSecond = 24;
    private int _selectedStep = -1;

    public TimelineWindow(ForgeSession session, AnalysisRunner runner)
        : base("BossModForge — Timeline###ForgeTimeline")
    {
        _session = session;
        _runner = runner;
        RespectCloseHotkey = true;
        SizeConstraints = Ui.SizeConstraints;
        Size = new Vector2(980, 560);
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
        if (em?.Timeline is null || result is null)
        {
            ImGui.TextDisabled("Analyze a recording first.");
            return;
        }

        var tl = em.Timeline;
        ImGui.Text($"Pulls {tl.PullCount}");
        ImGui.SameLine();
        Ui.ConfidenceBadge(tl.Confidence);
        if (tl.Loop is { } loop)
            ImGui.Text($"Loop start={loop.StartStep} len={loop.Length} repeats={loop.Repeats} trailing={loop.TrailingPartial}");

        ImGui.SliderFloat("px/s", ref _pxPerSecond, 8, 80);

        if (ImGui.BeginTable("steps", 10, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Id");
            ImGui.TableSetupColumn("Kind");
            ImGui.TableSetupColumn("AID");
            ImGui.TableSetupColumn("Delay");
            ImGui.TableSetupColumn("Cast");
            ImGui.TableSetupColumn("MAD");
            ImGui.TableSetupColumn("Support");
            ImGui.TableSetupColumn("Activate");
            ImGui.TableSetupColumn("Deactivate");
            ImGui.TableSetupColumn("Review");
            ImGui.TableHeadersRow();

            var loopStart = tl.Loop?.StartStep ?? -1;
            var loopEnd = tl.Loop is { } lp ? lp.StartStep + lp.Length : -1;
            foreach (var phase in tl.Phases)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), $"Phase {phase.Index} {phase.Name} ({phase.Kind}) HP={phase.HpThreshold?.ToString("0.##") ?? "—"}");
            }

            for (var i = 0; i < tl.Steps.Length; i++)
            {
                var step = tl.Steps[i];
                ImGui.TableNextRow();
                var inLoop = i >= loopStart && i < loopEnd;
                if (inLoop)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, Ui.ColorU32(new Vector4(0.25f, 0.2f, 0.05f, 0.45f))); // VERIFY-5
                var amber = step.TimingMad > 1.5f || (tl.PullCount > 0 && step.Support < 0.6 * tl.PullCount);
                var warn = amber ? new Vector4(1f, 0.75f, 0.2f, 1f) : new Vector4(1, 1, 1, 1);

                ImGui.TableNextColumn();
                if (ImGui.Selectable(step.Id, _selectedStep == i, ImGuiSelectableFlags.SpanAllColumns))
                    _selectedStep = i;
                ImGui.TableNextColumn();
                ImGui.Text(step.Kind);
                ImGui.TableNextColumn();
                var aidName = step.Aid is { } aid && em.Enums.Aid.TryGetValue(aid, out var n) ? n : (step.Aid?.ToString() ?? "—");
                ImGui.Text(aidName);
                ImGui.TableNextColumn();
                ImGui.TextColored(warn, step.Delay.ToString("0.00"));
                ImGui.TableNextColumn();
                ImGui.Text(step.CastTime.ToString("0.00"));
                ImGui.TableNextColumn();
                ImGui.TextColored(warn, step.TimingMad.ToString("0.00"));
                ImGui.TableNextColumn();
                ImGui.TextColored(warn, $"{step.Support}/{tl.PullCount}");
                ImGui.TableNextColumn();
                ImGui.Text(string.Join(",", step.Activate));
                ImGui.TableNextColumn();
                ImGui.Text(string.Join(",", step.Deactivate));
                ImGui.TableNextColumn();
                ImGui.Text(step.Review ?? "");
            }

            ImGui.EndTable();
        }

        DrawLanes(result, tl);
        DrawStepDetail(result, tl);
    }

    private void DrawLanes(Core.Pipeline.AnalysisResult result, Core.Model.TimelineModel tl)
    {
        ImGui.Text("Consensus lanes");
        var origin = ImGui.GetCursorScreenPos();
        var pulls = Math.Max(1, tl.PullCount);
        var laneH = 14f;
        var header = 18f;
        var height = header + pulls * laneH + 8;
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.Dummy(new Vector2(width, height));
        var wheel = ImGui.GetIO().MouseWheel; // VERIFY-5
        if (ImGui.IsItemHovered() && MathF.Abs(wheel) > 0)
            _pxPerSecond = Math.Clamp(_pxPerSecond + wheel * 4f, 8, 80);

        var draw = ImGui.GetWindowDrawList();
        var x0 = origin.X + 40;
        var w = MathF.Max(40, width - 48);
        var y0 = origin.Y + header;

        draw.AddText(origin, 0xFFCCCCCC, "consensus");
        var t = 0f;
        foreach (var step in tl.Steps)
        {
            t += step.Delay;
            var x = x0 + t * _pxPerSecond;
            if (x > origin.X + width)
                break;
            var color = HashColor(step.Aid ?? 0);
            draw.AddLine(new Vector2(x, origin.Y), new Vector2(x, y0 - 2), color, 2f);
        }

        for (var p = 0; p < pulls; p++)
        {
            var y = y0 + p * laneH + laneH * 0.5f;
            draw.AddText(new Vector2(origin.X, y - 6), 0xFFAAAAAA, p.ToString(CultureInfo.InvariantCulture));
            draw.AddLine(new Vector2(x0, y), new Vector2(x0 + w, y), 0x40FFFFFF, 1f);
            foreach (var ab in result.Catalog.Abilities)
            {
                foreach (var inst in ab.Instances)
                {
                    if (inst.PullIndex != p)
                        continue;
                    var pull = result.Pulls[p];
                    var dt = (float)(inst.EffectTime - pull.Start).TotalSeconds;
                    var x = x0 + dt * _pxPerSecond;
                    if (x < x0 || x > x0 + w)
                        continue;
                    draw.AddCircleFilled(new Vector2(x, y), 3f, HashColor(ab.Action.Id), 8);
                }
            }
        }
    }

    private void DrawStepDetail(Core.Pipeline.AnalysisResult result, Core.Model.TimelineModel tl)
    {
        if (_selectedStep < 0 || _selectedStep >= tl.Steps.Length)
            return;
        var step = tl.Steps[_selectedStep];
        var t = 0f;
        for (var i = 0; i <= _selectedStep; i++)
            t += tl.Steps[i].Delay;
        ImGui.Separator();
        ImGui.Text($"Step {step.Id} ±5s around t={t:0.00}s");
        for (var p = 0; p < result.Pulls.Length; p++)
        {
            var pull = result.Pulls[p];
            foreach (var ab in result.Catalog.Abilities)
            {
                foreach (var inst in ab.Instances)
                {
                    if (inst.PullIndex != p)
                        continue;
                    var dt = (float)(inst.EffectTime - pull.Start).TotalSeconds;
                    if (MathF.Abs(dt - t) > 5f)
                        continue;
                    ImGui.Text($"pull {p}: {ab.Name} {dt - t:+0.00;-0.00;0}");
                }
            }
        }
    }

    private static uint HashColor(uint aid)
    {
        var h = aid * 2654435761u;
        var r = (h >> 16) & 0xFF;
        var g = (h >> 8) & 0xFF;
        var b = h & 0xFF;
        return 0xFF000000 | (b << 16) | (g << 8) | r;
    }
}
