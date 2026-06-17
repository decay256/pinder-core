using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// #950: psychological stake must surface in option-generator prompt.
    /// Tests guard the PROMPT PATH — not live LLM output (stochastic).
    /// </summary>
    [Collection("PromptTraceSingleton")]
    public class Issue950_StakeSurfacingTests
    {
        // ── helpers ──────────────────────────────────────────────────────

        private static string[] MakeStakeLines() => new[]
        {
            "13. My last named ex was Margot and the specific reason it ended was she found my laminated Camino map humiliating",
            "7. If you opened my browser history at 3am last Tuesday you'd find 17 tabs about the same drummer from 2019",
            "1. The most humiliating thing that happened to me this week was when I deleted my thesis on purpose",
        };

        private static DialogueContext MakeContextWithStake(
            string[]? stakeLines = null,
            IReadOnlyCollection<int>? stakeLinesReferenced = null,
            int turn = 2)
        {
            return new DialogueContext(
                playerAvatarPrompt: "PSYCHOLOGICAL STAKE:\n" + string.Join("\n", stakeLines ?? MakeStakeLines()),
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("O", "Hi"),
                    ("P", "Hey there"),
                },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerName: "P",
                dateeName: "O",
                currentTurn: turn,
                stakeLines: stakeLines ?? MakeStakeLines(),
                stakeLinesReferenced: stakeLinesReferenced, availableStats: new[] { Pinder.Core.Stats.StatType.Charm, Pinder.Core.Stats.StatType.Rizz, Pinder.Core.Stats.StatType.Honesty,  });
        }

        // ── test 1: OPTION_C mandate appears in prompt (from templates.yaml) ──

        [Fact]
        public void Prompt_ContainsOptionC_Mandate_WhenStakeIsPresent()
        {
            // Catalog wired by LlmAdaptersTestWiring.ModuleInitializer.
            var context = MakeContextWithStake();
            var prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            // The templates.yaml dialogue-options-instruction now mandates OPTION_3.
            Assert.Contains("the final OPTION (OPTION_3) MUST", prompt);
        }

        // ── test 2: stake-coverage block injected when StakeLines set ────

        [Fact]
        public void Prompt_ContainsStakeCoverageBlock_WhenStakeLinesSet()
        {
            var context = MakeContextWithStake();
            var prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            Assert.Contains("STAKE COVERAGE", prompt);
            Assert.Contains("Untouched stake lines", prompt);
            // At least one of the concrete names/years should appear in the previews.
            Assert.Contains("Margot", prompt);
        }

        // ── test 3: referenced lines are excluded from the untouched list ─

        [Fact]
        public void Prompt_ExcludesReferencedLines_FromUntouchedList()
        {
            // Mark line 0 (Margot) as already referenced.
            var referenced = new HashSet<int> { 0 };
            var context = MakeContextWithStake(stakeLinesReferenced: referenced);
            var prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            // "1 line referenced" should appear
            Assert.Contains("1 line(s) referenced this session", prompt);
            // Margot's line is no longer in the untouched preview
            Assert.DoesNotContain("Line 1:", prompt); // 0-based idx 0 → display "Line 1"
            // The drummer line and thesis line are still untouched
            Assert.Contains("Line 2:", prompt);
        }

        // ── test 4: warning fires when options skip stake content ─────────

        [Fact]
        public async Task StakeSkipWarning_Fires_WhenOptionsOmitAllStakeFragments()
        {
            // Transport returns four options with no stake content whatsoever.
            const string fakeResponse =
                "OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n" +
                "\"So what are you up to this weekend?\"\n\n" +
                "OPTION_2\n[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n" +
                "\"You seem like someone who enjoys long walks.\"\n\n" +
                "OPTION_3\n[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n" +
                "\"Honestly I have no idea what I'm doing here.\"\n\n" +
                "OPTION_4\n[STAT: CHAOS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n" +
                "\"Bold of you to assume I have plans.\"";

            string? capturedWarning = null;
            var transport = new FixedResponseTransport(fakeResponse);
            var options = new PinderLlmAdapterOptions
            {
                OnStakeSkipWarning = w => capturedWarning = w,
            };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = MakeContextWithStake();
            await adapter.GetDialogueOptionsAsync(context);

            Assert.NotNull(capturedWarning);
            Assert.Contains("option_generator_skipped_stake", capturedWarning);
            Assert.Contains("stake_hits=0", capturedWarning);
        }

        // ── test 5: no warning when at least one option references stake ──

        [Fact]
        public async Task StakeSkipWarning_DoesNotFire_WhenOptionContainsStakeFragment()
        {
            // OPTION_3 mentions "Margot" — a fragment from the stake lines.
            const string fakeResponse =
                "OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n" +
                "\"So what are you up to this weekend?\"\n\n" +
                "OPTION_2\n[STAT: WIT] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n" +
                "\"You seem like someone who enjoys long walks.\"\n\n" +
                "OPTION_3\n[STAT: HONESTY] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n" +
                "\"I deleted my thesis on purpose, actually. Watched it go.\"\n\n" +
                "OPTION_4\n[STAT: CHAOS] [CALLBACK: none] [COMBO: none] [TELL_BONUS: no]\n" +
                "\"Bold of you to assume I have plans.\"";

            string? capturedWarning = null;
            var transport = new FixedResponseTransport(fakeResponse);
            var options = new PinderLlmAdapterOptions
            {
                OnStakeSkipWarning = w => capturedWarning = w,
            };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = MakeContextWithStake();
            await adapter.GetDialogueOptionsAsync(context);

            // "deleted my thesis on purpose" is a fragment from stake line[2]
            Assert.Null(capturedWarning);
        }

        // ── fake transport ───────────────────────────────────────────────

        private sealed class FixedResponseTransport : ILlmTransport
        {
            private readonly string _response;
            public FixedResponseTransport(string response) => _response = response;

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
                => Task.FromResult(_response);
        }
    }
}
