using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Pinder.LlmAdapters.Anthropic;
using Xunit;

namespace Pinder.LlmAdapters.Tests.Anthropic
{
    public sealed class ToolSchemasAssetTests
    {
        [Fact]
        public void DialogueOptions_LoadsDescriptionAndSchemaFromAsset()
        {
            var asset = LoadSchemaAsset("anthropic_submit_dialogue_options_tool.json");
            var tool = ToolSchemas.GetDialogueOptions(3);

            Assert.Equal(asset.Value<string>("name"), tool.Name);
            Assert.Equal(asset.Value<string>("description"), tool.Description);

            var assetProps = asset["input_schema"]!["properties"]!["options"]!["items"]!["properties"] as JObject;
            var toolProps = tool.InputSchema["properties"]!["options"]!["items"]!["properties"] as JObject;

            Assert.Equal(
                assetProps!["text"]!["description"]!.Value<string>(),
                toolProps!["text"]!["description"]!.Value<string>());
            Assert.Equal(3, tool.InputSchema["properties"]!["options"]!["minItems"]!.Value<int>());
            Assert.Equal(3, tool.InputSchema["properties"]!["options"]!["maxItems"]!.Value<int>());
        }

        [Fact]
        public void DateeResponse_LoadsDescriptionAndSchemaFromAsset()
        {
            var asset = LoadSchemaAsset("anthropic_submit_datee_response_tool.json");

            Assert.Equal(asset.Value<string>("name"), ToolSchemas.DateeResponse.Name);
            Assert.Equal(asset.Value<string>("description"), ToolSchemas.DateeResponse.Description);
            Assert.Equal(
                asset["input_schema"]!["properties"]!["tell"]!["description"]!.Value<string>(),
                ToolSchemas.DateeResponse.InputSchema["properties"]!["tell"]!["description"]!.Value<string>());
        }

        [Fact]
        public void Improvement_LoadsDescriptionAndSchemaFromAsset()
        {
            var asset = LoadSchemaAsset("anthropic_submit_improvement_tool.json");

            Assert.Equal(asset.Value<string>("name"), ToolSchemas.Improvement.Name);
            Assert.Equal(asset.Value<string>("description"), ToolSchemas.Improvement.Description);
            Assert.Equal(
                asset["input_schema"]!["properties"]!["improved"]!["description"]!.Value<string>(),
                ToolSchemas.Improvement.InputSchema["properties"]!["improved"]!["description"]!.Value<string>());
        }

        [Fact]
        public void ToolSchemasSource_DoesNotInlineModelFacingSchemaDescriptions()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Pinder.LlmAdapters", "Anthropic", "ToolSchemas.cs"));

            Assert.DoesNotContain("JObject.Parse(@", source, StringComparison.Ordinal);
            Assert.DoesNotContain("The dialogue text for this option.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Brief description of the tell behaviour.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("If no changes are needed, return the original content unchanged.", source, StringComparison.Ordinal);
        }

        private static JObject LoadSchemaAsset(string fileName)
        {
            string path = Path.Combine(FindRepoRoot(), "data", "schemas", fileName);
            return JObject.Parse(File.ReadAllText(path));
        }

        private static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "data")) &&
                    Directory.Exists(Path.Combine(dir, "src")))
                {
                    return dir;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate pinder-core repo root from test output directory.");
        }
    }
}
