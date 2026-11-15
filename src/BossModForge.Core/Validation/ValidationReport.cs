using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;

namespace BossModForge.Core.Validation;

public sealed record ValidationOptions(
    double TimingP90MaxSeconds = 1.0,
    double TimingMatchWindowSeconds = 10.0,
    double MaxMissedStepFraction = 0.4,
    double AoeMinPrecision = 0.95,
    double AoeMinRecall = 0.95,
    SegmenterOptions? Segmenter = null,
    IGameData? GameData = null)
{
    public static readonly ValidationOptions Default = new();
}

public sealed record StepTiming(
    string StepId,
    string Kind,
    uint? Aid,
    int Expected,
    int Observed,
    int Missed,
    double MedianError,
    double P90Error,
    double MaxError);

public sealed record TimingReport(
    int Steps,
    int Samples,
    int Missed,
    double P50,
    double P90,
    double Max,
    bool Passed,
    ImmutableArray<StepTiming> PerStep);

public sealed record AoeReport(
    string Component,
    uint Aid,
    int Instances,
    int Tp,
    int Fp,
    int Fn,
    int Tn,
    double Precision,
    double Recall,
    bool Passed);

public sealed record CoverageReport(
    int DamagingCasts,
    int Covered,
    double Fraction,
    ImmutableArray<uint> UncoveredAids);

public sealed record PullValidation(
    int RecordingIndex,
    string RecordingPath,
    int PullIndex,
    DateTime Start,
    ImmutableArray<double> TimingDeltas,
    int ObservedSteps,
    int MissedSteps);

public sealed record RecordingValidation(
    string Path,
    int PullCount,
    ImmutableArray<PullValidation> Pulls);

public sealed record ValidationThresholds(
    double TimingP90MaxSeconds,
    double TimingMatchWindowSeconds,
    double MaxMissedStepFraction,
    double AoeMinPrecision,
    double AoeMinRecall);

public sealed record ValidationReport(
    string EmPath,
    ImmutableArray<string> Recordings,
    int PullCount,
    TimingReport Timing,
    ImmutableArray<AoeReport> Aoe,
    CoverageReport Coverage,
    bool Passed,
    ImmutableArray<string> Failures,
    ImmutableArray<ReviewItem> SuggestedReview,
    ImmutableArray<RecordingValidation> PerRecording,
    ValidationThresholds Thresholds);

public static class ValidationReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    public static string ToJson(ValidationReport report)
    {
        var node = JsonSerializer.SerializeToNode(report, SerializerOptions) as JsonObject
            ?? throw new InvalidOperationException("ValidationReport did not serialize to a JSON object.");
        SortKeys(node);
        var json = node.ToJsonString(SerializerOptions);
        if (!json.EndsWith('\n'))
            json += "\n";
        return json.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string ToMarkdown(ValidationReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Validation report");
        sb.AppendLine();
        sb.Append("- Passed: ").Append(report.Passed ? "true" : "false").AppendLine();
        sb.Append("- Pulls: ").Append(report.PullCount.ToString(CultureInfo.InvariantCulture)).AppendLine();
        if (!string.IsNullOrEmpty(report.EmPath))
            sb.Append("- EM: `").Append(report.EmPath).AppendLine("`");
        foreach (var path in report.Recordings.IsDefaultOrEmpty ? [] : report.Recordings)
            sb.Append("- Recording: `").Append(path).AppendLine("`");
        sb.AppendLine();
        sb.AppendLine("## Timing");
        sb.AppendLine();
        sb.Append("- P50: ").Append(Fmt(report.Timing.P50));
        sb.Append("  P90: ").Append(Fmt(report.Timing.P90));
        sb.Append("  Max: ").Append(Fmt(report.Timing.Max));
        sb.Append("  Missed: ").Append(report.Timing.Missed.ToString(CultureInfo.InvariantCulture));
        sb.Append("  Samples: ").Append(report.Timing.Samples.ToString(CultureInfo.InvariantCulture));
        sb.Append("  ").Append(report.Timing.Passed ? "PASS" : "FAIL").AppendLine();
        foreach (var step in report.Timing.PerStep.IsDefaultOrEmpty ? [] : report.Timing.PerStep)
        {
            sb.Append("- `").Append(step.StepId).Append("` ").Append(step.Kind);
            if (step.Aid is { } aid)
                sb.Append(" AID ").Append(aid.ToString(CultureInfo.InvariantCulture));
            sb.Append(" expected=").Append(step.Expected.ToString(CultureInfo.InvariantCulture));
            sb.Append(" observed=").Append(step.Observed.ToString(CultureInfo.InvariantCulture));
            sb.Append(" missed=").Append(step.Missed.ToString(CultureInfo.InvariantCulture));
            sb.Append(" p90=").Append(Fmt(step.P90Error)).AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("## AOE");
        sb.AppendLine();
        if (report.Aoe.IsDefaultOrEmpty || report.Aoe.Length == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var aoe in report.Aoe)
            {
                sb.Append("- ").Append(aoe.Component)
                    .Append(" AID ").Append(aoe.Aid.ToString(CultureInfo.InvariantCulture))
                    .Append(" P=").Append(Fmt(aoe.Precision))
                    .Append(" R=").Append(Fmt(aoe.Recall))
                    .Append(' ').Append(aoe.Passed ? "PASS" : "FAIL")
                    .AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Coverage");
        sb.AppendLine();
        sb.Append("- ").Append(report.Coverage.Covered.ToString(CultureInfo.InvariantCulture))
            .Append('/')
            .Append(report.Coverage.DamagingCasts.ToString(CultureInfo.InvariantCulture))
            .Append(" (").Append(Fmt(report.Coverage.Fraction)).AppendLine(")");
        foreach (var aid in report.Coverage.UncoveredAids.IsDefaultOrEmpty ? [] : report.Coverage.UncoveredAids)
            sb.Append("- uncovered AID ").Append(aid.ToString(CultureInfo.InvariantCulture)).AppendLine();

        if (!report.Failures.IsDefaultOrEmpty && report.Failures.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Failures");
            sb.AppendLine();
            foreach (var failure in report.Failures)
                sb.Append("- ").Append(failure).AppendLine();
        }

        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Fmt(double value)
        => value.ToString("0.000", CultureInfo.InvariantCulture);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        options.Converters.Add(new DateTimeOConverter());
        return options;
    }

    private static void SortKeys(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var properties = obj.ToList();
                foreach (var (key, _) in properties)
                    obj.Remove(key);
                foreach (var (key, value) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    SortKeys(value);
                    obj[key] = value;
                }

                break;
            }
            case JsonArray array:
                foreach (var child in array)
                    SortKeys(child);
                break;
        }
    }

    private sealed class DateTimeOConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => DateTime.Parse(reader.GetString() ?? throw new JsonException("DateTime was null."), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }
}
