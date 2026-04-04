using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Tests for Issue #490: opponent resistance descriptors based on interest level.
    /// Verifies that BuildOpponentPrompt includes the fundamental resistance rule
    /// and interest-appropriate resistance descriptors.
    /// </summary>
    public class Issue490_ResistanceDescriptorTests
    {
        private static OpponentContext MakeOpponentContext(int interestAfter, int interestBefore = -1)
        {
            if (interestBefore < 0) interestBefore = interestAfter;
            return new OpponentContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: new List<(string, string)> { ("P", "hey"), ("O", "hi") },
                opponentLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: "hey there",
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: 2.0,
                playerName: "P",
                opponentName: "O");
        }

        // ── Fundamental resistance rule present ──

        [Fact]
        public void BuildOpponentPrompt_ContainsFundamentalResistanceRule()
        {
            var ctx = MakeOpponentContext(12);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("FUNDAMENTAL RULE: Below Interest 25, you are not won over", result);
        }

        [Fact]
        public void BuildOpponentPrompt_ContainsArchetypeResistanceGuidance()
        {
            var ctx = MakeOpponentContext(12);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Your archetype determines HOW you resist, not WHETHER", result);
        }

        // ── Interest 1-4: Active disengagement ──

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void BuildOpponentPrompt_Interest1To4_ActiveDisengagement(int interest)
        {
            var ctx = MakeOpponentContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Active disengagement", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ── Interest 5-9: Skeptical interest ──

        [Theory]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(9)]
        public void BuildOpponentPrompt_Interest5To9_SkepticalInterest(int interest)
        {
            var ctx = MakeOpponentContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Skeptical interest", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ── Interest 10-14: Unstable agreement ──

        [Theory]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(14)]
        public void BuildOpponentPrompt_Interest10To14_UnstableAgreement(int interest)
        {
            var ctx = MakeOpponentContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Unstable agreement", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ── Interest 15-20: Deliberate approach ──

        [Theory]
        [InlineData(15)]
        [InlineData(17)]
        [InlineData(20)]
        public void BuildOpponentPrompt_Interest15To20_DeliberateApproach(int interest)
        {
            var ctx = MakeOpponentContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Deliberate approach", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ── Interest 21-24: Almost convinced ──

        [Theory]
        [InlineData(21)]
        [InlineData(23)]
        [InlineData(24)]
        public void BuildOpponentPrompt_Interest21To24_AlmostConvinced(int interest)
        {
            var ctx = MakeOpponentContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Almost convinced", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ── Interest 25: Resistance dissolved ──

        [Fact]
        public void BuildOpponentPrompt_Interest25_ResistanceDissolved()
        {
            var ctx = MakeOpponentContext(25);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Resistance dissolved", result);
            Assert.Contains("Current interest: 25/25", result);
        }

        // ── Interest 0: Active disengagement (edge case) ──

        [Fact]
        public void BuildOpponentPrompt_Interest0_ActiveDisengagement()
        {
            var ctx = MakeOpponentContext(0);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Active disengagement", result);
        }

        // ── Boundary tests ──

        [Fact]
        public void BuildOpponentPrompt_BoundaryAt5_SkepticalNotActive()
        {
            var ctx = MakeOpponentContext(5);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Skeptical interest", result);
            Assert.DoesNotContain("Active disengagement", result);
        }

        [Fact]
        public void BuildOpponentPrompt_BoundaryAt10_UnstableNotSkeptical()
        {
            var ctx = MakeOpponentContext(10);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Unstable agreement", result);
            Assert.DoesNotContain("Skeptical interest", result);
        }

        [Fact]
        public void BuildOpponentPrompt_BoundaryAt15_DeliberateNotUnstable()
        {
            var ctx = MakeOpponentContext(15);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Deliberate approach", result);
            Assert.DoesNotContain("Unstable agreement", result);
        }

        [Fact]
        public void BuildOpponentPrompt_BoundaryAt21_AlmostNotDeliberate()
        {
            var ctx = MakeOpponentContext(21);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Almost convinced", result);
            Assert.DoesNotContain("Deliberate approach", result);
        }

        [Fact]
        public void BuildOpponentPrompt_BoundaryAt25_DissolvedNotAlmost()
        {
            var ctx = MakeOpponentContext(25);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Resistance dissolved", result);
            Assert.DoesNotContain("Almost convinced", result);
        }

        // ── GetResistanceBlock unit tests ──

        [Theory]
        [InlineData(0, "Active disengagement")]
        [InlineData(1, "Active disengagement")]
        [InlineData(4, "Active disengagement")]
        [InlineData(5, "Skeptical interest")]
        [InlineData(9, "Skeptical interest")]
        [InlineData(10, "Unstable agreement")]
        [InlineData(14, "Unstable agreement")]
        [InlineData(15, "Deliberate approach")]
        [InlineData(20, "Deliberate approach")]
        [InlineData(21, "Almost convinced")]
        [InlineData(24, "Almost convinced")]
        [InlineData(25, "Resistance dissolved")]
        public void GetResistanceBlock_ReturnsCorrectDescriptor(int interest, string expectedPhrase)
        {
            var block = SessionDocumentBuilder.GetResistanceBlock(interest);

            Assert.Contains(expectedPhrase, block);
            Assert.Contains($"Current interest: {interest}/25", block);
        }
    }
}
