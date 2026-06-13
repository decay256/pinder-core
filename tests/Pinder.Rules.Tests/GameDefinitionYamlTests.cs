using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.Rules.Tests
{
    /// <summary>
    /// Validates that game-definition.yaml is well-formed and contains all
    /// required sections for GameDefinition.LoadFrom (#543) to parse.
    /// </summary>
    public class GameDefinitionYamlTests
    {
        private static readonly string[] RequiredContentKeys = new[]
        {
            "name",
            "vision",
            "world_description",
            "player_avatar_role_description",
            "datee_role_description",
            "narrative_doctrine"
        };

        /// <summary>All top-level keys expected in the current game-definition.yaml.</summary>
        private static readonly string[] AllExpectedKeys = new[]
        {
            "name",
            "max_turns",
            "max_dialogue_options",
            "max_delivery_words",
            "vision",
            "world_description",
            "player_avatar_role_description",
            "datee_role_description",
            "datee_friction",
            "datee_curiosity",
            "player_avatar_probing",
            "narrative_doctrine",
            "delivery_rules",
            "conversation_arc",
            "dramatic_craft",
            "improvement_prompt",
            "global_dc_bias",
            "horniness_time_modifiers",
            "steering_prompt"
        };

        private static string LoadYamlContent()
        {
            // Walk up from test bin to repo root, then into data/
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "game-definition.yaml")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            if (dir == null)
                throw new FileNotFoundException("Could not find data/game-definition.yaml from test directory");
            return File.ReadAllText(Path.Combine(dir, "data", "game-definition.yaml"));
        }

        /// <summary>
        /// Recursively convert YamlDotNet's Dictionary&lt;object,object&gt; nodes
        /// (and scalar integer values) into Dictionary&lt;string,object?&gt;.
        /// Needed because Deserialize&lt;Dictionary&lt;string,object?&gt;&gt; fails when
        /// nested mappings contain integer values (horniness_time_modifiers).
        /// </summary>
        private static object? ConvertYamlNode(object? node)
        {
            if (node == null) return null;
            if (node is string s) return s;
            if (node is int i) return i;
            if (node is long l) return l;
            if (node is Dictionary<object, object> dict)
            {
                var result = new Dictionary<string, object?>();
                foreach (var kvp in dict)
                    result[kvp.Key.ToString()!] = ConvertYamlNode(kvp.Value);
                return result;
            }
            return node.ToString();
        }

        private static Dictionary<string, object?> ParseYamlRaw()
        {
            var content = LoadYamlContent();
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            var raw = deserializer.Deserialize<Dictionary<object, object>>(content);
            var result = new Dictionary<string, object?>();
            foreach (var kvp in raw)
                result[kvp.Key.ToString()!] = ConvertYamlNode(kvp.Value);
            return result;
        }

        /// <summary>
        /// Parse YAML and return string values for content-section keys.
        /// Nested dicts are flattened by concatenating their values; integers are stringified.
        /// Null-valued keys are omitted from the result.
        /// </summary>
        private static Dictionary<string, string> ParseYaml()
        {
            var raw = ParseYamlRaw();
            var result = new Dictionary<string, string>();
            foreach (var kvp in raw)
            {
                if (kvp.Value == null) continue;
                if (kvp.Value is string s)
                {
                    result[kvp.Key] = s;
                }
                else if (kvp.Value is int i)
                {
                    result[kvp.Key] = i.ToString();
                }
                else if (kvp.Value is Dictionary<string, object?> dict)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var dk in dict.Values)
                    {
                        if (dk != null) sb.AppendLine(dk.ToString());
                    }
                    var flat = sb.ToString().TrimEnd();
                    if (!string.IsNullOrEmpty(flat))
                        result[kvp.Key] = flat;
                }
            }
            return result;
        }

        [Fact]
        public void YamlFile_IsValidYaml()
        {
            // Should not throw
            var data = ParseYaml();
            Assert.NotNull(data);
        }

        [Fact]
        public void YamlFile_ContainsAllRequiredKeys()
        {
            var raw = ParseYamlRaw();
            foreach (var key in RequiredContentKeys)
            {
                Assert.True(raw.ContainsKey(key), $"Missing required key: {key}");
            }
        }

        [Fact]
        public void YamlFile_HasNoExtraKeys()
        {
            var raw = ParseYamlRaw();
            var extraKeys = raw.Keys.Except(AllExpectedKeys).ToList();
            if (extraKeys.Count > 0)
            {
                Assert.Fail($"Unexpected top-level key(s): {string.Join(", ", extraKeys)}. " +
                    "Update AllExpectedKeys if this addition is intentional.");
            }
        }

        [Theory]
        [InlineData("name")]
        [InlineData("vision")]
        [InlineData("world_description")]
        [InlineData("player_avatar_role_description")]
        [InlineData("datee_role_description")]
        [InlineData("narrative_doctrine")]
        public void YamlFile_ValueIsNonEmpty(string key)
        {
            var raw = ParseYamlRaw();
            Assert.True(raw.ContainsKey(key), $"Missing key: {key}");
            Assert.True(raw[key] is string s && !string.IsNullOrWhiteSpace(s),
                $"Value for '{key}' is not a non-empty string (type: {raw[key]?.GetType().Name ?? "null"})");
        }

        [Fact]
        public void YamlFile_NameIsPinder()
        {
            var data = ParseYaml();
            Assert.Equal("Pinder", data["name"]);
        }

        [Fact]
        public void YamlFile_VisionMentionsPinderSpecificConcepts()
        {
            var data = ParseYaml();
            var vision = data["vision"];
            // Must reference the core premise
            Assert.Contains("sentient penis", vision, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("game master", vision, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("RPG", vision, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("dialogue", vision, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void YamlFile_WorldDescriptionMentionsStatPairs()
        {
            var data = ParseYaml();
            var world = data["world_description"];
            // Must reference the 6 stat pairs
            Assert.Contains("Charm", world);
            Assert.Contains("Madness", world);
            Assert.Contains("Rizz", world);
            Assert.Contains("Despair", world);
            Assert.Contains("Honesty", world);
            Assert.Contains("Denial", world);
            Assert.Contains("Chaos", world);
            Assert.Contains("Fixation", world);
            Assert.Contains("Wit", world);
            Assert.Contains("Dread", world);
            Assert.Contains("Self-Awareness", world);
            Assert.Contains("Overthinking", world);
        }

        [Fact]
        public void YamlFile_WorldDescriptionMentionsInterestMeter()
        {
            var data = ParseYaml();
            var world = data["world_description"];
            var playerRole = data["player_avatar_role_description"];
            var dateeRole = data["datee_role_description"];
            Assert.Contains("Interest", world, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("25", playerRole);
            Assert.Contains("Bored", dateeRole);
            Assert.Contains("Date Secured", playerRole);
        }

        [Fact]
        public void YamlFile_MetaContractMentionsNeverBreakCharacter()
        {
            var data = ParseYaml();
            var meta = data["narrative_doctrine"];
            Assert.Contains("Never break character", meta);
            Assert.Contains("ENGINE", meta);
        }

        [Fact]
        public void YamlFile_WritingRulesMentionsTextingRegister()
        {
            var data = ParseYaml();
            var rules = data["narrative_doctrine"];
            Assert.Contains("texting", rules, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("asterisk", rules, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void YamlFile_ContentSectionsHaveSubstantiveLength()
        {
            var data = ParseYaml();
            // Each content section should be substantial (> 200 chars)
            foreach (var key in RequiredContentKeys.Where(k => k != "name"))
            {
                Assert.True(data.ContainsKey(key), $"Section '{key}' not found in parsed YAML");
                Assert.True(data[key].Length > 200,
                    $"Section '{key}' is only {data[key].Length} chars — expected substantial content (>200)");
            }
        }
    }
}
