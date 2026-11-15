using System.CommandLine;
using System.Text.Json.Nodes;
using BossModForge.Cli.Polish;
using BossModForge.Core.Model;
using BossModForge.Core.Polish;

namespace BossModForge.Cli.Commands;

public static class PolishCommand
{
    internal const string AllowlistText =
        "Allowed patch paths (everything else is rejected): " +
        "replace/remove /abilities/{actionId}/enumName (sanitised to a C# identifier; must not collide with an existing AID), " +
        "/abilities/{actionId}/name, /abilities/{actionId}/comment, " +
        "/participants/{oid}/enumName (the primary OID must keep the name Boss), /participants/{oid}/name, /participants/{oid}/comment, " +
        "/review/{reviewId}/resolution (value must be one of that review item's suggested[] values). " +
        "Patches never touch geometry, mechanic classes, or the timeline.";

    public static Command Create()
    {
        var em = new Argument<string>("em.json")
        {
            Description = "Encounter Model JSON path",
        };
        var provider = new Option<string>("--provider")
        {
            Description = "Patch source: file (offline patch review) or http (LLM endpoint)",
            Required = true,
        };
        var patches = new Option<string?>("--patches")
        {
            Description = "JSON patch array file (required for --provider file)",
        };
        var endpoint = new Option<string?>("--endpoint")
        {
            Description = "HTTP endpoint that accepts the prompt JSON and returns a JSON patch array (required for --provider http)",
        };
        var model = new Option<string?>("--model")
        {
            Description = "Model name sent to the http provider",
        };
        var apiKeyEnv = new Option<string?>("--api-key-env")
        {
            Description = "Name of the environment variable holding the bearer token for the http provider",
        };
        var output = new Option<string?>("--out")
        {
            Description = "Write the polished Encounter Model JSON here (default: stdout)",
        };
        var command = new Command("polish", "Apply allowlisted LLM/user polish patches to an Encounter Model")
        {
            em,
            provider,
            patches,
            endpoint,
            model,
            apiKeyEnv,
            output,
        };
        command.SetAction(parseResult =>
        {
            var emPath = parseResult.GetValue(em) ?? "";
            var providerName = parseResult.GetValue(provider) ?? "";
            var patchesPath = parseResult.GetValue(patches);
            var endpointUrl = parseResult.GetValue(endpoint);
            var modelName = parseResult.GetValue(model);
            var apiKeyEnvName = parseResult.GetValue(apiKeyEnv);
            var outPath = parseResult.GetValue(output);
            return Run(emPath, providerName, patchesPath, endpointUrl, modelName, apiKeyEnvName, outPath, Console.Out, Console.Error);
        });
        return command;
    }

    public static int Run(
        string emPath,
        string providerName,
        string? patchesPath,
        string? endpoint,
        string? model,
        string? apiKeyEnv,
        string? outPath,
        TextWriter stdout,
        TextWriter stderr)
    {
        string json;
        try
        {
            json = File.ReadAllText(emPath);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {emPath}: {ex.Message}");
            return 4;
        }

        var errors = EncounterModelJson.Validate(json);
        if (errors.Length > 0)
        {
            foreach (var error in errors)
                stderr.WriteLine("error: " + error);
            return 3;
        }

        EncounterModel em;
        try
        {
            em = EncounterModelJson.Deserialize(json);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {emPath}: {ex.Message}");
            return 4;
        }

        IReadOnlyList<PolishPatch> patches;
        if (providerName.Equals("file", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(patchesPath))
            {
                stderr.WriteLine("error: --provider file requires --patches <file>");
                return 4;
            }

            try
            {
                patches = FilePatchProvider.Load(patchesPath);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"error: {patchesPath}: {ex.Message}");
                return 4;
            }
        }
        else if (providerName.Equals("http", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                stderr.WriteLine("error: --provider http requires --endpoint <url>");
                return 4;
            }

            string? apiKey = null;
            if (!string.IsNullOrWhiteSpace(apiKeyEnv))
            {
                apiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
                if (string.IsNullOrEmpty(apiKey))
                {
                    stderr.WriteLine($"error: environment variable '{apiKeyEnv}' (--api-key-env) is not set or empty");
                    return 4;
                }
            }

            try
            {
                var httpProvider = new HttpPatchProvider(endpoint, apiKey, model);
                patches = PolishPatchParser.Parse(httpProvider.Complete(BuildPrompt(em)));
            }
            catch (Exception ex)
            {
                stderr.WriteLine("error: " + ex.Message);
                return 4;
            }
        }
        else
        {
            stderr.WriteLine("error: --provider must be file or http");
            return 4;
        }

        var result = Polisher.Apply(em, patches);
        var polished = EncounterModelJson.Serialize(result.Model);
        var outErrors = EncounterModelJson.Validate(polished);
        if (outErrors.Length > 0)
        {
            foreach (var error in outErrors)
                stderr.WriteLine("error: polished model is not schema-valid: " + error);
            return 3;
        }

        if (string.IsNullOrWhiteSpace(outPath))
        {
            stdout.WriteLine(polished.TrimEnd('\n'));
        }
        else
        {
            try
            {
                File.WriteAllText(outPath, polished);
            }
            catch (Exception ex)
            {
                stderr.WriteLine("error: " + ex.Message);
                return 4;
            }
        }

        foreach (var item in result.Accepted)
            stdout.WriteLine("accepted: " + item);
        foreach (var item in result.Rejected)
            stdout.WriteLine("rejected: " + item);
        stdout.WriteLine($"polish: {result.Accepted.Length} accepted, {result.Rejected.Length} rejected"
            + (string.IsNullOrWhiteSpace(outPath) ? "" : $" -> {outPath}"));
        return 0;
    }

    private static JsonObject BuildPrompt(EncounterModel em)
    {
        JsonArray review = [];
        foreach (var item in em.Review)
        {
            JsonArray suggested = [.. item.Suggested];
            review.Add(new JsonObject
            {
                ["id"] = item.Id,
                ["question"] = item.Question,
                ["suggested"] = suggested,
            });
        }

        return new JsonObject
        {
            ["encounterModel"] = JsonNode.Parse(EncounterModelJson.Serialize(em)),
            ["review"] = review,
            ["allowlist"] = AllowlistText,
        };
    }
}
