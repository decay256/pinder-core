using System;
using System.IO;
using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptCatalogStaticState")]
    public class PromptCatalogValidationTests
    {
        [Fact]
        public void ResolveCatalogOrThrow_ThrowsExistingWiringMessage()
        {
            var previous = PromptTemplates.Catalog;
            PromptTemplates.Catalog = null;
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(
                    () => PromptCatalog.ResolveCatalogOrThrow(null));

                Assert.Equal(
                    "PromptTemplates.Catalog is not wired. Call PromptWiring.Wire() at startup.",
                    ex.Message);
            }
            finally
            {
                PromptTemplates.Catalog = previous;
            }
        }

        [Fact]
        public void RequireCompleteEntry_PreservesMissingKeyMessage()
        {
            using var temp = new TempCatalogDirectory(
                "schema_version: 1\nprompts: {}\n");
            var catalog = PromptCatalog.LoadFromDirectory(temp.Path);

            var ex = Assert.Throws<InvalidOperationException>(
                () => catalog.RequireCompleteEntry(
                    "stake",
                    "prompt-catalog: missing required key 'stake'. The yaml file is incomplete or missing."));

            Assert.Equal(
                "prompt-catalog: missing required key 'stake'. The yaml file is incomplete or missing.",
                ex.Message);
        }

        [Fact]
        public void LoadFromDirectory_CanonicalizesPromptSourcePrefixAcrossFilesystemCase()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "prompt-catalog-source-case-" + Guid.NewGuid().ToString("N"));
            string prompts = System.IO.Path.Combine(root, "Data", "prompts");
            Directory.CreateDirectory(prompts);
            File.WriteAllText(
                System.IO.Path.Combine(prompts, "emotional-reactions.yaml"),
                "schema_version: 1\nprompts:\n  emotional-reaction-director:\n"
                + "    system_prompt: \"SYSTEM\"\n");
            try
            {
                var catalog = PromptCatalog.LoadFromDirectory(prompts);

                Assert.Equal(
                    "data/prompts/emotional-reactions.yaml",
                    catalog.TryGet("emotional-reaction-director")!.SourceFile);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void LoadFromDirectory_DoesNotTrustPromptSourceSubstringNearMatch()
        {
            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "prompt-catalog-source-near-match-" + Guid.NewGuid().ToString("N"));
            string prompts = System.IO.Path.Combine(root, "metaData", "prompts");
            string sourceFile = System.IO.Path.Combine(prompts, "emotional-reactions.yaml");
            Directory.CreateDirectory(prompts);
            File.WriteAllText(
                sourceFile,
                "schema_version: 1\nprompts:\n  emotional-reaction-director:\n"
                + "    system_prompt: \"SYSTEM\"\n");
            try
            {
                var catalog = PromptCatalog.LoadFromDirectory(prompts);

                Assert.Equal(
                    sourceFile.Replace('\\', '/'),
                    catalog.TryGet("emotional-reaction-director")!.SourceFile);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void SessionDocumentBuilder_UsesExplicitlyCapturedCatalog_AfterGlobalChanges()
        {
            var promptsRoot = FindPromptsRoot();
            var capturedRoot = CopyPromptsToTemp(promptsRoot);
            var globalRoot = CopyPromptsToTemp(promptsRoot);
            var previous = PromptTemplates.Catalog;
            try
            {
                ReplaceInTemplates(
                    capturedRoot,
                    "Generate exactly {options_count} dialogue options for {player_name}.",
                    "CAPTURED GENERATION for {player_name}: generate {options_count} options.");
                ReplaceInTemplates(
                    globalRoot,
                    "Generate exactly {options_count} dialogue options for {player_name}.",
                    "NEW GLOBAL GENERATION for {player_name}: generate {options_count} options.");
                var capturedCatalog = PromptCatalog.LoadFromDirectory(capturedRoot);
                PromptTemplates.Catalog = PromptCatalog.LoadFromDirectory(globalRoot);
                var context = new DialogueContext(
                    playerAvatarPrompt: "player",
                    dateePrompt: "datee",
                    conversationHistory: Array.Empty<(string, string)>(),
                    dateeLastMessage: "",
                    activeTraps: Array.Empty<string>(),
                    currentInterest: 10,
                    playerName: "Ari",
                    dateeName: "Sam",
                    availableStats: new[] { Pinder.Core.Stats.StatType.Charm });

                var prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                    context,
                    capturedCatalog);

                Assert.Contains("CAPTURED GENERATION for Ari", prompt);
                Assert.DoesNotContain("NEW GLOBAL GENERATION", prompt);
            }
            finally
            {
                PromptTemplates.Catalog = previous;
                Directory.Delete(capturedRoot, recursive: true);
                Directory.Delete(globalRoot, recursive: true);
            }
        }

        [Fact]
        public void ValidateRuntimeCatalog_RejectsMissingOperationalPlaceholder()
        {
            var root = CopyPromptsToTemp(FindPromptsRoot());
            try
            {
                ReplaceInTemplates(root, "{options_list}", "options_list");
                var catalog = PromptCatalog.LoadFromDirectory(root);

                var error = Assert.Throws<InvalidOperationException>(
                    () => catalog.ValidateRuntimeCatalog());

                Assert.Contains("dialogue-options-instruction", error.Message);
                Assert.Contains("{options_list}", error.Message);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ValidateRuntimeCatalog_RejectsMalformedCharacterDataFraming()
        {
            var root = CopyPromptsToTemp(FindPromptsRoot());
            try
            {
                var path = System.IO.Path.Combine(root, "structural.yaml");
                var contents = File.ReadAllText(path);
                Assert.Contains("{self_awareness}", contents);
                File.WriteAllText(
                    path,
                    contents.Replace("{self_awareness}", "self_awareness", StringComparison.Ordinal));
                var catalog = PromptCatalog.LoadFromDirectory(root);

                var error = Assert.Throws<InvalidOperationException>(
                    () => catalog.ValidateRuntimeCatalog());

                Assert.Contains("character_data_framing", error.Message);
                Assert.Contains("{self_awareness}", error.Message);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ValidateRuntimeCatalog_RejectsDiagnosisPromptMissingRequiredField()
        {
            var root = CopyPromptsToTemp(FindPromptsRoot());
            try
            {
                var path = System.IO.Path.Combine(root, "diagnosis.yaml");
                var contents = File.ReadAllText(path);
                Assert.Contains("\"self_awareness_reaction\"", contents);
                File.WriteAllText(
                    path,
                    contents.Replace(
                        "\"self_awareness_reaction\"",
                        "\"self_awareness_response\"",
                        StringComparison.Ordinal));
                var catalog = PromptCatalog.LoadFromDirectory(root);

                var error = Assert.Throws<InvalidOperationException>(
                    () => catalog.ValidateRuntimeCatalog());

                Assert.Contains("diagnosis", error.Message);
                Assert.Contains("self_awareness_reaction", error.Message);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Theory]
        [InlineData(
            "user_template: \"USER\"\n    temperature: 0.8\n",
            "prompt-catalog: key 'outfit' has no system_prompt. Check the yaml file.")]
        [InlineData(
            "system_prompt: \"SYSTEM\"\n    temperature: 0.8\n",
            "prompt-catalog: key 'outfit' has no user_template. Check the yaml file.")]
        [InlineData(
            "system_prompt: \"SYSTEM\"\n    user_template: \"USER\"\n",
            "prompt-catalog: key 'outfit' has no temperature. Check the yaml file.")]
        public void RequireCompleteEntry_PreservesIncompleteFieldMessages(
            string entryBody,
            string expectedMessage)
        {
            using var temp = new TempCatalogDirectory(
                "schema_version: 1\nprompts:\n  outfit:\n    " + entryBody);
            var catalog = PromptCatalog.LoadFromDirectory(temp.Path);

            var ex = Assert.Throws<InvalidOperationException>(
                () => catalog.RequireCompleteEntry(
                    "outfit",
                    "prompt-catalog: missing required key 'outfit'."));

            Assert.Equal(expectedMessage, ex.Message);
        }

        private sealed class TempCatalogDirectory : IDisposable
        {
            public TempCatalogDirectory(string yaml)
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "prompt-catalog-validation-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
                File.WriteAllText(System.IO.Path.Combine(Path, "test.yaml"), yaml);
            }

            public string Path { get; }

            public void Dispose()
            {
                Directory.Delete(Path, recursive: true);
            }
        }

        private static string FindPromptsRoot()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                var candidate = System.IO.Path.Combine(dir, "data", "prompts");
                if (Directory.Exists(candidate)) return candidate;
                dir = System.IO.Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }

        private static string CopyPromptsToTemp(string source)
        {
            var destination = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "prompt-generation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source, "*.yaml"))
            {
                File.Copy(file, System.IO.Path.Combine(destination, System.IO.Path.GetFileName(file)));
            }
            return destination;
        }

        private static void ReplaceInTemplates(string root, string oldValue, string newValue)
        {
            var path = System.IO.Path.Combine(root, "templates.yaml");
            var contents = File.ReadAllText(path);
            Assert.Contains(oldValue, contents);
            File.WriteAllText(path, contents.Replace(oldValue, newValue, StringComparison.Ordinal));
        }
    }
}
