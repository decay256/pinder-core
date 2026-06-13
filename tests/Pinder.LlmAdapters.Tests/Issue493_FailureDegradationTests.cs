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
    /// Tests for Issue #493: failure degradation legibility in datee response prompts.
    /// Verifies that BuildDateePrompt injects per-tier failure context when DeliveryTier != None,
    /// and omits it on success.
    /// </summary>
    public class Issue493_FailureDegradationTests
    {
        private static DateeContext MakeContext(FailureTier tier)
        {
            return new DateeContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)> { ("Player", "hey"), ("Datee", "hi") },
                dateeLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "so uh yeah you seem cool",
                interestBefore: 12,
                interestAfter: 11,
                responseDelayMinutes: 2.0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 3,
                deliveryTier: tier);
        }

        [Fact]
        public void Success_NoFailureContext()
        {
            var context = MakeContext(FailureTier.None);
            var prompt = SessionDocumentBuilder.BuildDateePrompt(context);

            Assert.Contains("PLAYER'S LAST MESSAGE", prompt);
            Assert.DoesNotContain("FAILURE CONTEXT", prompt);
            Assert.DoesNotContain("delivered after a", prompt);
        }

        [Fact]
        public void Fumble_InjectsFailureContextWithSlightCoolness()
        {
            var context = MakeContext(FailureTier.Fumble);
            var prompt = SessionDocumentBuilder.BuildDateePrompt(context);

            Assert.Contains("delivered after a FUMBLE", prompt);
            Assert.Contains("FAILURE CONTEXT", prompt);
            Assert.Contains("slight coolness", prompt);
            Assert.Contains("so uh yeah you seem cool", prompt);
        }

        [Fact]
        public void Misfire_InjectsGuardedReaction()
        {
            var context = MakeContext(FailureTier.Misfire);
            var prompt = SessionDocumentBuilder.BuildDateePrompt(context);

            Assert.Contains("delivered after a MISFIRE", prompt);
            Assert.Contains("FAILURE CONTEXT", prompt);
            Assert.Contains("guarded", prompt);
        }

        [Fact]
        public void TropeTrap_InjectsVisibleDiscomfort()
        {
            var context = MakeContext(FailureTier.TropeTrap);
            var prompt = SessionDocumentBuilder.BuildDateePrompt(context);

            Assert.Contains("delivered after a TROPE_TRAP", prompt);
            Assert.Contains("FAILURE CONTEXT", prompt);
            Assert.Contains("warmth drops noticeably", prompt);
        }

        [Fact]
        public void Catastrophe_InjectsConfusion()
        {
            var context = MakeContext(FailureTier.Catastrophe);
            var prompt = SessionDocumentBuilder.BuildDateePrompt(context);

            Assert.Contains("delivered after a CATASTROPHE", prompt);
            Assert.Contains("FAILURE CONTEXT", prompt);
            Assert.Contains("discomfort", prompt);
        }

        [Fact]
        public void Legendary_InjectsMaximumReaction()
        {
            var context = MakeContext(FailureTier.Legendary);
            var prompt = SessionDocumentBuilder.BuildDateePrompt(context);

            Assert.Contains("delivered after a LEGENDARY", prompt);
            Assert.Contains("FAILURE CONTEXT", prompt);
            Assert.Contains("secondhand embarrassment", prompt);
        }

        [Fact]
        public void DefaultDeliveryTier_IsNone()
        {
            // Backward compatibility: DateeContext without deliveryTier defaults to None
            var context = new DateeContext(
                playerAvatarPrompt: "p",
                dateePrompt: "o",
                conversationHistory: new List<(string, string)> { ("P", "hey") },
                dateeLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "hello",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 1.0);

            Assert.Equal(FailureTier.None, context.DeliveryTier);
        }

        [Fact]
        public void GetDateeReactionGuidance_ReturnsEmptyForNone()
        {
            var result = SessionDocumentBuilder.GetDateeReactionGuidance(FailureTier.None);
            Assert.Equal(string.Empty, result);
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        [InlineData(FailureTier.Legendary)]
        public void GetDateeReactionGuidance_ReturnsNonEmptyForAllTiers(FailureTier tier)
        {
            var result = SessionDocumentBuilder.GetDateeReactionGuidance(tier);
            Assert.False(string.IsNullOrEmpty(result));
        }
    }
}
