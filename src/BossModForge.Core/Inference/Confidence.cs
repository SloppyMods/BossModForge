using System.Collections.Immutable;
using System.Text.Json;

namespace BossModForge.Core.Inference;

public enum ConfidenceBand
{
    High,
    Medium,
    Low,
}

public sealed record Confidence(double Score, ConfidenceBand Band, ImmutableArray<string> Reasons)
{
    public static Confidence Of(double score, params string[] reasons)
    {
        var clamped = Math.Clamp(score, 0, 1);
        var band = clamped >= 0.85 ? ConfidenceBand.High
            : clamped >= 0.6 ? ConfidenceBand.Medium
            : ConfidenceBand.Low;
        return new Confidence(clamped, band, [.. reasons]);
    }

    public static Confidence LowBecause(params string[] reasons) => Of(0.3, reasons);
}

public sealed record Evidence(string Kind, string Summary, JsonElement? Data = null);

public enum ReviewSeverity
{
    High,
    Medium,
    Low,
}

public sealed record ReviewItem(
    string Id,
    ReviewSeverity Severity,
    string Subject,
    string Question,
    ImmutableArray<string> Suggested,
    string? Resolution = null);
