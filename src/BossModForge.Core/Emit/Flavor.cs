using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BossModForge.Core.Emit;

public enum Flavor
{
    Vbm,
    Bmr,
}

public sealed record FlavorProfile(
    Flavor Flavor,
    string Upstream,
    string Commit,
    bool Verified,
    string RootNamespace,
    string HelperComment,
    ImmutableSortedDictionary<string, string> Templates)
{
    public static FlavorProfile Load(string path)
    {
        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<FlavorFile>(json, FlavorJsonOptions)
            ?? throw new JsonException("Flavor profile deserialized to null: " + path);
        var flavor = dto.Flavor?.Equals("bmr", StringComparison.OrdinalIgnoreCase) == true ? Flavor.Bmr : Flavor.Vbm;
        var templates = ImmutableSortedDictionary.CreateRange(
            StringComparer.Ordinal,
            dto.Templates ?? new Dictionary<string, string>());
        return new FlavorProfile(
            flavor,
            dto.Upstream ?? "",
            dto.Commit ?? "",
            dto.Verified,
            dto.RootNamespace ?? "BossMod",
            dto.HelperOid ?? "",
            templates);
    }

    private static readonly JsonSerializerOptions FlavorJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private sealed class FlavorFile
    {
        [JsonPropertyName("flavor")]
        public string? Flavor { get; set; }

        [JsonPropertyName("upstream")]
        public string? Upstream { get; set; }

        [JsonPropertyName("commit")]
        public string? Commit { get; set; }

        [JsonPropertyName("verified")]
        public bool Verified { get; set; }

        [JsonPropertyName("rootNamespace")]
        public string? RootNamespace { get; set; }

        [JsonPropertyName("helperOid")]
        public string? HelperOid { get; set; }

        [JsonPropertyName("templates")]
        public Dictionary<string, string>? Templates { get; set; }
    }
}
