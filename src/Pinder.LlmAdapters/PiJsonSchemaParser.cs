using System;
using System.Collections.Generic;
using System.Text.Json;
using Pi.AI;

namespace Pinder.LlmAdapters.Pi
{
    internal static class PiJsonSchemaParser
    {
        public static JsonSchema Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("JSON schema must not be blank.", nameof(json));
            using JsonDocument document = JsonDocument.Parse(json);
            return ParseSchema(document.RootElement);
        }

        private static JsonSchema ParseSchema(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new JsonException("A Pi tool schema must be a JSON object.");
            var schema = new JsonSchema();
            if (element.TryGetProperty("type", out JsonElement type)) schema.Types = ReadTypes(type);
            if (element.TryGetProperty("properties", out JsonElement properties))
                schema.Properties = ReadProperties(properties);
            if (element.TryGetProperty("required", out JsonElement required)) schema.Required = ReadStrings(required);
            if (element.TryGetProperty("items", out JsonElement items))
            {
                if (items.ValueKind == JsonValueKind.Array) schema.TupleItems = ReadSchemas(items);
                else schema.Items = ParseSchema(items);
            }
            if (element.TryGetProperty("additionalProperties", out JsonElement additional))
            {
                if (additional.ValueKind == JsonValueKind.True || additional.ValueKind == JsonValueKind.False)
                    schema.AdditionalPropertiesAllowed = additional.GetBoolean();
                else schema.AdditionalProperties = ParseSchema(additional);
            }
            if (element.TryGetProperty("allOf", out JsonElement allOf)) schema.AllOf = ReadSchemas(allOf);
            if (element.TryGetProperty("anyOf", out JsonElement anyOf)) schema.AnyOf = ReadSchemas(anyOf);
            if (element.TryGetProperty("oneOf", out JsonElement oneOf)) schema.OneOf = ReadSchemas(oneOf);
            if (element.TryGetProperty("enum", out JsonElement enumValues)) schema.Enum = ReadValues(enumValues);
            if (element.TryGetProperty("const", out JsonElement constant))
            {
                schema.Constant = ReadValue(constant);
                schema.HasConstant = true;
            }
            schema.Description = ReadString(element, "description");
            if (element.TryGetProperty("default", out JsonElement defaultValue)) schema.Default = ReadValue(defaultValue);
            schema.Minimum = ReadDecimal(element, "minimum");
            schema.Maximum = ReadDecimal(element, "maximum");
            schema.ExclusiveMinimum = ReadDecimal(element, "exclusiveMinimum");
            schema.ExclusiveMaximum = ReadDecimal(element, "exclusiveMaximum");
            schema.MinimumLength = ReadInt(element, "minLength");
            schema.MaximumLength = ReadInt(element, "maxLength");
            schema.Pattern = ReadString(element, "pattern");
            schema.MinimumItems = ReadInt(element, "minItems");
            schema.MaximumItems = ReadInt(element, "maxItems");
            return schema;
        }

        private static IReadOnlyList<string> ReadTypes(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String) return new[] { element.GetString()! };
            return ReadStrings(element);
        }

        private static IReadOnlyDictionary<string, JsonSchema> ReadProperties(JsonElement element)
        {
            var result = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject()) result[property.Name] = ParseSchema(property.Value);
            return result;
        }

        private static IReadOnlyList<JsonSchema> ReadSchemas(JsonElement element)
        {
            var result = new List<JsonSchema>();
            foreach (JsonElement item in element.EnumerateArray()) result.Add(ParseSchema(item));
            return result;
        }

        private static IReadOnlyList<string> ReadStrings(JsonElement element)
        {
            var result = new List<string>();
            foreach (JsonElement item in element.EnumerateArray()) result.Add(item.GetString()!);
            return result;
        }

        private static IReadOnlyList<object?> ReadValues(JsonElement element)
        {
            var result = new List<object?>();
            foreach (JsonElement item in element.EnumerateArray()) result.Add(ReadValue(item));
            return result;
        }

        private static object? ReadValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Number:
                    return element.TryGetInt64(out long integer) ? (object)integer : element.GetDecimal();
                case JsonValueKind.Array:
                    var list = new List<object?>();
                    foreach (JsonElement item in element.EnumerateArray()) list.Add(ReadValue(item));
                    return list;
                case JsonValueKind.Object:
                    var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (JsonProperty property in element.EnumerateObject()) dictionary[property.Name] = ReadValue(property.Value);
                    return dictionary;
                default: throw new JsonException("Unsupported JSON schema value.");
            }
        }

        private static string? ReadString(JsonElement element, string name)
            => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;

        private static decimal? ReadDecimal(JsonElement element, string name)
            => element.TryGetProperty(name, out JsonElement value) && value.TryGetDecimal(out decimal result)
                ? result : (decimal?)null;

        private static int? ReadInt(JsonElement element, string name)
            => element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
                ? result : (int?)null;
    }
}
