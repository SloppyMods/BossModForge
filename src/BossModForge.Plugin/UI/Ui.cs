using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using BossModForge.Core.Inference;
using BossModForge.Plugin.Services;

namespace BossModForge.Plugin.UI;

internal static class Ui
{
    public static readonly WindowSizeConstraints SizeConstraints = new()
    {
        MinimumSize = new Vector2(480, 320),
        MaximumSize = new Vector2(4096, 4096),
    };

    public static Vector4 Band(ConfidenceBand band) => band switch
    {
        ConfidenceBand.High => new Vector4(0.30f, 0.69f, 0.31f, 1f),
        ConfidenceBand.Medium => new Vector4(1f, 0.76f, 0.03f, 1f),
        _ => new Vector4(0.96f, 0.26f, 0.21f, 1f),
    };

    public static uint BandU32(ConfidenceBand band) => ColorU32(Band(band));

    // VERIFY-5: ImGui.ColorConvertFloat4ToU32
    public static uint ColorU32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    public static void ConfidenceBadge(Confidence confidence, bool dim = false)
    {
        var color = Band(confidence.Band);
        if (dim)
            color.W *= 0.45f;
        ImGui.TextColored(color, $"{confidence.Band} {confidence.Score:0.00}");
    }

    public static void DrawSessionBar(ForgeSession session, AnalysisRunner runner)
    {
        var snap = runner.Snapshot;
        var file = snap.SourcePath is { } path ? Path.GetFileName(path) : (session.Result?.Recording.Meta.Path is { } p ? Path.GetFileName(p) : "(no recording)");
        ImGui.Text(file);
        ImGui.SameLine();
        var stateColor = snap.State switch
        {
            RunnerState.Ready => Band(ConfidenceBand.High),
            RunnerState.Failed => Band(ConfidenceBand.Low),
            RunnerState.Cancelled => Band(ConfidenceBand.Medium),
            RunnerState.Idle => new Vector4(0.7f, 0.7f, 0.7f, 1f),
            _ => Band(ConfidenceBand.Medium),
        };
        ImGui.TextColored(stateColor, snap.State.ToString());
        if (runner.IsBusy)
        {
            // VERIFY-5: ImGui.ProgressBar
            ImGui.ProgressBar(snap.Progress, new Vector2(-1, 0), snap.StageText);
            if (ImGui.Button("Cancel"))
                runner.Cancel();
        }
        else if (!string.IsNullOrEmpty(snap.StageText) && snap.State is not RunnerState.Idle)
        {
            ImGui.TextDisabled(snap.StageText);
        }

        if (snap.Error is { } err)
            ImGui.TextColored(Band(ConfidenceBand.Low), err);
    }

    // VERIFY-5 resolved: this binding's overload is (label, hint, ref string, int maxLength, flags, callback);
    // plain InputText does not exist in Dalamud.Bindings.ImGui here (InputTextWithHint with empty hint is the idiom).
    public static bool InputText(string label, ref string value, uint max = 256, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
        => ImGui.InputTextWithHint(label, string.Empty, ref value, (int)max, flags);

    public static bool InputTextEnter(string label, ref string value, uint max = 256)
        => InputText(label, ref value, max, ImGuiInputTextFlags.EnterReturnsTrue);

    // VERIFY-5: ImGui.Combo(label, ref int, items, count)
    public static bool Combo(string label, ref int current, string[] items)
        => ImGui.Combo(label, ref current, items, items.Length);

    public static string ShapeSummary(Shape? shape)
    {
        if (shape is null)
            return "—";
        return shape.Kind switch
        {
            ShapeKind.Circle => $"Circle r={shape.Radius:0.##}",
            ShapeKind.Donut => $"Donut {shape.InnerRadius:0.##}–{shape.Radius:0.##}",
            ShapeKind.Cone => $"Cone r={shape.Radius:0.##} ±{shape.HalfAngleDeg:0.#}°",
            ShapeKind.Rect => $"Rect {shape.Radius:0.##}×{shape.HalfWidth * 2:0.##}",
            ShapeKind.Cross => $"Cross {shape.Radius:0.##}×{shape.HalfWidth * 2:0.##}",
            ShapeKind.RaidwideNoShape => "Raidwide",
            ShapeKind.SingleTarget => "SingleTarget",
            _ => shape.Kind.ToString(),
        };
    }

    public static JsonElement NullElement()
    {
        using var doc = JsonDocument.Parse("null");
        return doc.RootElement.Clone();
    }
}
