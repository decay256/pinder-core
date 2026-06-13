using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Text;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #1129 (data reset + persistence schema rename to new terminology).
    ///
    /// Two regression guards locking the engine-owned persisted/trace surface to
    /// the post-#1121/#1122 terminology (OPPONENT→DATEE, PLAYER→PLAYER AVATAR)
    /// and the post-#1125 slimmed phase set:
    ///
    ///   (a) ROUND-TRIP: driving every live prompt-compilation emitter records
    ///       its trace under the NEW <c>prompt_type</c> key names
    ///       (<c>datee</c>, <c>datee-system</c>, <c>dialogue-options</c>,
    ///       <c>dialogue-options-system</c>) and the recorded text round-trips
    ///       back out of the trace registry unchanged.
    ///
    ///   (b) GREP-GUARD: the live trace key set contains NONE of the retired
    ///       old-terminology keys (<c>opponent</c>, <c>opponent-system</c>) and
    ///       no <c>delivery</c> creative-phase trace (collapsed by #1125). This
    ///       is the in-test equivalent of the PR-body grep-clean assertion.
    ///
    /// Engine-side only. The live Postgres column/jsonb rename + data wipe is the
    /// cross-repo pinder-web follow-up filed against #1129; this repo owns the
    /// serialization/trace-key/fixture surface and asserts it here.
    /// </summary>
    public sealed class Issue1129_SchemaRenameGuardTests
    {
        // The complete set of live prompt_type trace keys the engine compiles
        // per turn after #1121 (OPPONENT→DATEE) and #1125 (delivery creative
        // call removed). Kept here as the single canonical expectation so the
        // guard fails loudly if a future change reintroduces old terminology.
        private static readonly string[] ExpectedLivePromptTypes =
        {
            "dialogue-options",
            "dialogue-options-system",
            "datee",
            "datee-system",
        };

        // Old-terminology keys that MUST NOT be compiled by any live emitter.
        private static readonly string[] RetiredPromptTypes =
        {
            "opponent",
            "opponent-system",
            // #1125: the creative "delivery" LLM call (and its trace) is gone.
            "delivery",
        };

        private static DialogueContext MakeDialogueContext() => new DialogueContext(
            playerAvatarPrompt: "You are reuben.",
            dateePrompt: "You are talking to velvet.",
            conversationHistory: new List<(string Sender, string Text)> { ("Velvet", "Hello") },
            dateeLastMessage: "Hello",
            activeTraps: new List<string>(),
            currentInterest: 10,
            currentTurn: 3);

        private static DateeContext MakeDateeContext() => new DateeContext(
            dateePrompt: "You are velvet.",
            conversationHistory: new List<(string Sender, string Text)> { ("Reuben", "Hi") },
            dateeLastMessage: "Hi",
            activeTraps: new List<string>(),
            currentInterest: 10,
            playerDeliveredMessage: "Hey there",
            interestBefore: 10,
            interestAfter: 12,
            responseDelayMinutes: 0.0,
            playerName: "Reuben",
            dateeName: "Velvet",
            currentTurn: 3);

        /// <summary>
        /// (a) Round-trip: every live emitter records under a NEW-terminology
        /// prompt_type key, and the recorded trace text equals what the builder
        /// returned (registry round-trips it unchanged).
        /// </summary>
        [Fact]
        public void LiveEmitters_RecordTracesUnderNewKeyNames_AndRoundTrip()
        {
            PromptCatalogInitializer.Initialize();
            InMemoryPromptTraceService.Instance.Clear();

            // Drive all four live prompt compilations a fresh session fires.
            var dialogue = SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            var datee = SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext());
            var dialogueSystem = SessionSystemPromptBuilder.BuildPlayerAvatar("You are reuben.");
            var dateeSystem = SessionSystemPromptBuilder.BuildDatee("You are velvet.");

            // Each new-terminology key is present and round-trips the exact text.
            void AssertRoundTrip(string promptType, string expectedText)
            {
                var trace = InMemoryPromptTraceService.Instance.GetLastTrace(promptType);
                Assert.True(trace != null, $"Expected a recorded trace for new prompt_type '{promptType}'.");
                Assert.Equal(expectedText, trace!.Text);
            }

            AssertRoundTrip("dialogue-options", dialogue);
            AssertRoundTrip("datee", datee);
            AssertRoundTrip("dialogue-options-system", dialogueSystem);
            AssertRoundTrip("datee-system", dateeSystem);

            // The recorded key set is exactly the expected new-terminology set.
            var recordedKeys = InMemoryPromptTraceService.Instance.GetAllTraces().Keys
                .Select(k => k.ToLowerInvariant())
                .ToHashSet();
            foreach (var key in ExpectedLivePromptTypes)
            {
                Assert.Contains(key, recordedKeys);
            }
        }

        /// <summary>
        /// (b) Grep-guard: after driving every live emitter, NONE of the retired
        /// old-terminology prompt_type keys (opponent*, delivery) were recorded.
        /// In-test equivalent of the PR-body "no stale OPPONENT prompt_type"
        /// grep-clean assertion.
        /// </summary>
        [Fact]
        public void LiveEmitters_RecordNoRetiredOldTerminologyKeys()
        {
            PromptCatalogInitializer.Initialize();
            InMemoryPromptTraceService.Instance.Clear();

            SessionDocumentBuilder.BuildDialogueOptionsPrompt(MakeDialogueContext());
            SessionDocumentBuilder.BuildDateePrompt(MakeDateeContext());
            SessionSystemPromptBuilder.BuildPlayerAvatar("You are reuben.");
            SessionSystemPromptBuilder.BuildDatee("You are velvet.");

            foreach (var retired in RetiredPromptTypes)
            {
                Assert.True(
                    InMemoryPromptTraceService.Instance.GetLastTrace(retired) == null,
                    $"Retired old-terminology prompt_type '{retired}' must NOT be compiled by any live emitter.");
            }

            // And no recorded key contains the old "opponent" substring at all.
            var recordedKeys = InMemoryPromptTraceService.Instance.GetAllTraces().Keys.ToList();
            Assert.DoesNotContain(recordedKeys, k => k.ToLowerInvariant().Contains("opponent"));
        }
    }
}
