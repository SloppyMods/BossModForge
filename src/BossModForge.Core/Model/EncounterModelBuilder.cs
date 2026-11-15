using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using BossModForge.Core.GameData;
using BossModForge.Core.Inference;
using BossModForge.Core.Replay;

namespace BossModForge.Core.Model;

public static class EncounterModelBuilder
{
    public const uint HelperOid = 0x233C;

    public static EncounterModel Build(
        Recording recording,
        ImmutableArray<Pull> pulls,
        ImmutableArray<Participant> participants,
        AbilityCatalog catalog,
        IGameData gameData,
        BuildOptions? options = null,
        IReadOnlyList<Recording>? sources = null)
    {
        options ??= new BuildOptions();
        if (pulls.IsDefault)
            pulls = [];
        if (participants.IsDefault)
            participants = [];
        catalog = new AbilityCatalog(
            catalog.Abilities.IsDefault ? [] : catalog.Abilities,
            catalog.Statuses.IsDefault ? [] : catalog.Statuses,
            catalog.Tethers.IsDefault ? [] : catalog.Tethers,
            catalog.Icons.IsDefault ? [] : catalog.Icons);
        var review = ImmutableArray.CreateBuilder<ReviewItem>();

        var first = pulls.IsDefaultOrEmpty ? null : pulls[0];
        var zone = first?.Zone ?? (ushort)0;
        var cfc = first?.CfcId ?? (ushort)0;
        var primaryOid = PickPrimaryOid(pulls, review);
        var boss = participants.FirstOrDefault(p => p.Oid == primaryOid && p.Role == ParticipantRole.Boss)
            ?? participants.FirstOrDefault(p => p.Oid == primaryOid);
        var bossName = boss?.Name;
        if (string.IsNullOrWhiteSpace(bossName) && boss is not null)
            bossName = gameData.BNpcName(boss.NameId);
        if (string.IsNullOrWhiteSpace(bossName))
            bossName = "Boss";
        var bossNameId = boss?.NameId ?? 0;
        var dutyName = gameData.ContentForTerritory(zone)?.Name ?? gameData.PlaceName(zone);
        var moduleName = string.IsNullOrWhiteSpace(options.ModuleName)
            ? CSharpIdentifier.Sanitise(bossName, "Encounter")
            : CSharpIdentifier.Sanitise(options.ModuleName, "Encounter");
        var ns = string.IsNullOrWhiteSpace(options.Namespace)
            ? "BossMod.Forge." + moduleName
            : options.Namespace;

        var emParticipants = participants
            .Where(p => p.Role is not ParticipantRole.Player and not ParticipantRole.Pet)
            .ToImmutableArray();

        var arenaFit = BoundsFitter.Fit(recording, pulls, primaryOid, options.Bounds);
        if (arenaFit.Evidence.Any(e => e.Kind == "ArenaChange"))
        {
            var when = arenaFit.Evidence.First(e => e.Kind == "ArenaChange").Summary;
            review.Add(new ReviewItem(
                "",
                ReviewSeverity.Medium,
                "arena",
                "Arena appears to change at " + when,
                ["Split phases if the arena actually changes"]));
        }

        var (oidNames, oidOrder) = BuildOidEnum(emParticipants);
        var (aidNames, abilities) = BuildAbilities(catalog, recording, pulls, arenaFit, options.Shape, review);
        var (sidNames, statuses) = BuildStatuses(catalog);
        var (tetherNames, tethers) = BuildTethers(catalog);
        var (iconNames, icons) = BuildIcons(catalog);

        var participantModels = emParticipants.Select(p => new ParticipantModel(
            p.Oid,
            oidNames[p.Oid],
            p.Name,
            p.NameId,
            p.Type.ToString(),
            p.Role.ToString(),
            p.HitboxRadius,
            p.InstanceCount,
            p.SpawnsDuringFight,
            p.Confidence,
            p.Evidence.IsDefault ? [] : p.Evidence)).ToImmutableArray();

        var vertices = arenaFit.Vertices.IsDefault
            ? ImmutableArray<PointModel>.Empty
            : arenaFit.Vertices.Select(v => new PointModel(v.X, v.Y)).ToImmutableArray();
        var arena = new ArenaModel(
            arenaFit.Kind.ToString(),
            arenaFit.Center.X,
            arenaFit.Center.Y,
            arenaFit.Radius,
            arenaFit.HalfWidth,
            arenaFit.HalfHeight,
            vertices,
            arenaFit.Confidence,
            arenaFit.Evidence.IsDefault ? [] : arenaFit.Evidence);

        var pullSummaries = pulls.Select(p => new PullSummary(
            p.Index,
            recording.Meta.Path,
            p.Start,
            p.End,
            p.Outcome.ToString(),
            (p.End - p.Start).TotalSeconds,
            p.Confidence)).ToImmutableArray();

        var em = new EncounterModel(
            SchemaVersion: 1,
            Encounter: new EncounterInfo(zone, cfc, dutyName, primaryOid, bossName, bossNameId, moduleName, ns),
            Sources: sources is null
                ? [SourceFor(recording)]
                : [.. sources.Select(SourceFor)],
            Pulls: pullSummaries,
            Participants: participantModels,
            Abilities: abilities,
            Statuses: statuses,
            Tethers: tethers,
            Icons: icons,
            Enums: new EnumNames(oidOrder, aidNames, sidNames, tetherNames, iconNames),
            Arena: arena,
            Review: [],
            Overrides: Overrides.Empty);
        em = EncounterTimelineBuilder.Attach(em, recording, pulls, catalog, review);
        var numbered = review.Select((item, i) => item with { Id = "R" + (i + 1).ToString(CultureInfo.InvariantCulture) }).ToImmutableArray();
        return em with { Review = numbered };
    }

    private static uint PickPrimaryOid(ImmutableArray<Pull> pulls, ImmutableArray<ReviewItem>.Builder review)
    {
        if (pulls.IsDefaultOrEmpty)
            return 0;

        var groups = pulls.GroupBy(p => p.PrimaryOid)
            .Select(g => (Oid: g.Key, Count: g.Count()))
            .ToArray();
        var best = groups.Max(g => g.Count);
        var tied = groups.Where(g => g.Count == best).Select(g => g.Oid).ToArray();
        var chosen = tied.Min();
        if (groups.Length > 1)
        {
            review.Add(new ReviewItem(
                "",
                ReviewSeverity.Medium,
                "encounter",
                "Multiple primary OIDs observed across pulls",
                ["Confirm the encounter's primary actor OID"]));
        }

        return chosen;
    }

    private static (Dictionary<uint, string> Lookup, ImmutableSortedDictionary<uint, string> Sorted) BuildOidEnum(
        ImmutableArray<Participant> participants)
    {
        var entries = new List<(uint Oid, string Raw)>();
        foreach (var p in participants.OrderBy(p => p.Oid))
        {
            var raw = p.Role switch
            {
                ParticipantRole.Boss => "Boss",
                ParticipantRole.Helper => "Helper",
                _ => CSharpIdentifier.Sanitise(p.Name, "Actor"),
            };
            entries.Add((p.Oid, raw));
        }

        if (participants.Any(p => p.Role == ParticipantRole.Helper) && entries.TrueForAll(e => e.Oid != HelperOid))
            entries.Add((HelperOid, "Helper"));

        entries.Sort((a, b) => a.Oid.CompareTo(b.Oid));
        var deduped = CSharpIdentifier.Dedupe(entries.Select(e => e.Raw));
        var lookup = new Dictionary<uint, string>();
        var sorted = ImmutableSortedDictionary.CreateBuilder<uint, string>();
        for (var i = 0; i < entries.Count; i++)
        {
            lookup[entries[i].Oid] = deduped[i];
            sorted[entries[i].Oid] = deduped[i];
        }

        return (lookup, sorted.ToImmutable());
    }

    private static (ImmutableSortedDictionary<uint, string> Names, ImmutableArray<AbilityModel> Models) BuildAbilities(
        AbilityCatalog catalog,
        Recording recording,
        ImmutableArray<Pull> pulls,
        ArenaFit arena,
        ShapeInferenceOptions? shapeOptions,
        ImmutableArray<ReviewItem>.Builder review)
    {
        var spell = new List<(uint Id, string Raw, Ability Ability)>();
        foreach (var ability in catalog.Abilities)
        {
            if (ability.Action.Type != ActionType.Spell)
            {
                review.Add(new ReviewItem(
                    "",
                    ReviewSeverity.Low,
                    ability.Name,
                    $"Non-spell action {ability.Action} omitted from AID enum",
                    []));
                continue;
            }

            var raw = string.IsNullOrWhiteSpace(ability.Name)
                ? "Ability" + ability.Action.Id.ToString(CultureInfo.InvariantCulture)
                : CSharpIdentifier.Sanitise(ability.Name, "Ability");
            spell.Add((ability.Action.Id, raw, ability));
        }

        spell.Sort((a, b) => a.Id.CompareTo(b.Id));
        var names = CSharpIdentifier.Dedupe(spell.Select(s => s.Raw));
        var map = ImmutableSortedDictionary.CreateBuilder<uint, string>();
        var models = ImmutableArray.CreateBuilder<AbilityModel>();
        for (var i = 0; i < spell.Count; i++)
        {
            map[spell[i].Id] = names[i];
            var a = spell[i].Ability;
            var fit = ShapeInference.Infer(a, recording, pulls, arena, shapeOptions);
            var shapeClass = ShapeInference.Classify(a, fit, arena, shapeOptions);
            var classified = MechanicClassifier.Classify(a, recording, pulls, arena, fit, shapeClass);
            var cls = classified.Class;
            var evidence = a.Evidence.IsDefault ? ImmutableArray<Evidence>.Empty : a.Evidence;
            if (fit is not null)
                evidence = evidence.AddRange(fit.Evidence);
            evidence = evidence.AddRange(classified.Evidence);
            if (cls == MechanicClass.TelegraphedAOE && a.Sheet is null)
                evidence = evidence.Add(new Evidence("OmenUnknown", "No action sheet omen"));
            var conf = a.Confidence;
            if (fit is not null && fit.Confidence.Score < conf.Score)
                conf = fit.Confidence;
            if (classified.Confidence is { } mc && mc.Score < conf.Score)
                conf = mc;
            if (fit is not null && fit.Confidence.Band == ConfidenceBand.Low)
            {
                review.Add(new ReviewItem(
                    "",
                    ReviewSeverity.Medium,
                    "shape",
                    names[i] + ": shape uncertain (" + fit.Shape.Kind + ", score " + fit.Score.ToString("0.00", CultureInfo.InvariantCulture) + ")",
                    []));
            }

            if (cls is MechanicClass.SharedTankbuster or MechanicClass.Attract or MechanicClass.Towers
                or MechanicClass.Chasing or MechanicClass.Instant or MechanicClass.Interrupt)
            {
                review.Add(new ReviewItem(
                    "",
                    ReviewSeverity.Medium,
                    names[i],
                    cls + " has no emitted standard component",
                    ["Review and implement manually"]));
            }

            ShapeFitSummary? summary = fit is null
                ? null
                : new ShapeFitSummary(fit.Score, fit.FitConsistency, fit.MinMargin, fit.LagSeconds, fit.Instances);
            models.Add(new AbilityModel(
                a.SourceOid,
                a.Action.Id,
                a.Action.Type.ToString(),
                names[i],
                a.Name,
                a.HasCastBar,
                a.CastTime,
                a.Interruptible,
                a.InstanceCount,
                cls,
                fit?.Shape,
                summary,
                conf,
                evidence));
        }

        return (map.ToImmutable(), models.ToImmutable());
    }

    private static (ImmutableSortedDictionary<uint, string> Names, ImmutableArray<StatusModel> Models) BuildStatuses(AbilityCatalog catalog)
    {
        var entries = catalog.Statuses.OrderBy(s => s.StatusId).ToArray();
        var raw = entries.Select(s => string.IsNullOrWhiteSpace(s.Name)
            ? "Status" + s.StatusId.ToString(CultureInfo.InvariantCulture)
            : CSharpIdentifier.Sanitise(s.Name, "Status" + s.StatusId.ToString(CultureInfo.InvariantCulture))).ToArray();
        var names = CSharpIdentifier.Dedupe(raw);
        var map = ImmutableSortedDictionary.CreateBuilder<uint, string>();
        var models = ImmutableArray.CreateBuilder<StatusModel>();
        for (var i = 0; i < entries.Length; i++)
        {
            map[entries[i].StatusId] = names[i];
            models.Add(new StatusModel(entries[i].StatusId, names[i], entries[i].Name, entries[i].Applications));
        }

        return (map.ToImmutable(), models.ToImmutable());
    }

    private static (ImmutableSortedDictionary<uint, string> Names, ImmutableArray<TetherModel> Models) BuildTethers(AbilityCatalog catalog)
    {
        var entries = catalog.Tethers.OrderBy(t => t.TetherId).ToArray();
        var names = CSharpIdentifier.Dedupe(entries.Select(t => "Tether" + t.TetherId.ToString(CultureInfo.InvariantCulture)));
        var map = ImmutableSortedDictionary.CreateBuilder<uint, string>();
        var models = ImmutableArray.CreateBuilder<TetherModel>();
        for (var i = 0; i < entries.Length; i++)
        {
            map[entries[i].TetherId] = names[i];
            models.Add(new TetherModel(entries[i].TetherId, names[i], entries[i].Count));
        }

        return (map.ToImmutable(), models.ToImmutable());
    }

    private static (ImmutableSortedDictionary<uint, string> Names, ImmutableArray<IconModel> Models) BuildIcons(AbilityCatalog catalog)
    {
        var entries = catalog.Icons.OrderBy(t => t.IconId).ToArray();
        var names = CSharpIdentifier.Dedupe(entries.Select(t => "Icon" + t.IconId.ToString(CultureInfo.InvariantCulture)));
        var map = ImmutableSortedDictionary.CreateBuilder<uint, string>();
        var models = ImmutableArray.CreateBuilder<IconModel>();
        for (var i = 0; i < entries.Length; i++)
        {
            map[entries[i].IconId] = names[i];
            models.Add(new IconModel(entries[i].IconId, names[i], entries[i].Count));
        }

        return (map.ToImmutable(), models.ToImmutable());
    }

    internal static SourceInfo SourceFor(Recording recording)
        => new(
            recording.Meta.Path,
            FileSha256(recording.Meta.Path),
            recording.Meta.Version,
            recording.Meta.GameVersion,
            recording.Start,
            recording.End);

    private static string FileSha256(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "";
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
