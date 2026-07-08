using System;
using System.IO;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
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

        [Theory]
        [InlineData(
            "user_template: \"USER\"\n    temperature: 0.8\n    max_tokens: 250\n",
            "prompt-catalog: key 'outfit' has no system_prompt. Check the yaml file.")]
        [InlineData(
            "system_prompt: \"SYSTEM\"\n    temperature: 0.8\n    max_tokens: 250\n",
            "prompt-catalog: key 'outfit' has no user_template. Check the yaml file.")]
        [InlineData(
            "system_prompt: \"SYSTEM\"\n    user_template: \"USER\"\n    max_tokens: 250\n",
            "prompt-catalog: key 'outfit' has no temperature. Check the yaml file.")]
        [InlineData(
            "system_prompt: \"SYSTEM\"\n    user_template: \"USER\"\n    temperature: 0.8\n",
            "prompt-catalog: key 'outfit' has no max_tokens. Check the yaml file.")]
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
    }
}
