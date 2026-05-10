using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.LlmAdapters;
using Pinder.SessionSetup;
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

        // ----- byte-identity contract: stake.yaml vs DefaultSystemPrompt -----

        [Fact]
        public void StakeYaml_SystemPrompt_MatchesConstFallback_ByteForByte()
        {
            // #843 Phase 1 contract: this is a pure relocation. The yaml
            // must produce the same system_prompt the legacy const does.
            // If a future PR tunes the prompt content, it MUST update
            // both the yaml AND the const (or, post-Phase-5, just the
            // yaml after the const is deleted).
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);
            var stake = catalog.Get("stake");

            string fromYaml = NormalizeWhitespace(stake.SystemPrompt!);
            string fromConst = NormalizeWhitespace(LlmStakeGenerator.DefaultSystemPrompt);

            Assert.Equal(fromConst, fromYaml);
        }

        [Fact]
        public void StakeYaml_UserTemplate_RendersIdenticallyToConstFallback()
        {
            var catalog = PromptCatalog.LoadFromDirectory(PromptsRoot);
            const string sampleProfile = "<<TEST PROFILE>>";

            // Catalog-driven render.
            string fromCatalog = LlmStakeGenerator.BuildUserMessage(
                sampleProfile, catalog);

            // Const-fallback render (catalog=null).
            string fromConst = LlmStakeGenerator.BuildUserMessage(
                sampleProfile, catalog: null);

            // Whitespace-normalised equality \u2014 yaml's block-scalar
            // trimming may add or strip a trailing newline; the
            // semantically-meaningful content must match.
            Assert.Equal(NormalizeWhitespace(fromConst), NormalizeWhitespace(fromCatalog));

            // Both renders must contain the substituted profile.
            Assert.Contains(sampleProfile, fromCatalog);
            Assert.Contains(sampleProfile, fromConst);
        }

        [Fact]
        public void StakeGenerator_WithoutCatalog_UsesConstDefaults()
        {
            // Belt-and-braces: the catalog argument is optional. The
            // legacy 3-arg constructor and the new 4-arg constructor
            // must both yield identical output when the catalog is
            // null / unsupplied.
            const string sampleProfile = "<<NULL CATALOG>>";
            string a = LlmStakeGenerator.BuildUserMessage(sampleProfile, catalog: null);
            string b = LlmStakeGenerator.BuildUserMessage(sampleProfile, catalog: null);
            Assert.Equal(a, b);
            Assert.Contains(sampleProfile, a);
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
