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
    /// Tests for Issue #493: failure degradation legibility in opponent response prompts.
    /// Verifies that BuildOpponentPrompt injects per-tier failure context when DeliveryTier != None,
    /// and omits it on success.
    /// </summary>
    public class Issue493_FailureDegradationTests
    {
        private static OpponentContext MakeContext(FailureTier tier)
        {
            return new OpponentContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: new List<(string, string)> { ("Player", "hey"), ("Opponent", "hi") },
                opponentLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "so uh yeah you seem cool",
                interestBefore: 12,
                interestAfter: 11,
                responseDelayMinutes: 2.0,
                playerName: "Player",
                opponentName: "Opponent",
                currentTurn: 3,
                deliveryTier: tier);
        }

        [Fact]
        public void Success_NoFailureContext()
        {
            var context = MakeContext(FailureTier.None);
            var prompt = SessionDocumentBuilder.BuildOpponentPrompt(context);

            Assert.Contains("PLAYER'S LAST MESSAGE", prompt);
            Assert.DoesNotContain("FAILURE CONTEXT", prompt);
            Assert.DoesNotContain("delivered after a", prompt);
        }

        [Fact]
        public void Fumble_InjectsFailureContextWithSlightCoolness()
        {
            var context = MakeContext(FailureTier.Fumble);
            var prompt = SessionDocumentBuilder.BuildOpponentPrompt(context);

            Assert.Contains("delivered after a FUMBLE", prompt);
            Assert.Contains("FAILURE CONTEXT", prompt);
            Assert.Contains("slight coolness", prompt);
            Assert.Contains("so uh yeah you seem cool", prompt);
        }

        [Fact]
        public void Misfire_InjectsGuardedReaction()
        {
            var context = MakeContext(FailureTier.Misfire);
            var prompt = SessionDocumentBuilder.BuildOpponentPrompt(context);

            Assert.Contains("delivered after a MISFIRE", prompt);
            Assert.Contains("FAILURE CONTEXT", prompt);
            Assert.Contains("guarded", prompt);
        }

        [Fact]
        public void TropeTrap_InjectsVisibleDiscomfort()
        {
            var context = MakeContext(FailureTier.TropeTrap);
            var prompt = SessionDocumentBuilder.BuildOpponentPrompt(context);

            Assert.Contains("delivered after a TROPE_TRAP", prompt);
            Assert.Contains("FAILURE CONTEXT", prompt);
            Assert.Contains("warmth drops noticeably", prompt);
        }

        [Fact]
        public void Catastrophe_InjectsConfusion()
        {
            var context = MakeContext(FailureTier.Catastrophe);
            var prompt = SessionDocumentBuilder.BuildOpponentPrompt(context);

            Assert.Contains("delivered after a CATASTROPHE", prompt);
            Assert.Contains("FAILURE CONTEXT", prompt);
            Assert.Contains("discomfort", prompt);
        }

        [Fact]
        public void Legendary_InjectsMaximumReaction()
        {
            var context = MakeContext(FailureTier.Legendary);
            var prompt = SessionDocumentBuilder.BuildOpponentPrompt(context);

            Assert.Contains("delivered after a LEGENDARY", prompt);
            Assert.Contains("FAILURE CONTEXT", prompt);
            Assert.Contains("secondhand embarrassment", prompt);
        }

        [Fact]
        public void DefaultDeliveryTier_IsNone()
        {
            // Backward compatibility: OpponentContext without deliveryTier defaults to None
            var context = new OpponentContext(
                playerPrompt: "p",
                opponentPrompt: "o",
                conversationHistory: new List<(string, string)> { ("P", "hey") },
                opponentLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "hello",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 1.0);

            Assert.Equal(FailureTier.None, context.DeliveryTier);
        }

        [Fact]
        public void GetOpponentReactionGuidance_ReturnsEmptyForNone()
        {
            var result = SessionDocumentBuilder.GetOpponentReactionGuidance(FailureTier.None);
            Assert.Equal(string.Empty, result);
        }

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        [InlineData(FailureTier.Legendary)]
        public void GetOpponentReactionGuidance_ReturnsNonEmptyForAllTiers(FailureTier tier)
        {
            var result = SessionDocumentBuilder.GetOpponentReactionGuidance(tier);
            Assert.False(string.IsNullOrEmpty(result));
        }
    }
}
