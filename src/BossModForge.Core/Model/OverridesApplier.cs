using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BossModForge.Core.Inference;

namespace BossModForge.Core.Model;

public static class OverridesApplier
{
    public const string ModuleNameKey = "/encounter/moduleName";
    public const string NamespaceKey = "/encounter/namespace";
    public const string ArenaKey = "/arena";

    private const string OverrideSubject = "override";
    private const string ShapeReason = "user override: shape";
    private const string ClassReason = "user override: class";
    private const string ArenaReason = "user override: arena";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly Evidence OverrideEvidence = new("Override", "user");
    private static readonly ImmutableArray<Evidence> OverrideEvidenceArray =
        ImmutableArray.Create(OverrideEvidence);

    public static string AbilityKey(uint aid, string field)
        => string.Create(CultureInfo.InvariantCulture, $"/abilities/{aid}/{field}");

    public static string ParticipantKey(uint oid, string field)
        => string.Create(CultureInfo.InvariantCulture, $"/participants/{oid}/{field}");

    public static JsonElement ToElement(Shape? shape) => JsonSerializer.SerializeToElement(shape, JsonOptions);

    public static JsonElement ToElement(string value) => JsonSerializer.SerializeToElement(value, JsonOptions);

    public static JsonElement ToElement(bool value) => JsonSerializer.SerializeToElement(value, JsonOptions);

    public static JsonElement ToElement(ArenaModel arena) => JsonSerializer.SerializeToElement(arena, JsonOptions);

    public static EncounterModel Apply(EncounterModel em)
    {
        if (em.Overrides.IsEmpty)
            return em;

        var abilities = em.Abilities.IsDefault ? ImmutableArray<AbilityModel>.Empty : em.Abilities;
        var participants = em.Participants.IsDefault ? ImmutableArray<ParticipantModel>.Empty : em.Participants;
        var aidNames = em.Enums.Aid;
        var oidNames = em.Enums.Oid;
        var encounter = em.Encounter;
        var arena = em.Arena;
        var extra = new List<ReviewItem>();
        var ignoredAids = new HashSet<uint>();
        foreach (var (key, value) in em.Overrides.Values)
        {
            if (TryParseAbilityKey(key, out var aid, out var field)
                && field == "ignore"
                && value.ValueKind == JsonValueKind.True)
            {
                ignoredAids.Add(aid);
            }
        }

        var ignoreSubjects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ability in abilities)
        {
            if (!ignoredAids.Contains(ability.ActionId))
                continue;
            ignoreSubjects.Add(ability.Name);
            ignoreSubjects.Add(ability.EnumName);
        }

        foreach (var (key, value) in em.Overrides.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            try
            {
                if (key == ModuleNameKey)
                {
                    if (!TryString(value, out var raw) || string.IsNullOrWhiteSpace(raw))
                    {
                        extra.Add(Malformed(key));
                        continue;
                    }

                    var moduleName = CSharpIdentifier.Sanitise(raw, "Encounter");
                    if (encounter.ModuleName != moduleName)
                        encounter = encounter with { ModuleName = moduleName };
                    continue;
                }

                if (key == NamespaceKey)
                {
                    if (!TryString(value, out var raw))
                    {
                        extra.Add(Malformed(key));
                        continue;
                    }

                    var trimmed = raw.Trim();
                    if (trimmed.Length == 0)
                    {
                        extra.Add(Malformed(key));
                        continue;
                    }

                    if (encounter.Namespace != trimmed)
                        encounter = encounter with { Namespace = trimmed };
                    continue;
                }

                if (key == ArenaKey)
                {
                    if (value.ValueKind != JsonValueKind.Object)
                    {
                        extra.Add(Malformed(key));
                        continue;
                    }

                    var parsed = value.Deserialize<ArenaModel>(JsonOptions);
                    if (parsed is null)
                    {
                        extra.Add(Malformed(key));
                        continue;
                    }

                    arena = ApplyArenaOverride(arena, parsed);
                    continue;
                }

                if (TryParseAbilityKey(key, out var aid, out var abilityField))
                {
                    var indexes = AbilityIndexes(abilities, aid);
                    if (indexes.Count == 0)
                    {
                        extra.Add(Malformed(key, "Unknown ability id " + aid.ToString(CultureInfo.InvariantCulture)));
                        continue;
                    }

                    switch (abilityField)
                    {
                        case "shape":
                            if (value.ValueKind == JsonValueKind.Null)
                            {
                                abilities = WithAbilities(abilities, indexes, a => OverrideShape(a, null));
                                break;
                            }

                            if (value.ValueKind != JsonValueKind.Object)
                            {
                                extra.Add(Malformed(key));
                                break;
                            }

                            var shape = value.Deserialize<Shape>(JsonOptions);
                            if (shape is null)
                            {
                                extra.Add(Malformed(key));
                                break;
                            }

                            abilities = WithAbilities(abilities, indexes, a => OverrideShape(a, shape));
                            break;
                        case "class":
                            if (!TryString(value, out var className)
                                || !Enum.TryParse<MechanicClass>(className, ignoreCase: false, out var cls))
                            {
                                extra.Add(Malformed(key));
                                break;
                            }

                            abilities = WithAbilities(abilities, indexes, a => OverrideClass(a, cls));
                            break;
                        case "enumName":
                            if (!TryString(value, out var rawName))
                            {
                                extra.Add(Malformed(key));
                                break;
                            }

                            var enumName = CSharpIdentifier.Sanitise(rawName, "Ability");
                            if (aidNames.Any(kv => kv.Key != aid && kv.Value == enumName)
                                || abilities.Any(a => a.ActionId != aid && a.EnumName == enumName))
                            {
                                extra.Add(new ReviewItem(
                                    "",
                                    ReviewSeverity.High,
                                    OverrideSubject,
                                    "Enum name '" + enumName + "' collides with an existing AID",
                                    ImmutableArray<string>.Empty));
                                break;
                            }

                            abilities = WithAbilities(abilities, indexes, a =>
                                a.EnumName == enumName ? a : a with { EnumName = enumName });
                            if (!aidNames.TryGetValue(aid, out var existingAidName) || existingAidName != enumName)
                                aidNames = aidNames.SetItem(aid, enumName);
                            break;
                        case "ignore":
                            if (value.ValueKind == JsonValueKind.False)
                                break;
                            if (value.ValueKind != JsonValueKind.True)
                            {
                                extra.Add(Malformed(key));
                                break;
                            }

                            abilities = WithAbilities(abilities, indexes, OverrideIgnore);
                            break;
                        default:
                            extra.Add(Unknown(key));
                            break;
                    }

                    continue;
                }

                if (TryParseParticipantKey(key, out var oid, out var participantField))
                {
                    var index = ParticipantIndex(participants, oid);
                    if (index < 0)
                    {
                        extra.Add(Malformed(key, "Unknown participant oid " + oid.ToString(CultureInfo.InvariantCulture)));
                        continue;
                    }

                    if (participantField != "enumName")
                    {
                        extra.Add(Unknown(key));
                        continue;
                    }

                    if (!TryString(value, out var rawName))
                    {
                        extra.Add(Malformed(key));
                        continue;
                    }

                    var enumName = CSharpIdentifier.Sanitise(rawName, "Actor");
                    var current = participants[index];
                    if (oid == encounter.PrimaryOid && current.EnumName == "Boss" && enumName != "Boss")
                    {
                        extra.Add(new ReviewItem(
                            "",
                            ReviewSeverity.High,
                            OverrideSubject,
                            "Cannot rename the primary OID away from Boss",
                            ImmutableArray<string>.Empty));
                        continue;
                    }

                    if (oidNames.Any(kv => kv.Key != oid && kv.Value == enumName)
                        || participants.Any(p => p.Oid != oid && p.EnumName == enumName))
                    {
                        extra.Add(new ReviewItem(
                            "",
                            ReviewSeverity.High,
                            OverrideSubject,
                            "Enum name '" + enumName + "' collides with an existing OID",
                            ImmutableArray<string>.Empty));
                        continue;
                    }

                    if (current.EnumName != enumName)
                        participants = participants.SetItem(index, current with { EnumName = enumName });
                    if (!oidNames.TryGetValue(oid, out var existingOidName) || existingOidName != enumName)
                        oidNames = oidNames.SetItem(oid, enumName);
                    continue;
                }

                extra.Add(Unknown(key));
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException)
            {
                extra.Add(Malformed(key));
            }
        }

        extra.Sort(CompareOverrideReviews);
        var kept = new List<ReviewItem>();
        foreach (var item in em.Review.IsDefault ? [] : em.Review)
        {
            if (item.Subject == OverrideSubject)
                continue;
            if (ignoreSubjects.Contains(item.Subject))
                continue;
            kept.Add(item);
        }

        kept.AddRange(extra);
        var numbered = NumberReviews(kept);

        var finalAbilities = abilities.SequenceEqual(em.Abilities) ? em.Abilities : abilities;
        var finalParticipants = participants.SequenceEqual(em.Participants) ? em.Participants : participants;
        var finalReview = numbered.SequenceEqual(em.Review) ? em.Review : numbered;
        var finalEnums = ReferenceEquals(aidNames, em.Enums.Aid) && ReferenceEquals(oidNames, em.Enums.Oid)
            ? em.Enums
            : em.Enums with { Oid = oidNames, Aid = aidNames };

        if (ReferenceEquals(encounter, em.Encounter)
            && finalParticipants == em.Participants
            && finalAbilities == em.Abilities
            && ReferenceEquals(finalEnums, em.Enums)
            && ReferenceEquals(arena, em.Arena)
            && finalReview == em.Review)
        {
            return em;
        }

        return em with
        {
            Encounter = encounter,
            Participants = finalParticipants,
            Abilities = finalAbilities,
            Enums = finalEnums,
            Arena = arena,
            Review = finalReview,
        };
    }

    private static Confidence KeepOrOverride(Confidence current, string reason)
    {
        if (current.Score == 1.0
            && current.Band == ConfidenceBand.High
            && current.Reasons.Length == 1
            && current.Reasons[0] == reason)
        {
            return current;
        }

        return Confidence.Of(1.0, reason);
    }

    private static AbilityModel OverrideShape(AbilityModel ability, Shape? shape)
    {
        if (shape is null)
        {
            if (ability.Shape is null && ability.ShapeFit is null)
            {
                var confidence = KeepOrOverride(ability.Confidence, ShapeReason);
                return ReferenceEquals(confidence, ability.Confidence) ? ability : ability with { Confidence = confidence };
            }

            return ability with
            {
                Shape = null,
                ShapeFit = null,
                Confidence = KeepOrOverride(ability.Confidence, ShapeReason),
            };
        }

        if (Equals(ability.Shape, shape) && ability.ShapeFit is null)
        {
            var confidence = KeepOrOverride(ability.Confidence, ShapeReason);
            return ReferenceEquals(confidence, ability.Confidence) ? ability : ability with { Confidence = confidence };
        }

        return ability with
        {
            Shape = shape,
            ShapeFit = null,
            Confidence = KeepOrOverride(ability.Confidence, ShapeReason),
        };
    }

    private static AbilityModel OverrideClass(AbilityModel ability, MechanicClass cls)
    {
        if (ability.Class == cls)
        {
            var confidence = KeepOrOverride(ability.Confidence, ClassReason);
            return ReferenceEquals(confidence, ability.Confidence) ? ability : ability with { Confidence = confidence };
        }

        return ability with
        {
            Class = cls,
            Confidence = KeepOrOverride(ability.Confidence, ClassReason),
        };
    }

    private static AbilityModel OverrideIgnore(AbilityModel ability)
    {
        if (ability.Class == MechanicClass.Unclassified && ability.Shape is null && ability.ShapeFit is null)
            return ability;

        return ability with
        {
            Class = MechanicClass.Unclassified,
            Shape = null,
            ShapeFit = null,
        };
    }

    private static ArenaModel ApplyArenaOverride(ArenaModel current, ArenaModel parsed)
    {
        var confidence = KeepOrOverride(current.Confidence, ArenaReason);
        var evidence = IsOverrideEvidence(current.Evidence) ? current.Evidence : OverrideEvidenceArray;
        if (ArenaGeometryEquals(current, parsed)
            && ReferenceEquals(confidence, current.Confidence)
            && evidence == current.Evidence)
        {
            return current;
        }

        return parsed with
        {
            Confidence = confidence,
            Evidence = evidence,
        };
    }

    private static bool ArenaGeometryEquals(ArenaModel left, ArenaModel right)
        => left.Kind == right.Kind
            && left.CenterX == right.CenterX
            && left.CenterZ == right.CenterZ
            && left.Radius == right.Radius
            && left.HalfWidth == right.HalfWidth
            && left.HalfHeight == right.HalfHeight
            && left.Vertices.SequenceEqual(right.Vertices);

    private static bool IsOverrideEvidence(ImmutableArray<Evidence> evidence)
        => evidence.Length == 1
            && evidence[0].Kind == OverrideEvidence.Kind
            && evidence[0].Summary == OverrideEvidence.Summary;

    private static ImmutableArray<ReviewItem> NumberReviews(List<ReviewItem> items)
    {
        if (items.Count == 0)
            return ImmutableArray<ReviewItem>.Empty;

        var builder = ImmutableArray.CreateBuilder<ReviewItem>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var id = "R" + (i + 1).ToString(CultureInfo.InvariantCulture);
            var item = items[i];
            builder.Add(item.Id == id ? item : item with { Id = id });
        }

        return builder.ToImmutable();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }

    private static bool TryParseAbilityKey(string key, out uint aid, out string field)
    {
        aid = 0;
        field = "";
        const string prefix = "/abilities/";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var rest = key[prefix.Length..];
        var slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1)
            return false;
        if (rest.IndexOf('/', slash + 1) >= 0)
            return false;
        if (!uint.TryParse(rest[..slash], NumberStyles.None, CultureInfo.InvariantCulture, out aid))
            return false;
        field = rest[(slash + 1)..];
        return field is "shape" or "class" or "enumName" or "ignore";
    }

    private static bool TryParseParticipantKey(string key, out uint oid, out string field)
    {
        oid = 0;
        field = "";
        const string prefix = "/participants/";
        if (!key.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var rest = key[prefix.Length..];
        var slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1)
            return false;
        if (rest.IndexOf('/', slash + 1) >= 0)
            return false;
        if (!uint.TryParse(rest[..slash], NumberStyles.None, CultureInfo.InvariantCulture, out oid))
            return false;
        field = rest[(slash + 1)..];
        return true;
    }

    private static bool TryString(JsonElement value, out string text)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            text = value.GetString() ?? "";
            return true;
        }

        text = "";
        return false;
    }

    private static List<int> AbilityIndexes(ImmutableArray<AbilityModel> abilities, uint aid)
    {
        var indexes = new List<int>();
        for (var i = 0; i < abilities.Length; i++)
        {
            if (abilities[i].ActionId == aid)
                indexes.Add(i);
        }

        return indexes;
    }

    private static int ParticipantIndex(ImmutableArray<ParticipantModel> participants, uint oid)
    {
        for (var i = 0; i < participants.Length; i++)
        {
            if (participants[i].Oid == oid)
                return i;
        }

        return -1;
    }

    private static ImmutableArray<AbilityModel> WithAbilities(
        ImmutableArray<AbilityModel> abilities,
        List<int> indexes,
        Func<AbilityModel, AbilityModel> update)
    {
        var changed = false;
        var copy = abilities.ToBuilder();
        foreach (var i in indexes)
        {
            var updated = update(copy[i]);
            if (!ReferenceEquals(updated, copy[i]))
            {
                copy[i] = updated;
                changed = true;
            }
        }

        return changed ? copy.ToImmutable() : abilities;
    }

    private static ReviewItem Unknown(string key)
        => new("", ReviewSeverity.Low, OverrideSubject, "Unknown override key " + key, ImmutableArray<string>.Empty);

    private static ReviewItem Malformed(string key, string? question = null)
        => new("", ReviewSeverity.High, OverrideSubject, question ?? "Malformed override value for " + key, ImmutableArray<string>.Empty);

    private static int CompareOverrideReviews(ReviewItem a, ReviewItem b)
    {
        var byQuestion = string.CompareOrdinal(a.Question, b.Question);
        if (byQuestion != 0)
            return byQuestion;
        return a.Severity.CompareTo(b.Severity);
    }
}
