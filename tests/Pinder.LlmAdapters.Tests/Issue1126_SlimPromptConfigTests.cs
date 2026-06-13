using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// #1126 — Slim prompt-fragment config to the minimal variable set.
    ///
    /// With one shared GM puppeteer prompt (#1124) and delivery collapsed into
    /// the deterministic, non-LLM <c>DeliveryOverlay</c> (#1125/#1138), the
    /// creative-delivery prompt fragments became dead config. This ticket
    /// removed the two provably-orphaned templates from
    /// <c>data/prompts/templates.yaml</c> and the matching loader surface:
    ///   - <c>engine-delivery-block</c>  (was <c>PromptTemplates.EngineDeliveryBlock</c>)
    ///   - <c>failure-delivery-instruction</c> (its C# property
    ///     <c>FailureDeliveryInstruction</c> was already deleted in #1138, leaving
    ///     the yaml entry orphaned).
    ///
    /// CONSERVATIVE-REMOVAL: every other prompt-fragment field was KEPT. In
    /// particular the still-live overlay machinery — <c>DeliveryRules</c>, the
    /// <c>default-*</c> tier strings, <c>max_delivery_words</c>, and the overlay
    /// length/tier/threshold instructions — remain, because the deterministic
    /// DeliveryOverlay (horniness/shadow/trap rewrites) still consumes them.
    ///
    /// These tests pin the slimmed surface:
    ///   (a) a session system prompt assembles with the slimmed field set and
    ///       contains NO unresolved <c>{placeholder}</c> tokens; and
    ///   (b) each removed field is genuinely unreferenced — the parser does not
    ///       throw a missing-required for it, the catalog no longer exposes it,
    ///       and the builder emits no empty/dead section for it.
    /// </summary>
    [Collection("PromptTraceSingleton")]
    public class Issue1126_SlimPromptConfigTests
    {
        public Issue1126_SlimPromptConfigTests()
        {
            // Wire PromptTemplates.Catalog / structural lookups from data/prompts
            // exactly as the production startup path does.
            PromptCatalogInitializer.Initialize();
        }

        private static string FindPromptsRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "data", "prompts");
                if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException(
                "Could not locate data/prompts in any ancestor of the test binary.");
        }

        // ── (a) slimmed prompt assembles with NO unresolved {placeholder} ──────

        [Theory]
        [InlineData("player")]
        [InlineData("datee")]
        public void SessionSystemPrompt_AssemblesWithSlimmedSet_NoUnresolvedPlaceholders(string session)
        {
            // Build with the real PinderDefaults game definition (the slimmed
            // field set) so any removed-field hole would surface as a dead token.
            var def = GameDefinition.PinderDefaults;
            string prompt = session == "player"
                ? SessionSystemPromptBuilder.BuildPlayerAvatar(
                    "You are Velvet. Lowercase-with-intent. Ironic.", def)
                : SessionSystemPromptBuilder.BuildDatee(
                    "You are Sable. Fast-talking. Uses omg and emoji.", def);

            Assert.False(string.IsNullOrWhiteSpace(prompt));

            // No unresolved {placeholder} token may survive in the assembled
            // system prompt. A {token} is a lowercase/underscore identifier in
            // braces (the substitution syntax used across templates.yaml). This
            // would only appear if a removed field left a dangling reference.
            var unresolved = Regex.Matches(prompt, @"\{[a-z][a-z0-9_]*\}")
                .Select(m => m.Value)
                .Distinct()
                .ToList();

            Assert.True(unresolved.Count == 0,
                $"Assembled {session} system prompt contains unresolved placeholder token(s): " +
                string.Join(", ", unresolved));
        }

        [Fact]
        public void TemplatesAndStructural_HaveNoDeadPlaceholderTokens_AcrossSuppliedVariables()
        {
            // AC grep-clean: every {...} token in templates.yaml / structural.yaml
            // must resolve to a variable some live builder supplies. The two
            // removed templates were the only fragments still carrying the dead
            // creative-delivery tokens ({chosen_option}, {roll_context},
            // {intended_message}, {miss_margin}, {tier_instruction}, ...). After
            // their removal those tokens must be gone from the retained catalog.
            string root = FindPromptsRoot();
            string templates = File.ReadAllText(Path.Combine(root, "templates.yaml"));
            string structural = File.ReadAllText(Path.Combine(root, "structural.yaml"));

            string[] deadTokens =
            {
                "{chosen_option}",
                "{roll_context}",
                "{intended_message}",
                "{miss_margin}",
                "{tier_instruction}",
                "{active_trap_llm_instructions}",
            };

            foreach (var tok in deadTokens)
            {
                Assert.DoesNotContain(tok, templates);
                Assert.DoesNotContain(tok, structural);
            }
        }

        // ── (b) each removed field is genuinely unreferenced ──────────────────

        [Fact]
        public void RemovedTemplateKeys_AreGoneFromCatalog()
        {
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            var names = catalog.Names.ToList();

            Assert.DoesNotContain("engine-delivery-block", names);
            Assert.DoesNotContain("failure-delivery-instruction", names);

            // TryGet returns null (not throw) for a genuinely absent key.
            Assert.Null(catalog.TryGet("engine-delivery-block"));
            Assert.Null(catalog.TryGet("failure-delivery-instruction"));
        }

        [Fact]
        public void Parser_DoesNotThrowMissingRequired_ForRemovedTemplates()
        {
            // The removed templates were NEVER game-definition.yaml required keys,
            // so loading the real game definition must still succeed end-to-end.
            // (A missing-required regression would throw here.)
            var def = GameDefinition.PinderDefaults;
            Assert.NotNull(def);

            // And the slimmed catalog loads cleanly with no exception.
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            Assert.NotNull(catalog);
        }

        [Fact]
        public void RemovedEngineDeliveryBlock_PropertyIsGone_FromPromptTemplates()
        {
            // The C# accessor must be removed together with the yaml key, so no
            // builder can append the dead block. Reflection guard pins removal.
            var prop = typeof(PromptTemplates).GetProperty(
                "EngineDeliveryBlock",
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);
            Assert.Null(prop);

            // FailureDeliveryInstruction was already removed in #1138; pin it too.
            var failureProp = typeof(PromptTemplates).GetProperty(
                "FailureDeliveryInstruction",
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);
            Assert.Null(failureProp);
        }

        [Fact]
        public void StillLiveOverlayFields_AreRetained()
        {
            // CONSERVATIVE-REMOVAL guard: the deterministic DeliveryOverlay still
            // consumes these, so they must NOT have been swept up with the dead
            // creative-delivery templates.
            var def = GameDefinition.PinderDefaults;
            Assert.NotNull(def.DeliveryRules);
            Assert.True(def.MaxDeliveryWords > 0);

            // The delivery-tier instruction strings (default-*) stay in the catalog.
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            var names = catalog.Names.ToList();
            Assert.Contains("default-clean", names);
            Assert.Contains("default-strong", names);
            Assert.Contains("default-critical", names);
            Assert.Contains("default-exceptional", names);
        }
    }
}
