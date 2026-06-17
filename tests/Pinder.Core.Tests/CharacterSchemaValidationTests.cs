using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Direct schema-file validation tests for issue #814. Walks the
    /// JSON-Schema document at <c>data/characters/character-schema.json</c>
    /// against the 6 starter files. Hand-rolled to avoid pulling in a new
    /// runtime NuGet dep; covers the subset of Draft-7 actually used by
    /// <c>character-schema.json</c> (object/array/integer/string,
    /// <c>required</c>, <c>type</c>, <c>const</c>, <c>pattern</c>,
    /// <c>minimum</c>, <c>maximum</c>, <c>minLength</c>,
    /// <c>additionalProperties: false</c>, nested object schemas).
    ///
    /// If a future schema revision uses richer keywords this validator
    /// won't notice them; that's fine — the parser's own tests already
    /// pin behaviour and this file's job is just to demonstrate that the
    /// declared schema and the starter files agree about shape.
    /// </summary>
    [Trait("Category", "Characters")]
    public class CharacterSchemaValidationTests
    {
        private static string RepoRoot
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir, "data")) &&
                        Directory.Exists(Path.Combine(dir, "src")))
                        return dir;
                    dir = Directory.GetParent(dir)?.FullName;
                }
                throw new InvalidOperationException("Cannot find repo root from " + AppContext.BaseDirectory);
            }
        }

        private static readonly string[] AllStarterSlugs =
            { "brick", "gerald", "reuben", "sable", "velvet", "zyx" };

        [Theory]
        [InlineData("brick")]
        [InlineData("gerald")]
        [InlineData("reuben")]
        [InlineData("sable")]
        [InlineData("velvet")]
        [InlineData("zyx")]
        public void StarterFile_ValidatesAgainstCharacterSchema(string slug)
        {
            string schemaPath = Path.Combine(RepoRoot, "data", "characters", "character-schema.json");
            string filePath = Path.Combine(RepoRoot, "data", "characters", $"{slug}.json");

            using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
            using var fileDoc = JsonDocument.Parse(File.ReadAllText(filePath));

            var errors = new List<string>();
            Validate(schemaDoc.RootElement, fileDoc.RootElement, slug, errors);

            Assert.True(errors.Count == 0,
                $"{slug}.json failed schema validation:\n  - " + string.Join("\n  - ", errors));
        }

        [Fact]
        public void Schema_RejectsFileWithExtraTopLevelProperty()
        {
            string schemaPath = Path.Combine(RepoRoot, "data", "characters", "character-schema.json");
            string filePath = Path.Combine(RepoRoot, "data", "characters", "gerald.json");

            // Inject an extra property at the top level. Schema has
            // additionalProperties: false, so this MUST fail.
            string mutated = File.ReadAllText(filePath).TrimEnd().TrimEnd('}')
                + ",\n  \"unknown_extra\": \"nope\"\n}";

            using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
            using var fileDoc = JsonDocument.Parse(mutated);

            var errors = new List<string>();
            Validate(schemaDoc.RootElement, fileDoc.RootElement, "mutated", errors);

            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("unknown_extra"));
        }

        [Fact]
        public void Schema_RejectsFileWithWrongSchemaVersionConst()
        {
            string schemaPath = Path.Combine(RepoRoot, "data", "characters", "character-schema.json");
            string filePath = Path.Combine(RepoRoot, "data", "characters", "gerald.json");

            // Replace schema_version: 2 with schema_version: 99.
            string mutated = File.ReadAllText(filePath).Replace(
                "\"schema_version\": 2,", "\"schema_version\": 99,");

            using var schemaDoc = JsonDocument.Parse(File.ReadAllText(schemaPath));
            using var fileDoc = JsonDocument.Parse(mutated);

            var errors = new List<string>();
            Validate(schemaDoc.RootElement, fileDoc.RootElement, "mutated", errors);

            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("schema_version"));
        }

        // -- minimal Draft-7 walker, tailored to this schema -------------------

        private static void Validate(JsonElement schema, JsonElement instance, string path, List<string> errors)
        {
            // type
            if (schema.TryGetProperty("type", out var typeProp))
            {
                string type = typeProp.GetString()!;
                if (!MatchesType(type, instance))
                {
                    errors.Add($"{path}: expected type '{type}', got '{instance.ValueKind}'");
                    return;
                }
            }

            // const (we only use it on integer for schema_version)
            if (schema.TryGetProperty("const", out var constProp))
            {
                if (!JsonElementsEqual(constProp, instance))
                    errors.Add($"{path}: expected const value {constProp}, got {instance}");
            }

            // pattern (string only — used for character_id UUIDv4 check)
            if (schema.TryGetProperty("pattern", out var patternProp) && instance.ValueKind == JsonValueKind.String)
            {
                var rx = new System.Text.RegularExpressions.Regex(patternProp.GetString()!);
                if (!rx.IsMatch(instance.GetString()!))
                    errors.Add($"{path}: value '{instance.GetString()}' does not match pattern {patternProp.GetString()}");
            }

            if (schema.TryGetProperty("minLength", out var minLen) && instance.ValueKind == JsonValueKind.String)
            {
                if (instance.GetString()!.Length < minLen.GetInt32())
                    errors.Add($"{path}: string shorter than minLength {minLen.GetInt32()}");
            }

            if (schema.TryGetProperty("minimum", out var min) && instance.ValueKind == JsonValueKind.Number)
            {
                if (instance.GetDouble() < min.GetDouble())
                    errors.Add($"{path}: value {instance.GetDouble()} below minimum {min.GetDouble()}");
            }

            if (schema.TryGetProperty("maximum", out var max) && instance.ValueKind == JsonValueKind.Number)
            {
                if (instance.GetDouble() > max.GetDouble())
                    errors.Add($"{path}: value {instance.GetDouble()} above maximum {max.GetDouble()}");
            }

            // object: required + properties + additionalProperties
            if (instance.ValueKind == JsonValueKind.Object)
            {
                var declaredProps = new HashSet<string>();
                if (schema.TryGetProperty("properties", out var props))
                {
                    foreach (var p in props.EnumerateObject())
                        declaredProps.Add(p.Name);
                }

                if (schema.TryGetProperty("required", out var req))
                {
                    foreach (var r in req.EnumerateArray())
                    {
                        string name = r.GetString()!;
                        if (!instance.TryGetProperty(name, out _))
                            errors.Add($"{path}: missing required property '{name}'");
                    }
                }

                bool additionalAllowed = true;
                if (schema.TryGetProperty("additionalProperties", out var addProp) &&
                    addProp.ValueKind == JsonValueKind.False)
                {
                    additionalAllowed = false;
                }

                foreach (var member in instance.EnumerateObject())
                {
                    if (declaredProps.Contains(member.Name))
                    {
                        var subSchema = schema.GetProperty("properties").GetProperty(member.Name);
                        Validate(subSchema, member.Value, $"{path}.{member.Name}", errors);
                    }
                    else if (!additionalAllowed)
                    {
                        errors.Add($"{path}: unexpected property '{member.Name}' (additionalProperties: false)");
                    }
                    else if (schema.TryGetProperty("additionalProperties", out var addSchema) &&
                             addSchema.ValueKind == JsonValueKind.Object)
                    {
                        Validate(addSchema, member.Value, $"{path}.{member.Name}", errors);
                    }
                }
            }

            // array: items
            if (instance.ValueKind == JsonValueKind.Array &&
                schema.TryGetProperty("items", out var itemsSchema))
            {
                int i = 0;
                foreach (var elem in instance.EnumerateArray())
                {
                    Validate(itemsSchema, elem, $"{path}[{i}]", errors);
                    i++;
                }
            }
        }

        private static bool MatchesType(string declared, JsonElement instance)
        {
            switch (declared)
            {
                case "object":  return instance.ValueKind == JsonValueKind.Object;
                case "array":   return instance.ValueKind == JsonValueKind.Array;
                case "string":  return instance.ValueKind == JsonValueKind.String;
                case "integer":
                    return instance.ValueKind == JsonValueKind.Number &&
                           instance.TryGetInt64(out _) &&
                           !instance.GetRawText().Contains('.');
                case "number":  return instance.ValueKind == JsonValueKind.Number;
                case "boolean": return instance.ValueKind == JsonValueKind.True ||
                                       instance.ValueKind == JsonValueKind.False;
                case "null":    return instance.ValueKind == JsonValueKind.Null;
                default:        return true; // unknown declared type — accept
            }
        }

        private static bool JsonElementsEqual(JsonElement a, JsonElement b)
        {
            if (a.ValueKind != b.ValueKind) return false;
            switch (a.ValueKind)
            {
                case JsonValueKind.Number: return a.GetRawText() == b.GetRawText();
                case JsonValueKind.String: return a.GetString() == b.GetString();
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    return true;
                default:
                    return a.GetRawText() == b.GetRawText();
            }
        }
    }
}
