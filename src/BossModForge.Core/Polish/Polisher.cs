using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BossModForge.Core.Inference;
using BossModForge.Core.Model;

namespace BossModForge.Core.Polish;

public sealed record PolishPatch(string Op, string Path, JsonElement Value);

public sealed record PolishResult(
    EncounterModel Model,
    ImmutableArray<string> Accepted,
    ImmutableArray<string> Rejected);

public static class Polisher
{
    private const string ReplaceOp = "replace";
    private const string RemoveOp = "remove";

    private static readonly Regex EntityPath = new(
        "^/(abilities|participants)/(\\d+)/(enumName|name|comment)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReviewPath = new(
        "^/review/([^/]+)/resolution$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static PolishResult Apply(EncounterModel em, IReadOnlyList<PolishPatch> patches)
    {
        var abilities = em.Abilities.IsDefault ? ImmutableArray<AbilityModel>.Empty : em.Abilities;
        var participants = em.Participants.IsDefault ? ImmutableArray<ParticipantModel>.Empty : em.Participants;
        var review = em.Review.IsDefault ? ImmutableArray<ReviewItem>.Empty : em.Review;
        var aidNames = em.Enums.Aid;
        var oidNames = em.Enums.Oid;
        var encounter = em.Encounter;
        var accepted = new List<string>();
        var rejected = new List<string>();
        var changed = false;

        foreach (var patch in patches)
        {
            try
            {
                changed |= ApplyOne(
                    patch, ref abilities, ref participants, ref review, ref aidNames, ref oidNames, encounter,
                    accepted, rejected);
            }
            catch (Exception ex)
            {
                rejected.Add(patch.Path + ": error: " + ex.GetType().Name);
            }
        }

        accepted.Sort(StringComparer.Ordinal);
        rejected.Sort(StringComparer.Ordinal);

        if (!changed)
            return new PolishResult(em, [.. accepted], [.. rejected]);

        var enums = ReferenceEquals(aidNames, em.Enums.Aid) && ReferenceEquals(oidNames, em.Enums.Oid)
            ? em.Enums
            : em.Enums with { Aid = aidNames, Oid = oidNames };
        var model = em with
        {
            Encounter = encounter,
            Abilities = abilities,
            Participants = participants,
            Review = review,
            Enums = enums,
        };
        return new PolishResult(model, [.. accepted], [.. rejected]);
    }

    private static bool ApplyOne(
        PolishPatch patch,
        ref ImmutableArray<AbilityModel> abilities,
        ref ImmutableArray<ParticipantModel> participants,
        ref ImmutableArray<ReviewItem> review,
        ref ImmutableSortedDictionary<uint, string> aidNames,
        ref ImmutableSortedDictionary<uint, string> oidNames,
        EncounterInfo encounter,
        List<string> accepted,
        List<string> rejected)
    {
        var entity = EntityPath.Match(patch.Path);
        if (entity.Success)
        {
            var kind = entity.Groups[1].Value;
            var id = uint.Parse(entity.Groups[2].Value, CultureInfo.InvariantCulture);
            var field = entity.Groups[3].Value;
            return kind == "abilities"
                ? ApplyAbility(patch, id, field, ref abilities, ref aidNames, accepted, rejected)
                : ApplyParticipant(patch, id, field, ref participants, ref oidNames, encounter, accepted, rejected);
        }

        if (ReviewPath.Match(patch.Path) is { Success: true } reviewMatch)
            return ApplyResolution(patch, reviewMatch.Groups[1].Value, ref review, accepted, rejected);

        rejected.Add(patch.Path + ": path outside allowlist");
        return false;
    }

    private static bool ApplyAbility(
        PolishPatch patch,
        uint aid,
        string field,
        ref ImmutableArray<AbilityModel> abilities,
        ref ImmutableSortedDictionary<uint, string> aidNames,
        List<string> accepted,
        List<string> rejected)
    {
        var indexes = new List<int>();
        for (var i = 0; i < abilities.Length; i++)
        {
            if (abilities[i].ActionId == aid)
                indexes.Add(i);
        }

        if (indexes.Count == 0)
        {
            rejected.Add(string.Create(CultureInfo.InvariantCulture, $"{patch.Path}: unknown ability id {aid}"));
            return false;
        }

        switch (field)
        {
            case "enumName":
                if (!TryReplaceString(patch, rejected, out var rawName))
                    return false;
                var enumName = CSharpIdentifier.Sanitise(rawName, "Ability");
                if (aidNames.Any(kv => kv.Key != aid && kv.Value == enumName)
                    || abilities.Any(a => a.ActionId != aid && a.EnumName == enumName))
                {
                    rejected.Add(patch.Path + ": enum name '" + enumName + "' collides with an existing AID");
                    return false;
                }

                abilities = UpdateAt(abilities, indexes, a => a.EnumName == enumName ? a : a with { EnumName = enumName });
                if (!aidNames.TryGetValue(aid, out var existing) || existing != enumName)
                    aidNames = aidNames.SetItem(aid, enumName);
                accepted.Add(patch.Path + ": enumName set to '" + enumName + "'");
                return true;

            case "name":
                if (!TryReplaceString(patch, rejected, out var name))
                    return false;
                abilities = UpdateAt(abilities, indexes, a => a.Name == name ? a : a with { Name = name });
                accepted.Add(patch.Path + ": name set");
                return true;

            default: // comment
                if (patch.Op == RemoveOp)
                {
                    abilities = UpdateAt(abilities, indexes, a => a.Comment is null ? a : a with { Comment = null });
                    accepted.Add(patch.Path + ": comment removed");
                    return true;
                }

                if (!TryReplaceString(patch, rejected, out var comment))
                    return false;
                abilities = UpdateAt(abilities, indexes, a => a.Comment == comment ? a : a with { Comment = comment });
                accepted.Add(patch.Path + ": comment set");
                return true;
        }
    }

    private static bool ApplyParticipant(
        PolishPatch patch,
        uint oid,
        string field,
        ref ImmutableArray<ParticipantModel> participants,
        ref ImmutableSortedDictionary<uint, string> oidNames,
        EncounterInfo encounter,
        List<string> accepted,
        List<string> rejected)
    {
        var index = -1;
        for (var i = 0; i < participants.Length; i++)
        {
            if (participants[i].Oid == oid)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            rejected.Add(string.Create(CultureInfo.InvariantCulture, $"{patch.Path}: unknown participant oid {oid}"));
            return false;
        }

        switch (field)
        {
            case "enumName":
                if (!TryReplaceString(patch, rejected, out var rawName))
                    return false;
                var enumName = CSharpIdentifier.Sanitise(rawName, "Actor");
                var current = participants[index];
                if (oid == encounter.PrimaryOid && current.EnumName == "Boss" && enumName != "Boss")
                {
                    rejected.Add(patch.Path + ": cannot rename the primary OID away from Boss");
                    return false;
                }

                if (oidNames.Any(kv => kv.Key != oid && kv.Value == enumName)
                    || participants.Any(p => p.Oid != oid && p.EnumName == enumName))
                {
                    rejected.Add(patch.Path + ": enum name '" + enumName + "' collides with an existing OID");
                    return false;
                }

                if (current.EnumName != enumName)
                    participants = participants.SetItem(index, current with { EnumName = enumName });
                if (!oidNames.TryGetValue(oid, out var existing) || existing != enumName)
                    oidNames = oidNames.SetItem(oid, enumName);
                accepted.Add(patch.Path + ": enumName set to '" + enumName + "'");
                return true;

            case "name":
                if (!TryReplaceString(patch, rejected, out var name))
                    return false;
                participants = participants.SetItem(index, participants[index] with { Name = name });
                accepted.Add(patch.Path + ": name set");
                return true;

            default: // comment
                if (patch.Op == RemoveOp)
                {
                    participants = participants.SetItem(index, participants[index] with { Comment = null });
                    accepted.Add(patch.Path + ": comment removed");
                    return true;
                }

                if (!TryReplaceString(patch, rejected, out var comment))
                    return false;
                participants = participants.SetItem(index, participants[index] with { Comment = comment });
                accepted.Add(patch.Path + ": comment set");
                return true;
        }
    }

    private static bool ApplyResolution(
        PolishPatch patch,
        string reviewId,
        ref ImmutableArray<ReviewItem> review,
        List<string> accepted,
        List<string> rejected)
    {
        var index = -1;
        for (var i = 0; i < review.Length; i++)
        {
            if (review[i].Id == reviewId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            rejected.Add(patch.Path + ": unknown review id " + reviewId);
            return false;
        }

        if (patch.Op == RemoveOp)
        {
            review = review.SetItem(index, review[index] with { Resolution = null });
            accepted.Add(patch.Path + ": resolution removed");
            return true;
        }

        if (!TryReplaceString(patch, rejected, out var resolution))
            return false;
        if (!review[index].Suggested.Contains(resolution, StringComparer.Ordinal))
        {
            rejected.Add(patch.Path + ": resolution must be one of the suggested values for " + reviewId);
            return false;
        }

        review = review.SetItem(index, review[index] with { Resolution = resolution });
        accepted.Add(patch.Path + ": resolution set");
        return true;
    }

    private static bool TryReplaceString(PolishPatch patch, List<string> rejected, out string value)
    {
        if (patch.Op != ReplaceOp)
        {
            rejected.Add(patch.Path + ": unsupported op '" + patch.Op + "'");
            value = "";
            return false;
        }

        if (patch.Value.ValueKind == JsonValueKind.String)
        {
            value = patch.Value.GetString() ?? "";
            return true;
        }

        rejected.Add(patch.Path + ": value must be a string");
        value = "";
        return false;
    }

    private static ImmutableArray<AbilityModel> UpdateAt(
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
}
