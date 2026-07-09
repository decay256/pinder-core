using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;
using Pinder.Core.Tests.SessionSetup;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Issue #843 Phase 1: <see cref="PromptCatalog"/> loader + first
    /// call-site migration (<see cref="LlmStakeGenerator"/>).
    ///
    /// What this file pins:
    /// - The loader parses <c>data/prompts/stake.yaml</c> into a
    ///   <see cref="PromptCatalog"/> with the <c>"stake"</c> entry
    ///   present and carrying both system_prompt + user_template.
    /// - <c>{token}</c> substitution renders the user template byte-
    ///   identically to the legacy const-fallback path when
    ///   <c>{character_profile}</c> is the only token.
    /// - The catalog version of the system prompt matches the
    ///   <c>LlmStakeGenerator.DefaultSystemPrompt</c> const byte-for-byte
    ///   (Phase 1 contract: pure relocation, no content tuning; Phase 5
    ///   deletes the const).
    /// - The loader rejects schema_version != 1 and duplicate prompt
    ///   keys across files.
    /// </summary>
    [Trait("Category", "PromptCatalog")]
    public class Issue843_PromptCatalogPhase1Tests
    {
        // ----- repo helpers ---------------------------------------------------

        private static string FindRepoSubdir(string subdir)
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = Path.Combine(dir, subdir);
                if (Directory.Exists(candidate)) return candidate;
                var parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            throw new DirectoryNotFoundException(
                $"Could not locate {subdir} in any ancestor of the test binary.");
        }

        private static string PromptsRoot
            => FindRepoSubdir(Path.Combine("data", "prompts"));

        // ----- loader -------------------------------------------------------

        [Fact]
        public void Loader_LoadsStakePrompt_FromYamlFile()
        {
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);

            var stake = catalog.TryGet("stake");
            Assert.NotNull(stake);
            Assert.False(string.IsNullOrWhiteSpace(stake!.SystemPrompt));
            Assert.False(string.IsNullOrWhiteSpace(stake.UserTemplate));
            Assert.Contains("{character_profile}", stake.UserTemplate);
        }

        [Fact]
        public void Loader_LoadsOverlayModelComparisonPrompt_FromYamlFile()
        {
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);

            var system = catalog.TryGet("overlay-model-comparison-delivery-system");
            var user = catalog.TryGet("overlay-model-comparison-overlay-user");

            Assert.NotNull(system);
            Assert.NotNull(user);
            Assert.Contains("{brick_personality}", system!.UserTemplate);
            Assert.Contains("{base_message}", user!.UserTemplate);

            string rendered = PromptCatalog.Substitute(
                user.UserTemplate!,
                new Dictionary<string, string>
                {
                    { "catastrophe_overlay", "OVERLAY" },
                    { "base_message", "hello" },
                });

            Assert.Contains("OVERLAY INSTRUCTION:", rendered);
            Assert.Contains("MESSAGE TO TRANSFORM:\nhello", rendered);
        }

        [Fact]
        public void Loader_RejectsSchemaVersionOtherThanOne()
        {
            var dir = Directory.CreateTempSubdirectory("prompt-catalog-test-").FullName;
            try
            {
                File.WriteAllText(Path.Combine(dir, "bad.yaml"),
                    "schema_version: 999\nprompts: {}\n");
                Assert.Throws<InvalidDataException>(
                    () => PromptCatalog.LoadFromDirectory(dir));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Loader_RejectsDuplicatePromptKeysAcrossFiles()
        {
            var dir = Directory.CreateTempSubdirectory("prompt-catalog-test-").FullName;
            try
            {
                File.WriteAllText(Path.Combine(dir, "a.yaml"),
                    "schema_version: 1\nprompts:\n  shared:\n    system_prompt: from a\n");
                File.WriteAllText(Path.Combine(dir, "b.yaml"),
                    "schema_version: 1\nprompts:\n  shared:\n    system_prompt: from b\n");
                var ex = Assert.Throws<InvalidDataException>(
                    () => PromptCatalog.LoadFromDirectory(dir));
                Assert.Contains("duplicate prompt key", ex.Message, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Loader_TolerateMissingPromptsBlock()
        {
            // A yaml file without a `prompts:` block reserves a surface
            // for a later phase \u2014 must not crash the loader.
            var dir = Directory.CreateTempSubdirectory("prompt-catalog-test-").FullName;
            try
            {
                File.WriteAllText(Path.Combine(dir, "empty.yaml"),
                    "schema_version: 1\n# reserved for phase N\n");
                var catalog = PromptCatalog.LoadFromDirectory(dir);
                Assert.Empty(catalog.Names);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // ----- substitution -------------------------------------------------

        [Fact]
        public void Substitute_ReplacesNamedToken()
        {
            string out_ = PromptCatalog.Substitute(
                "Hello, {name}!",
                new Dictionary<string, string> { { "name", "Daniel" } });
            Assert.Equal("Hello, Daniel!", out_);
        }

        [Fact]
        public void Substitute_ThrowsOnMissingToken()
        {
            Assert.Throws<KeyNotFoundException>(() =>
                PromptCatalog.Substitute(
                    "Hello, {missing}!",
                    new Dictionary<string, string>()));
        }

        [Fact]
        public void Substitute_PassesThroughUnrecognisedBraces()
        {
            // Stray braces in prose / JSON blob in the template body
            // must not trip the substituter \u2014 only well-formed
            // {alphanumeric_token} sequences are recognised.
            string template = "use { for an opening brace and {123} is not a token";
            string out_ = PromptCatalog.Substitute(
                template,
                new Dictionary<string, string>());
            Assert.Equal(template, out_);
        }

        // ----- Phase 5 contract: missing catalog throws descriptive initialization error -----

        [Fact]
        public void Generators_WithoutCatalog_ThrowsInvalidOperationException()
        {
            // Ensure any null/unwired PromptTemplates.Catalog throws
            var previous = PromptTemplates.Catalog;
            PromptTemplates.Catalog = null;
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new LlmStakeGenerator(new StubLlmTransport()));

                Assert.Throws<InvalidOperationException>(() =>
                    new LlmBackgroundGenerator(new StubLlmTransport()));

                Assert.Throws<InvalidOperationException>(() =>
                    new LlmBackstoryGenerator(new StubLlmTransport()));
            }
            finally
            {
                PromptTemplates.Catalog = previous;
            }
        }

        [Fact]
        public void Generators_WithIncompleteCatalog_ThrowsInvalidOperationException()
        {
            // Ensure if catalog is present but missing stake/background entries, it throws
            var dir = Directory.CreateTempSubdirectory("incomplete-catalog-test-").FullName;
            try
            {
                File.WriteAllText(Path.Combine(dir, "empty.yaml"),
                    "schema_version: 1\nprompts: {}\n");
                var emptyCatalog = PromptCatalog.LoadFromDirectory(dir);

                Assert.Throws<InvalidOperationException>(() =>
                    new LlmStakeGenerator(new StubLlmTransport(), null, null, emptyCatalog));

                Assert.Throws<InvalidOperationException>(() =>
                    new LlmBackgroundGenerator(new StubLlmTransport(), null, emptyCatalog));

                Assert.Throws<InvalidOperationException>(() =>
                    new LlmBackstoryGenerator(new StubLlmTransport(), null, emptyCatalog));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // ----- helper -------------------------------------------------------

        /// <summary>
        /// Whitespace-normalise: collapse runs of whitespace to a single
        /// space, trim ends. Preserves semantically meaningful content
        /// while tolerating yaml block-scalar trim differences.
        /// </summary>
        private static string NormalizeWhitespace(string s)
        {
            return string.Join(" ",
                s.Split(new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
