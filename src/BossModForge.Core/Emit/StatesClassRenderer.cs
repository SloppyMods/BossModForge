using System.Globalization;
using System.Text;
using BossModForge.Core.Model;

namespace BossModForge.Core.Emit;

internal static class StatesClassRenderer
{
    public static string Render(StatesPlan plan, FlavorProfile profile)
    {
        var ctor = T(profile, "statesCtor").Replace("{name}", plan.ModuleName, StringComparison.Ordinal);
        var sb = new StringBuilder();
        sb.Append("class ").Append(plan.ModuleName).Append("States : StateMachineBuilder\n{\n    ");
        sb.Append(ctor).Append("\n    {\n");
        if (plan.Phases.IsDefaultOrEmpty)
        {
            RenderTrivial(sb, plan, profile);
        }
        else
        {
            for (var i = 0; i < plan.Phases.Length; i++)
            {
                RenderPhaseCtor(sb, plan.Phases[i], profile);
                sb.Append('\n');
            }
        }

        sb.Append("    }\n");
        if (!plan.Phases.IsDefaultOrEmpty)
        {
            foreach (var phase in plan.Phases)
            {
                sb.Append('\n');
                RenderPhaseMethod(sb, phase, profile, isLast: phase.SeqId == plan.Phases[^1].SeqId);
                if (phase.Loop is { } loop)
                {
                    sb.Append('\n');
                    RenderLoopMethod(sb, loop, profile);
                }
            }
        }

        sb.Append("}\n");
        return sb.ToString().TrimEnd('\n');
    }

    private static void RenderTrivial(StringBuilder sb, StatesPlan plan, FlavorProfile profile)
    {
        var phase = T(profile, "trivialPhase");
        sb.Append("        ").Append(phase);
        foreach (var type in plan.TrivialActivate)
        {
            sb.Append('\n');
            sb.Append("            ").Append(T(profile, "activateOnEnter").Replace("{comp}", type, StringComparison.Ordinal));
        }

        sb.Append(';');
    }

    private static void RenderPhaseCtor(StringBuilder sb, PhasePlan phase, FlavorProfile profile)
    {
        string head;
        if (phase.Kind == "HpThreshold")
        {
            head = T(profile, "simplePhase")
                .Replace("{seq}", phase.SeqId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{method}", phase.MethodName, StringComparison.Ordinal)
                .Replace("{name}", phase.DisplayName, StringComparison.Ordinal);
        }
        else
        {
            head = T(profile, "deathPhase")
                .Replace("{seq}", phase.SeqId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{method}", phase.MethodName, StringComparison.Ordinal);
        }

        sb.Append("        ").Append(head);
        foreach (var comp in phase.Activate.OrderBy(n => n, StringComparer.Ordinal))
        {
            sb.Append('\n');
            sb.Append("            ").Append(T(profile, "activateOnEnter").Replace("{comp}", comp, StringComparison.Ordinal));
        }

        if (phase.Kind == "HpThreshold")
        {
            var ratio = ShapeCSharp.Num((float)(phase.HpThreshold ?? 0));
            sb.Append('\n');
            sb.Append("            ").Append(T(profile, "phaseHpUpdate").Replace("{ratio}", ratio, StringComparison.Ordinal));
        }

        sb.Append(';');
    }

    private static void RenderPhaseMethod(StringBuilder sb, PhasePlan phase, FlavorProfile profile, bool isLast)
    {
        sb.Append("    ").Append(T(profile, "phaseMethod").Replace("{method}", phase.MethodName, StringComparison.Ordinal)).Append("\n    {\n");
        foreach (var step in phase.Prefix)
            RenderStep(sb, step, profile, delayOverride: null);
        if (phase.Loop is { } loop)
        {
            for (var r = 0; r < loop.Repeats; r++)
            {
                var offset = loop.FirstCallOffset + loop.Stride * (uint)r;
                var delay = r == 0 ? loop.EntryDelay : loop.Body[0].Delay;
                sb.Append("        ").Append(loop.MethodName).Append('(').Append(IdExpr(offset)).Append(", ").Append(ShapeCSharp.Num(delay)).Append(");\n");
            }

            foreach (var step in phase.Tail)
                RenderStep(sb, step, profile, delayOverride: null);
        }

        if (phase.Loop is not null || isLast)
            sb.Append("        ").Append(T(profile, "stateTail").Replace("{id}", "id + 0xFF0000", StringComparison.Ordinal)).Append(";\n");
        sb.Append("    }\n");
    }

    private static void RenderLoopMethod(StringBuilder sb, LoopPlan loop, FlavorProfile profile)
    {
        sb.Append("    ").Append(T(profile, "loopMethod").Replace("{method}", loop.MethodName, StringComparison.Ordinal)).Append("\n    {\n");
        for (var i = 0; i < loop.Body.Length; i++)
        {
            var step = loop.Body[i];
            RenderStep(sb, step, profile, delayOverride: i == 0 ? "delay" : null);
        }

        sb.Append("    }\n");
    }

    private static void RenderStep(StringBuilder sb, StepPlan step, FlavorProfile profile, string? delayOverride)
    {
        if (!string.IsNullOrEmpty(step.ReviewComment))
            sb.Append("        // ").Append(step.ReviewComment).Append('\n');
        var id = IdExpr(step.Offset);
        var delay = delayOverride ?? ShapeCSharp.Num(step.Delay);
        var castTime = ShapeCSharp.Num(step.CastTime);
        var duration = ShapeCSharp.Num(step.Duration);
        var name = step.Name;
        string call;
        switch (step.Kind)
        {
            case "CastMulti":
                var aids = string.Join(", ", step.OfMembers.Select(m => "AID." + m));
                call = T(profile, "stateCastMulti")
                    .Replace("{id}", id, StringComparison.Ordinal)
                    .Replace("{aids}", aids, StringComparison.Ordinal)
                    .Replace("{delay}", delay, StringComparison.Ordinal)
                    .Replace("{castTime}", castTime, StringComparison.Ordinal)
                    .Replace("{name}", name, StringComparison.Ordinal);
                break;
            case "Timeout":
                call = T(profile, "stateTimeout")
                    .Replace("{id}", id, StringComparison.Ordinal)
                    .Replace("{duration}", duration, StringComparison.Ordinal)
                    .Replace("{name}", name, StringComparison.Ordinal);
                break;
            case "Targetable":
                call = T(profile, "stateTargetable")
                    .Replace("{id}", id, StringComparison.Ordinal)
                    .Replace("{value}", step.Targetable == true ? "true" : "false", StringComparison.Ordinal)
                    .Replace("{delay}", delay, StringComparison.Ordinal)
                    .Replace("{name}", name, StringComparison.Ordinal);
                break;
            default:
                call = T(profile, "stateCast")
                    .Replace("{id}", id, StringComparison.Ordinal)
                    .Replace("{aid}", step.AidMember ?? "Unknown", StringComparison.Ordinal)
                    .Replace("{delay}", delay, StringComparison.Ordinal)
                    .Replace("{castTime}", castTime, StringComparison.Ordinal)
                    .Replace("{name}", name, StringComparison.Ordinal);
                break;
        }

        sb.Append("        ").Append(call);
        var links = new List<string>();
        foreach (var c in step.Activate.OrderBy(n => n, StringComparer.Ordinal))
            links.Add(T(profile, "activateOnEnter").Replace("{comp}", c, StringComparison.Ordinal));
        foreach (var c in step.Deactivate.OrderBy(n => n, StringComparer.Ordinal))
            links.Add(T(profile, "deactivateOnExit").Replace("{comp}", c, StringComparison.Ordinal));
        if (links.Count == 0)
        {
            sb.Append(";\n");
            return;
        }

        foreach (var link in links)
        {
            sb.Append('\n');
            sb.Append("            ").Append(link);
        }

        sb.Append(";\n");
    }

    internal static string IdExpr(uint offset)
        => offset == 0 ? "id" : "id + 0x" + offset.ToString("X", CultureInfo.InvariantCulture);

    private static string T(FlavorProfile profile, string key)
        => profile.Templates.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException("Flavor template '" + key + "' is missing.");
}
