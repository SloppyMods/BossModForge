using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;

namespace BossModForge.Core.Model;

public static class EncounterModelJson
{
    private const string SchemaResource = "BossModForge.Core.em.v1.schema.json";

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();
    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

    public static string Serialize(EncounterModel em)
    {
        var node = JsonSerializer.SerializeToNode(em, SerializerOptions) as JsonObject
            ?? throw new InvalidOperationException("EncounterModel did not serialize to a JSON object.");
        SortKeys(node);
        MoveSchemaVersionFirst(node);
        var json = node.ToJsonString(SerializerOptions);
        if (!json.EndsWith('\n'))
            json += "\n";
        return json.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static EncounterModel Deserialize(string json)
    {
        var em = JsonSerializer.Deserialize<EncounterModel>(json, SerializerOptions)
            ?? throw new JsonException("EncounterModel JSON deserialized to null.");
        return em;
    }

    public static ImmutableArray<string> Validate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var results = Schema.Value.Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid)
            return [];

        var errors = ImmutableArray.CreateBuilder<string>();
        CollectErrors(results, errors);
        if (errors.Count == 0)
            errors.Add("schema validation failed");
        return errors.ToImmutable();
    }

    private static void CollectErrors(EvaluationResults node, ImmutableArray<string>.Builder errors)
    {
        if (!node.IsValid && node.Errors is { Count: > 0 } map)
        {
            foreach (var (keyword, message) in map)
                errors.Add($"{node.InstanceLocation} {keyword}: {message}");
        }

        if (node.Details is { Count: > 0 } details)
        {
            foreach (var child in details)
                CollectErrors(child, errors);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        options.Converters.Add(new DateTimeOConverter());
        options.Converters.Add(new UIntStringDictionaryConverter());
        options.Converters.Add(new JsonElementDictionaryConverter());
        return options;
    }

    private static JsonSchema LoadSchema()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(SchemaResource)
            ?? throw new InvalidOperationException($"Embedded schema '{SchemaResource}' is missing.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }

    private static void SortKeys(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var properties = obj.ToList();
                foreach (var (key, _) in properties)
                    obj.Remove(key);
                foreach (var (key, value) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    SortKeys(value);
                    obj[key] = value;
                }

                break;
            }
            case JsonArray array:
                foreach (var child in array)
                    SortKeys(child);
                break;
        }
    }

    private static void MoveSchemaVersionFirst(JsonObject obj)
    {
        if (!obj.TryGetPropertyValue("schemaVersion", out var version))
            return;
        obj.Remove("schemaVersion");
        var rest = obj.ToList();
        obj.Clear();
        obj["schemaVersion"] = version;
        foreach (var (key, value) in rest)
            obj[key] = value;
    }

    private sealed class DateTimeOConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => DateTime.Parse(reader.GetString() ?? throw new JsonException("DateTime was null."), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }

    private sealed class UIntStringDictionaryConverter : JsonConverter<ImmutableSortedDictionary<uint, string>>
    {
        public override ImmutableSortedDictionary<uint, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected object for uint→string map.");
            var builder = ImmutableSortedDictionary.CreateBuilder<uint, string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;
                var key = uint.Parse(reader.GetString() ?? "", CultureInfo.InvariantCulture);
                reader.Read();
                builder[key] = reader.GetString() ?? "";
            }

            return builder.ToImmutable();
        }

        public override void Write(Utf8JsonWriter writer, ImmutableSortedDictionary<uint, string> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, name) in value.OrderBy(kv => kv.Key.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal))
                writer.WriteString(key.ToString(CultureInfo.InvariantCulture), name);
            writer.WriteEndObject();
        }
    }

    private sealed class JsonElementDictionaryConverter : JsonConverter<ImmutableSortedDictionary<string, JsonElement>>
    {
        public override ImmutableSortedDictionary<string, JsonElement> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected object for overrides.values.");
            using var doc = JsonDocument.ParseValue(ref reader);
            var builder = ImmutableSortedDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
                builder[prop.Name] = prop.Value.Clone();
            return builder.ToImmutable();
        }

        public override void Write(Utf8JsonWriter writer, ImmutableSortedDictionary<string, JsonElement> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, element) in value.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                element.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}
