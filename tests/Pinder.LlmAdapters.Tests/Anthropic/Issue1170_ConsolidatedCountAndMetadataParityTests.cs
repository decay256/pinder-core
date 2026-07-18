using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Pinder.Core.Stats;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public sealed class Issue1170_ConsolidatedCountAndMetadataParityTests
    {
        // ═══════════════════════════════════════════════════════════════
        // 1. Configurable Option Count / Distinct Stats / Non-leakage
        // ═══════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void ParseDialogueOptionsText_WithDrawnStatCountN_ReturnsExactlyNOptions(int n)
        {
            // Create N distinct drawn stats
            var allStats = new[] { StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos, StatType.Rizz, StatType.SelfAwareness };
            var drawnStats = allStats.Take(n).ToArray();

            // Feed an LLM response containing fewer, equal, and more option headers than N to test padding/cap
            var llmResponse = string.Join("\n", Enumerable.Range(1, n + 2).Select(i => 
                $"OPTION_{i} [STAT: {drawnStats[i % n]}] \"Text option {i}\" [CALLBACK: none] [COMBO: none]"));

            var result = DialogueOptionParsers.ParseDialogueOptionsText(llmResponse, drawnStats);

            // Exactly N options returned
            Assert.Equal(n, result.Length);

            // Distinct stats contract
            var resultStats = result.Select(r => r.Stat).ToList();
            Assert.Equal(n, resultStats.Distinct().Count());

            // All stats within the drawn set
            foreach (var stat in resultStats)
            {
                Assert.Contains(stat, drawnStats);
            }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void ParseDialogueOptionsTool_WithDrawnStatCountN_ReturnsExactlyNOptions(int n)
        {
            var allStats = new[] { StatType.Charm, StatType.Honesty, StatType.Wit, StatType.Chaos, StatType.Rizz, StatType.SelfAwareness };
            var drawnStats = allStats.Take(n).ToArray();

            var optionsArray = new JArray();
            for (int i = 0; i < n; i++)
            {
                optionsArray.Add(new JObject
                {
                    ["stat"] = drawnStats[i % n].ToString(),
                    ["text"] = $"Tool text option {i}",
                    ["callback"] = "none",
                    ["combo"] = "none"
                });
            }
            var toolInput = new JObject { ["options"] = optionsArray };

            var result = DialogueOptionParsers.ParseDialogueOptionsTool(toolInput, drawnStats);

            Assert.NotNull(result);
            Assert.Equal(n, result!.Length);

            var resultStats = result.Select(r => r.Stat).ToList();
            Assert.Equal(n, resultStats.Distinct().Count());

            foreach (var stat in resultStats)
            {
                Assert.Contains(stat, drawnStats);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 2. Text-fallback parse of NUMBERED OPTION_1..N block
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void TextFallbackParse_OfNumberedOptionsBlock_YieldsNRealOptions()
        {
            var drawnStats = new[] { StatType.Charm, StatType.Honesty, StatType.Wit };
            var n = drawnStats.Length;

            var input = @"OPTION_1 [STAT: Charm] ""Charming message"" [CALLBACK: none] [COMBO: none]
OPTION_2 [STAT: Honesty] ""Honest message"" [CALLBACK: none] [COMBO: none]
OPTION_3 [STAT: Wit] ""Witty message"" [CALLBACK: none] [COMBO: none]";

            var result = DialogueOptionParsers.ParseDialogueOptionsText(input, drawnStats);

            Assert.Equal(n, result.Length);
            Assert.Equal("Charming message", result[0].IntendedText);
            Assert.Equal("Honest message", result[1].IntendedText);
            Assert.Equal("Witty message", result[2].IntendedText);
            
            // Prove no padded fallbacks were used
            foreach (var opt in result)
            {
                Assert.NotEqual("...", opt.IntendedText);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 3. Tool-schema builder parametrizes minItems/maxItems
        // ═══════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void ToolSchema_GetDialogueOptions_ParametrizesItemsExactly(int n)
        {
            var schema = ToolSchemas.GetDialogueOptions(n);
            Assert.Equal(n, schema.InputSchema["properties"]!["options"]!["minItems"]!.Value<int>());
            Assert.Equal(n, schema.InputSchema["properties"]!["options"]!["maxItems"]!.Value<int>());
        }

        // ═══════════════════════════════════════════════════════════════
        // 4. Metadata field-parity test
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void ToolSchema_ExcludesEngineDerivedGameplayMetadata()
        {
            // Get regex tags via reflection from DialogueOptionParsers
            var parserType = typeof(DialogueOptionParsers);
            var regexFields = parserType.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Where(f => f.FieldType == typeof(Regex) && f.Name.EndsWith("Regex"))
                .ToList();

            var textTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in regexFields)
            {
                var regex = (Regex)field.GetValue(null)!;
                var pattern = regex.ToString();
                var match = Regex.Match(pattern, @"\\\[([A-Z_]+):");
                if (match.Success)
                {
                    textTags.Add(match.Groups[1].Value);
                }
            }

            Assert.Contains("STAT", textTags);
            Assert.Contains("CALLBACK", textTags);
            Assert.Contains("COMBO", textTags);
            Assert.DoesNotContain("TELL_BONUS", textTags);
            Assert.Equal(3, textTags.Count);

            // Get schema fields from ToolSchemas
            var toolSchema = ToolSchemas.GetDialogueOptions(4);
            var optionsProps = toolSchema.InputSchema["properties"]?["options"]?["items"]?["properties"] as JObject;
            Assert.NotNull(optionsProps);

            var schemaFields = optionsProps.Properties()
                .Select(p => p.Name)
                .Where(name => name != "text")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains("stat", schemaFields);
            Assert.Contains("callback", schemaFields);
            Assert.Contains("combo", schemaFields);
            Assert.DoesNotContain("tell_bonus", schemaFields);
            Assert.DoesNotContain("weakness_window", schemaFields);
            Assert.Equal(3, schemaFields.Count);

            Assert.True(schemaFields.SetEquals(new[] { "stat", "callback", "combo" }), "Structured tool schema must only ask for model-authored fields; gameplay metadata remains engine-derived.");
        }

        // ═══════════════════════════════════════════════════════════════
        // 5. Revert-proof guard
        // ═══════════════════════════════════════════════════════════════

        [Fact]
        public void RevertProofGuard_AssertsNoHardcodedFourInOptionPipeline()
        {
            // Find DialogueOptionParsers.cs relative to test directory
            var currentDir = Directory.GetCurrentDirectory();
            var searchPaths = new[]
            {
                Path.Combine(currentDir, "../../../src/Pinder.LlmAdapters/Anthropic/DialogueOptionParsers.cs"),
                Path.Combine(currentDir, "../../../../../src/Pinder.LlmAdapters/Anthropic/DialogueOptionParsers.cs"),
                Path.Combine(currentDir, "src/Pinder.LlmAdapters/Anthropic/DialogueOptionParsers.cs"),
                Path.Combine(currentDir, "../src/Pinder.LlmAdapters/Anthropic/DialogueOptionParsers.cs"),
                "/root/projects/pinder-core/.worktrees/1170/src/Pinder.LlmAdapters/Anthropic/DialogueOptionParsers.cs"
            };

            string? sourcePath = null;
            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    sourcePath = path;
                    break;
                }
            }

            Assert.NotNull(sourcePath);
            var sourceContent = File.ReadAllText(sourcePath!);

            // Normalize whitespace and remove comments so we only check code
            var cleanedContent = Regex.Replace(sourceContent, @"//.*|/\*.*?\*/", "", RegexOptions.Singleline);

            // Assert that there are NO hardcoded literals for DialogueOption cap/pad limit of 4
            Assert.DoesNotContain("parsed.Count >= 4", cleanedContent);
            Assert.DoesNotContain("result.Count < 4", cleanedContent);
            Assert.DoesNotContain("result.Count > 4", cleanedContent);
            Assert.DoesNotContain("GetRange(0, 4)", cleanedContent);
        }
    }
}
