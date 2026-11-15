// Forge ReplayValidator host (template).
//
// Deployed into a BossMod/BMR checkout by BossmodBuildValidator and executed
// with: dotnet run -- <moduleTypeName> <replay.log> [more logs...]
//
// It loads each recording through BossMod's own replay parser, instantiates the
// emitted module, steps the world state op by op, and prints one JSON object
// per line on stdout: transition events (state-machine state changes) and AOE
// events (each component's active AOEs sampled per action-effect). Forge
// captures stdout and merges it into the validation report.
//
// MODELED on upstream replay tooling at the vbm pin (1391bc42); first real
// compile/run happens on a machine with a BossMod checkout — see
// docs/UPSTREAM.md §11. Keep under 200 lines.

using System.Reflection;
using System.Text.Json;
using BossMod;
using BossMod.Data;
using BossMod.Replay;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ForgeReplayValidator <moduleTypeName> <replay.log> [...]");
    return 2;
}

var moduleTypeName = args[0];
var logPaths = args.Skip(1).ToArray();

var moduleType = typeof(BossModule).Assembly.GetType(moduleTypeName)
    ?? AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType(moduleTypeName))
        .FirstOrDefault(t => t is not null)
    ?? throw new InvalidOperationException($"module type '{moduleTypeName}' not found");

void Emit(JsonElement e) => Console.WriteLine(e.GetRawText());

void EmitEvent(string replay, string ev, Action<Utf8JsonWriter> fields)
{
    using var ms = new MemoryStream();
    using (var w = new Utf8JsonWriter(ms))
    {
        w.WriteStartObject();
        w.WriteString("replay", replay);
        w.WriteString("event", ev);
        fields(w);
        w.WriteEndObject();
    }

    Emit(JsonDocument.Parse(ms.ToArray()).RootElement);
}

foreach (var logPath in logPaths)
{
    var replay = ReplayParser.Parse(logPath); // BossMod's own parser
    var ws = replay.WorldState;

    var primary = ws.Actors.FirstOrDefault(a => a.Type == ActorType.Enemy && !a.IsDeadOrDestroyed)
        ?? ws.Actors.FirstOrDefault()
        ?? throw new InvalidOperationException("no actors in replay");

    // Emitted module ctors are (WorldState ws, Actor primary) — center/bounds are
    // embedded in the emitted module itself (moduleDecl template). Must match arity.
    var module = (BossModule)Activator.CreateInstance(moduleType, ws, primary)!;

    var lastState = -1;
    var t0 = ws.CurrentTime;
    foreach (var op in replay.Ops)
    {
        op.Exec(ws);
        module.Update();

        var state = module.StateMachine?.ActiveState;
        var stateId = state?.ID ?? -1;
        if (stateId != lastState)
        {
            var sid = stateId;
            var t = (long)(ws.CurrentTime - t0).TotalMilliseconds;
            EmitEvent(logPath, "transition", w =>
            {
                w.WriteNumber("t", t);
                w.WriteNumber("state", sid);
            });
            lastState = stateId;
        }

        if (op is ActorCastEvent cast)
        {
            var aid = cast.Action.ID;
            foreach (var comp in module.Components)
            {
                var instances = comp.ActiveAOEs(0, ws.Party.Player() ?? primary).ToList();
                if (instances.Count == 0)
                    continue;
                EmitEvent(logPath, "aoe", w =>
                {
                    w.WriteString("component", comp.GetType().Name);
                    w.WriteNumber("aid", aid);
                    w.WriteNumber("count", instances.Count);
                });
            }
        }
    }
}

return 0;
