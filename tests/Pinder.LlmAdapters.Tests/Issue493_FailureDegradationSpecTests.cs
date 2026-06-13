using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #493: failure degradation legibility.
    /// Supplements the existing Issue493_FailureDegradationTests with edge cases
    /// and additional acceptance criteria coverage.
    /// </summary>
    public class Issue493_FailureDegradationSpecTests
    {
        private static DateeContext MakeContext(FailureTier tier, int interestBefore = 12, int interestAfter = 11)
        {
            return new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)> { ("Player", "hey"), ("Datee", "hi") },
                dateeLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: "so uh yeah you seem cool",
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: 2.0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 3,
                deliveryTier: tier);
        }

        #region AC5: TropeTrap and Catastrophe produce distinct guidance

        // Mutation: would catch if TropeTrap and Catastrophe return identical guidance text
        [Fact]
        public void AC5_TropeTrap_And_Catastrophe_ProduceDistinctGuidance()
        {
            var tropeTrapPrompt = SessionDocumentBuilder.BuildDateePrompt(MakeContext(FailureTier.TropeTrap));
            var catastrophePrompt = SessionDocumentBuilder.BuildDateePrompt(MakeContext(FailureTier.Catastrophe));

            // Both should contain FAILURE CONTEXT but with different content
            Assert.Contains("FAILURE CONTEXT", tropeTrapPrompt);
            Assert.Contains("FAILURE CONTEXT", catastrophePrompt);
            Assert.NotEqual(tropeTrapPrompt, catastrophePrompt);
        }

        #endregion

        #region AC6: Success produces no failure context

        // Mutation: would catch if FAILURE CONTEXT is always injected regardless of tier
        [Fact]
        public void AC6_Success_NoFailureContext_InPrompt()
        {
            var context = MakeContext(FailureTier.None, interestBefore: 12, interestAfter: 13);
            var prompt = SessionDocumentBuilder.BuildDateePrompt(context);

            Assert.DoesNotContain("FAILURE CONTEXT", prompt);
        }

        #endregion

        #region AC7: All five failure tiers have distinct non-empty guidance

        // Mutation: would catch if any two tiers share the same guidance text
        [Fact]
        public void AC7_AllFailureTiers_ProduceDistinctGuidance()
        {
            var tiers = new[]
            {
                FailureTier.Fumble,
                FailureTier.Misfire,
                FailureTier.TropeTrap,
                FailureTier.Catastrophe,
                FailureTier.Legendary
            };

            var guidanceTexts = new HashSet<string>();

            foreach (var tier in tiers)
            {
                var guidance = SessionDocumentBuilder.GetDateeReactionGuidance(tier);
                Assert.False(string.IsNullOrEmpty(guidance), $"Guidance for {tier} should not be empty");
                Assert.True(guidanceTexts.Add(guidance), $"Guidance for {tier} should be distinct from others");
            }
        }

        #endregion

        #region AC4: Fumble guidance — subtle, no fourth-wall breaking

        // Mutation: would catch if Fumble guidance uses explicit failure language
        [Fact]
        public void AC4_Fumble_Guidance_DoesNotBreakFourthWall()
        {
            var guidance = SessionDocumentBuilder.GetDateeReactionGuidance(FailureTier.Fumble);

            // Per spec: "the datee shouldn't break the fourth wall"
            // Guidance should NOT contain meta-game language like "failed" or "messed up"
            Assert.DoesNotContain("failed", guidance.ToLowerInvariant());
            Assert.DoesNotContain("messed up", guidance.ToLowerInvariant());
            Assert.DoesNotContain("rolled", guidance.ToLowerInvariant());
        }

        #endregion

        #region Edge Cases

        // Mutation: would catch if unrecognized FailureTier enum value causes exception instead of empty string
        [Fact]
        public void EdgeCase_InvalidTierValue_ReturnsEmptyString()
        {
            var invalidTier = (FailureTier)999;
            var guidance = SessionDocumentBuilder.GetDateeReactionGuidance(invalidTier);

            Assert.Equal(string.Empty, guidance);
        }

        // Mutation: would catch if FAILURE CONTEXT section is placed incorrectly (e.g., before player message)
        [Fact]
        public void EdgeCase_FailureContext_AppearsAfterPlayerMessage()
        {
            var context = MakeContext(FailureTier.Catastrophe);
            var prompt = SessionDocumentBuilder.BuildDateePrompt(context);

            int playerMessageIndex = prompt.IndexOf("PLAYER'S LAST MESSAGE", StringComparison.Ordinal);
            int failureContextIndex = prompt.IndexOf("FAILURE CONTEXT", StringComparison.Ordinal);

            // FAILURE CONTEXT should appear after PLAYER'S LAST MESSAGE
            Assert.True(playerMessageIndex >= 0, "PLAYER'S LAST MESSAGE section should exist");
            Assert.True(failureContextIndex >= 0, "FAILURE CONTEXT section should exist for failure");
            Assert.True(failureContextIndex > playerMessageIndex,
                "FAILURE CONTEXT should appear after PLAYER'S LAST MESSAGE");
        }

        // Mutation: would catch if Legendary and Catastrophe produce the same guidance
        [Fact]
        public void EdgeCase_Legendary_DistinctFromCatastrophe()
        {
            var legendary = SessionDocumentBuilder.GetDateeReactionGuidance(FailureTier.Legendary);
            var catastrophe = SessionDocumentBuilder.GetDateeReactionGuidance(FailureTier.Catastrophe);

            Assert.NotEqual(legendary, catastrophe);
        }

        // Mutation: would catch if guidance escalation order is wrong (e.g., Fumble more severe than Catastrophe)
        [Fact]
        public void EdgeCase_GuidanceSeverity_Escalates()
        {
            // Legendary should be the most extreme — check it references embarrassment/shock
            var legendary = SessionDocumentBuilder.GetDateeReactionGuidance(FailureTier.Legendary);
            Assert.Contains("embarrassment", legendary.ToLowerInvariant());

            // Fumble should be the mildest — check it references subtle/slight
            var fumble = SessionDocumentBuilder.GetDateeReactionGuidance(FailureTier.Fumble);
            Assert.Contains("slight", fumble.ToLowerInvariant());
        }

        #endregion
    }
}
