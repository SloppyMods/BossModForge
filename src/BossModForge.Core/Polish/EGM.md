# Polish

## Overview
Applies an allowlisted set of JSON-patch-style edits (typically produced by an external polish provider such as an LLM) to an `EncounterModel`. `Polisher` validates each patch independently and returns the updated model together with accepted/rejected messages. `IPolishProvider` is the provider contract; no implementation lives here.

## Interface
- `IPolishProvider`: `string Name { get; }`, `JsonElement Complete(JsonObject prompt)` [Polish/IPolishProvider.cs:IPolishProvider]
- `PolishPatch(string Op, string Path, JsonElement Value)` [Polish/Polisher.cs:PolishPatch]
- `PolishResult(EncounterModel Model, ImmutableArray<string> Accepted, ImmutableArray<string> Rejected)` [Polish/Polisher.cs:PolishResult]
- `Polisher.Apply(EncounterModel em, IReadOnlyList<PolishPatch> patches)` -> `PolishResult` [Polish/Polisher.cs:Polisher.Apply]
- Subdirectories: None

## Invariants
- Only three path shapes are accepted: `/abilities/{id}/(enumName|name|comment)`, `/participants/{id}/(enumName|name|comment)`, `/review/{reviewId}/resolution`. Anything else is rejected with `path outside allowlist` [Polish/Polisher.cs:Polisher.EntityPath, ReviewPath, ApplyOne]
- `{id}` is a decimal ActionId (abilities) or Oid (participants); an id not present is rejected [Polish/Polisher.cs:Polisher.ApplyAbility, ApplyParticipant]
- Only `replace` sets values; `remove` is honoured only for `comment` and `resolution`. Other ops are rejected as unsupported, and non-string values are rejected [Polish/Polisher.cs:Polisher.TryReplaceString, ApplyAbility, ApplyParticipant, ApplyResolution]
- `enumName` is passed through `CSharpIdentifier.Sanitise` (fallback `"Ability"` / `"Actor"`) and rejected if it collides with another AID/OID name in `Enums` or in another ability/participant [Polish/Polisher.cs:Polisher.ApplyAbility, ApplyParticipant]
- The primary OID's participant with `EnumName == "Boss"` cannot be renamed to anything else [Polish/Polisher.cs:Polisher.ApplyParticipant]
- A review `resolution` must equal one of that item's `Suggested` values (ordinal compare) [Polish/Polisher.cs:Polisher.ApplyResolution]
- An accepted `enumName` updates both the entity and `Enums.Aid`/`Enums.Oid` [Polish/Polisher.cs:Polisher.ApplyAbility, ApplyParticipant]
- Ability patches are applied to every `AbilityModel` sharing the ActionId [Polish/Polisher.cs:Polisher.ApplyAbility]
- Patches are processed independently; an exception in one is caught and recorded as `"{path}: error: {ExceptionType}"` without aborting the rest [Polish/Polisher.Apply]
- `Accepted` and `Rejected` are sorted with `StringComparer.Ordinal`; if no patch was accepted the original `em` instance is returned [Polish/Polisher.cs:Polisher.Apply]
- The input model is not mutated (immutable collections and `with` expressions) [Polish/Polisher.cs:Polisher.Apply]

## Side Effects
- None in this directory: no I/O, network, locks or static mutable state (only compiled static `Regex` fields) [Polish/Polisher.cs:Polisher]
- `IPolishProvider.Complete` is a contract only; any I/O is the implementer's [Polish/IPolishProvider.cs:IPolishProvider]

## Verification
- Run: `dotnet test BossModForge.slnx -c Release` (from repo root)
