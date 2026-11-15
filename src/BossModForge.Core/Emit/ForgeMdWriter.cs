using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;

namespace BossModForge.Core.Emit;

public static class ForgeMdWriter
{
    public static string Write(EncounterModel em, IEmitterProfile profile, ImmutableArray<ReviewItem> review)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(em.Encounter.ModuleName).AppendLine();
        sb.AppendLine();
        sb.Append("- Flavor: ").Append(profile.Flavor.ToString().ToLowerInvariant());
        sb.Append(profile.Profile.Verified ? " (verified)" : " (unverified)").AppendLine();
        sb.Append("- Upstream: ").Append(profile.Profile.Upstream);
        if (!string.IsNullOrEmpty(profile.Profile.Commit))
            sb.Append('@').Append(profile.Profile.Commit);
        sb.AppendLine();
        sb.Append("- Namespace: `").Append(em.Encounter.Namespace).AppendLine("`");
        sb.Append("- Boss: ").Append(em.Encounter.BossName)
            .Append(" (oid 0x").Append(em.Encounter.PrimaryOid.ToString("X", CultureInfo.InvariantCulture))
            .Append(", nameId ").Append(em.Encounter.BossNameId.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
        sb.Append("- Zone ").Append(em.Encounter.Zone.ToString(CultureInfo.InvariantCulture))
            .Append(" / CFC ").Append(em.Encounter.CfcId.ToString(CultureInfo.InvariantCulture)).AppendLine();
        if (!string.IsNullOrEmpty(em.Encounter.DutyName))
            sb.Append("- Duty: ").Append(em.Encounter.DutyName).AppendLine();
        sb.AppendLine();
        sb.AppendLine("## Sources");
        sb.AppendLine();
        foreach (var source in em.Sources)
        {
            sb.Append("- `").Append(source.Path).Append("` sha256=").Append(source.Sha256)
                .Append(" v").Append(source.Version.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(source.GameVersion).AppendLine();
            sb.Append("  ").Append(source.Start.ToString("O", CultureInfo.InvariantCulture))
                .Append(" – ").Append(source.End.ToString("O", CultureInfo.InvariantCulture)).AppendLine();
        }

        if (em.Sources.Length > 1)
        {
            sb.AppendLine();
            sb.AppendLine(
                "> Merged from multiple recordings: actor instance ids must be distinct across merged recordings; " +
                "pulls are segmented per recording and concatenated. If two recordings reused instance ids, " +
                "re-parse before merging — colliding ids merge into spurious actor incarnations and corrupt pull segmentation.");
        }

        sb.AppendLine();
        sb.AppendLine("## Arena");
        sb.AppendLine();
        sb.Append(em.Arena.Kind).Append(" center (")
            .Append(em.Arena.CenterX.ToString("G", CultureInfo.InvariantCulture)).Append(", ")
            .Append(em.Arena.CenterZ.ToString("G", CultureInfo.InvariantCulture)).Append(") r=")
            .Append(em.Arena.Radius.ToString("G", CultureInfo.InvariantCulture));
        if (em.Arena.Kind is "Square" or "Rect")
        {
            sb.Append(" hw=").Append(em.Arena.HalfWidth.ToString("G", CultureInfo.InvariantCulture))
                .Append(" hh=").Append(em.Arena.HalfHeight.ToString("G", CultureInfo.InvariantCulture));
        }

        if (!em.Arena.Vertices.IsDefaultOrEmpty)
            sb.Append(" vertices=").Append(em.Arena.Vertices.Length.ToString(CultureInfo.InvariantCulture));
        sb.Append(" — ").Append(em.Arena.Confidence.Band);
        if (!em.Arena.Confidence.Reasons.IsDefaultOrEmpty)
            sb.Append(" (").Append(string.Join("; ", em.Arena.Confidence.Reasons)).Append(')');
        sb.AppendLine();
        if (!em.Arena.Evidence.IsDefaultOrEmpty)
        {
            foreach (var ev in em.Arena.Evidence)
                sb.Append("- ").Append(ev.Kind).Append(": ").Append(ev.Summary).AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("## Shapes");
        sb.AppendLine();
        if (em.Abilities.IsDefaultOrEmpty)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var a in em.Abilities)
            {
                sb.Append("- ").Append(a.EnumName).Append(' ').Append(a.Class);
                if (a.Shape is not null)
                    sb.Append(' ').Append(DescribeShape(a.Shape));
                if (a.ShapeFit is not null)
                {
                    sb.Append(" score=").Append(a.ShapeFit.Score.ToString("0.00", CultureInfo.InvariantCulture))
                        .Append(" lag=").Append(a.ShapeFit.LagSeconds.ToString("0.0", CultureInfo.InvariantCulture))
                        .Append(" consistency=").Append(a.ShapeFit.FitConsistency.ToString("0.00", CultureInfo.InvariantCulture));
                }

                sb.Append(' ').Append(a.Confidence.Band).AppendLine();
            }
        }
        sb.AppendLine();
        WriteTimeline(sb, em);
        sb.AppendLine("## Review");
        sb.AppendLine();
        if (review.IsDefaultOrEmpty)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var item in review)
            {
                sb.Append("- `").Append(item.Id).Append("` [").Append(item.Severity).Append("] ")
                    .Append(item.Subject).Append(": ").Append(item.Question).AppendLine();
                foreach (var suggestion in item.Suggested.IsDefault ? [] : item.Suggested)
                    sb.Append("  - ").Append(suggestion).AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Participants");
        sb.AppendLine();
        foreach (var p in em.Participants)
        {
            sb.Append("- ").Append(p.EnumName).Append(" = 0x").Append(p.Oid.ToString("X", CultureInfo.InvariantCulture))
                .Append(" (").Append(p.Role).Append(", R").Append(p.HitboxRadius.ToString("F3", CultureInfo.InvariantCulture))
                .Append(", x").Append(p.InstanceCount.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
        }

        if (!sb.ToString().EndsWith('\n'))
            sb.Append('\n');
        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string DescribeShape(Shape shape)
        => shape.Kind switch
        {
            ShapeKind.Circle => "Circle r=" + shape.Radius.ToString("0.#", CultureInfo.InvariantCulture),
            ShapeKind.Donut => "Donut " + shape.InnerRadius.ToString("0.#", CultureInfo.InvariantCulture) + "-" + shape.Radius.ToString("0.#", CultureInfo.InvariantCulture),
            ShapeKind.Cone => "Cone r=" + shape.Radius.ToString("0.#", CultureInfo.InvariantCulture) + " " + shape.HalfAngleDeg.ToString("0.#", CultureInfo.InvariantCulture) + "°",
            ShapeKind.Rect => "Rect " + shape.Radius.ToString("0.#", CultureInfo.InvariantCulture) + "x" + shape.HalfWidth.ToString("0.#", CultureInfo.InvariantCulture),
            ShapeKind.Cross => "Cross " + shape.Radius.ToString("0.#", CultureInfo.InvariantCulture) + "x" + shape.HalfWidth.ToString("0.#", CultureInfo.InvariantCulture),
            ShapeKind.RaidwideNoShape => "Raidwide",
            _ => shape.Kind.ToString(),
        };

    private static void WriteTimeline(StringBuilder sb, EncounterModel em)
    {
        sb.AppendLine("## Timeline");
        sb.AppendLine();
        if (em.Timeline is null)
        {
            sb.AppendLine("(none)");
            sb.AppendLine();
            return;
        }

        var tl = em.Timeline;
        sb.Append("- Pulls: ").Append(tl.PullCount.ToString(CultureInfo.InvariantCulture))
            .Append(" — ").Append(tl.Confidence.Band).AppendLine();
        if (!tl.Phases.IsDefaultOrEmpty)
        {
            sb.AppendLine();
            sb.AppendLine("| Phase | Kind | Threshold | Steps | Activate |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var phase in tl.Phases)
            {
                sb.Append("| ").Append(phase.Index.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(phase.Kind)
                    .Append(" | ").Append(phase.HpThreshold is double hp ? hp.ToString("0.##", CultureInfo.InvariantCulture) : "")
                    .Append(" | ").Append(phase.StepCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(string.Join(", ", phase.Activate.IsDefault ? [] : phase.Activate))
                    .AppendLine(" |");
            }
        }

        if (!tl.Steps.IsDefaultOrEmpty)
        {
            sb.AppendLine();
            sb.AppendLine("| Id | Kind | AID | Delay | Cast | MAD | Support | Activate | Deactivate | Review |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var step in tl.Steps)
            {
                var aids = step.Of.IsDefaultOrEmpty
                    ? (step.Aid is uint a ? a.ToString(CultureInfo.InvariantCulture) : "")
                    : string.Join(",", step.Of.Select(id => id.ToString(CultureInfo.InvariantCulture)));
                sb.Append("| `").Append(step.Id).Append("` | ").Append(step.Kind)
                    .Append(" | ").Append(aids)
                    .Append(" | ").Append(step.Delay.ToString("0.0", CultureInfo.InvariantCulture))
                    .Append(" | ").Append(step.CastTime.ToString("0.0", CultureInfo.InvariantCulture))
                    .Append(" | ").Append(step.TimingMad.ToString("0.0", CultureInfo.InvariantCulture))
                    .Append(" | ").Append(step.Support.ToString(CultureInfo.InvariantCulture)).Append('/').Append(tl.PullCount.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ").Append(string.Join(", ", step.Activate.IsDefault ? [] : step.Activate))
                    .Append(" | ").Append(string.Join(", ", step.Deactivate.IsDefault ? [] : step.Deactivate))
                    .Append(" | ").Append(step.Review ?? "")
                    .AppendLine(" |");
            }
        }

        if (tl.Loop is { } loop)
        {
            var end = loop.StartStep + loop.Length - 1;
            sb.AppendLine();
            sb.Append("Loop: Rotation0: steps ").Append(loop.StartStep.ToString(CultureInfo.InvariantCulture))
                .Append('–').Append(end.ToString(CultureInfo.InvariantCulture))
                .Append(" × ").Append(loop.Repeats.ToString(CultureInfo.InvariantCulture))
                .Append(" + ").Append(loop.TrailingPartial.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" trailing");
        }

        sb.AppendLine();
    }
}
