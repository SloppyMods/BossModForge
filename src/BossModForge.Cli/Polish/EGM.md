# Polish

## Overview
CLI-side providers that produce `PolishPatch` lists (type from `BossModForge.Core.Polish`) for the `polish` command: one reads a JSON file, one POSTs a prompt to an HTTP endpoint, and a shared parser validates the JSON patch-array shape. Patch application/allowlisting is not here (it lives in `Polisher` in Core, called from `Commands/PolishCommand.cs`).

## Interface
- `PolishPatchParser.Parse(JsonElement root) -> IReadOnlyList<PolishPatch>` [PolishPatchParser.cs:PolishPatchParser.Parse]
- `FilePatchProvider.Load(string path) -> IReadOnlyList<PolishPatch>` [FilePatchProvider.cs:FilePatchProvider.Load]
- `HttpPatchProvider(string endpoint, string? apiKey, string? model)` implementing `IPolishProvider`; `Name => "http"`; `Complete(JsonObject prompt) -> JsonElement` [HttpPatchProvider.cs:HttpPatchProvider]

## Invariants
- Root must be a JSON array, and each element an object with string `op` and string `path`; otherwise `InvalidOperationException` is thrown [PolishPatchParser.cs:Parse]. `value` is optional (defaults to `default(JsonElement)`) and is cloned [PolishPatchParser.cs:Parse].
- HTTP request timeout is 30 s [HttpPatchProvider.cs:RequestTimeout]. A non-success status throws `InvalidOperationException` with status, reason and body [HttpPatchProvider.cs:Complete].
- Request body is `{ "prompt": <prompt>, "model": <model> }`; `model` is only added when non-whitespace; `Authorization: Bearer` header is only set when the key is non-empty [HttpPatchProvider.cs:Complete].
- The returned `JsonElement` is cloned so it outlives the disposed `JsonDocument` [HttpPatchProvider.cs:Complete].

## Side Effects
- `FilePatchProvider.Load` reads the file at `path` [FilePatchProvider.cs:Load].
- `HttpPatchProvider.Complete` performs a synchronous HTTP POST to the configured endpoint, sending the prompt (and bearer key if set) [HttpPatchProvider.cs:Complete]; the constructor allocates an `HttpClient` that is never disposed in this class [HttpPatchProvider.cs:ctor].
- `PolishPatchParser`: None.

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from the repo root)
