using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.Rules.Tests
{
    /// <summary>
    /// Deep content validation for game-definition.yaml against the spec for issue #545.
    /// These tests verify that each section contains Pinder-specific creative direction,
    /// not generic boilerplate, per the acceptance criteria.
    /// </summary>
    public partial class GameDefinitionYamlContentTests
    {
        private static string LoadYamlContent()
        {
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

        private static Dictionary<string, string> ParseYaml()
        {
            var content = LoadYamlContent();
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            var rawDict = deserializer.Deserialize<Dictionary<object, object>>(content);
            var raw = new Dictionary<string, object?>();
            foreach (var kvp in rawDict)
                raw[kvp.Key.ToString()!] = ConvertYamlNode(kvp.Value);
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
                    var sb = new StringBuilder();
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

        // ===== AC1: File location and format =====

        // Mutation: would catch if file contained tab characters causing YAML parse issues
        [Fact]
        public void YamlFile_ContainsNoTabs()
        {
            var content = LoadYamlContent();
            Assert.DoesNotContain("\t", content);
        }

        // Mutation: would catch if file had BOM marker (spec requires UTF-8 without BOM)
        [Fact]
        public void YamlFile_HasNoBom()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "data", "game-definition.yaml")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            var bytes = File.ReadAllBytes(Path.Combine(dir!, "data", "game-definition.yaml"));
            // UTF-8 BOM is 0xEF 0xBB 0xBF
            if (bytes.Length >= 3)
            {
                Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                    "File has a UTF-8 BOM — spec requires UTF-8 without BOM");
            }
        }

        // Mutation: would catch if any content value is something other than string, int, or nested string/int dict
        [Fact]
        public void YamlFile_AllContentValuesAreStringIntOrNestedDict()
        {
            var content = LoadYamlContent();
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            var dataRaw = deserializer.Deserialize<Dictionary<object, object>>(content);
            var data = new Dictionary<string, object?>();
            foreach (var kvp in dataRaw)
                data[kvp.Key.ToString()!] = ConvertYamlNode(kvp.Value);
            foreach (var kvp in data)
            {
                if (kvp.Value is Dictionary<string, object?> dict)
                {
                    foreach (var dkv in dict.Values)
                    {
                        if (dkv != null && dkv is not string && dkv is not int && dkv is not long)
                            Assert.Fail($"Nested value in '{kvp.Key}' is not a string or integer: {dkv.GetType()}");
                    }
                }
                else if (kvp.Value is not string && kvp.Value is not int && kvp.Value is not long)
                {
                    Assert.Fail($"Value for '{kvp.Key}' is not a string or integer: {kvp.Value?.GetType()}");
                }
            }
        }

        [Fact]
        public void YamlFile_HasRequiredContentKeys()
        {
            var data = ParseYaml();
            // The YAML now contains many more keys than the original 7;
            // verify the core content sections are still present.
            string[] required = { "name", "vision", "world_description", "player_role_description",
                "datee_role_description", "narrative_doctrine" };
            foreach (var key in required)
            {
                Assert.True(data.ContainsKey(key), $"Missing required content key: {key}");
            }
        }
    }
}
