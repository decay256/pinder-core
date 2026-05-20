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
            "player_role_description",
            "opponent_role_description",
            "meta_contract",
            "writing_rules"
        };

        /// <summary>All top-level keys expected in the current game-definition.yaml.</summary>
        private static readonly string[] AllExpectedKeys = new[]
        {
            "name",
            "max_turns",
            "max_dialogue_options",
            "vision",
            "world_description",
            "texting_psychology",
            "player_role_description",
            "opponent_role_description",
            "opponent_friction",
            "opponent_curiosity",
            "player_probing",
            "meta_contract",
            "writing_rules",
            "revelation_over_statement",
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

        private static Dictionary<string, object?> ParseYamlRaw()
        {
            var content = LoadYamlContent();
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            return deserializer.Deserialize<Dictionary<string, object?>>(content);
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
                else if (kvp.Value is Dictionary<object, object> dict)
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
        [InlineData("player_role_description")]
        [InlineData("opponent_role_description")]
        [InlineData("meta_contract")]
        [InlineData("writing_rules")]
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
            Assert.Contains("sentient peni", vision, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("dating", vision, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("comedy", vision, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("shadow", vision, StringComparison.OrdinalIgnoreCase);
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
            Assert.Contains("Interest", world);
            Assert.Contains("25", world);
            Assert.Contains("Bored", world);
            Assert.Contains("Date Secured", world);
        }

        [Fact]
        public void YamlFile_MetaContractMentionsNeverBreakCharacter()
        {
            var data = ParseYaml();
            var meta = data["meta_contract"];
            Assert.Contains("Never break character", meta);
            Assert.Contains("ENGINE", meta);
        }

        [Fact]
        public void YamlFile_WritingRulesMentionsTextingRegister()
        {
            var data = ParseYaml();
            var rules = data["writing_rules"];
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
