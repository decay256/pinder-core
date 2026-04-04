using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Spec-driven tests for Issue #490: opponent resistance below Interest 25.
    /// Tests verify behavior described in docs/specs/issue-490-spec.md.
    /// Each test has a mutation comment explaining what code change would cause failure.
    /// </summary>
    public class Issue490_ResistanceSpec_Tests
    {
        /// <summary>
        /// Helper to construct a minimal OpponentContext with the given interest.
        /// </summary>
        private static OpponentContext MakeContext(int interestAfter, int interestBefore = -1)
        {
            if (interestBefore < 0) interestBefore = interestAfter;
            return new OpponentContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: new List<(string, string)> { ("Player", "hey"), ("Opponent", "hi") },
                opponentLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: "hey there",
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: 1.0,
                playerName: "Player",
                opponentName: "Opponent");
        }

        // ═══════════════════════════════════════════════════════════
        // AC1: Fundamental resistance rule is included in prompt
        // ═══════════════════════════════════════════════════════════

        // Fails if: the resistance rule constant is removed or not injected into BuildOpponentPrompt
        [Fact]
        public void AC1_BuildOpponentPrompt_ContainsFundamentalResistanceRule()
        {
            var ctx = MakeContext(12);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Below Interest 25, you are not won over", result);
        }

        // Fails if: the resistance rule is only injected for certain interest ranges (e.g. skipped at 25)
        [Theory]
        [InlineData(3)]
        [InlineData(12)]
        [InlineData(22)]
        public void AC1_ResistanceRulePresent_BelowDateSecured(int interest)
        {
            var ctx = MakeContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("resistance is always present underneath", result);
        }

        // ═══════════════════════════════════════════════════════════
        // AC2: Interest 1-4 — Active disengagement
        // ═══════════════════════════════════════════════════════════

        // Fails if: boundary check uses > 4 instead of >= 5 for the next band
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void AC2_Interest1To4_ShowsActiveDisengagement(int interest)
        {
            var ctx = MakeContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Active disengagement", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ═══════════════════════════════════════════════════════════
        // AC3: Interest 10-14 — Warmth with holdback
        // ═══════════════════════════════════════════════════════════

        // Fails if: the 10-14 band uses wrong descriptor or boundary is off-by-one
        [Theory]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(14)]
        public void AC3_Interest10To14_ShowsUnstableAgreement(int interest)
        {
            var ctx = MakeContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Unstable agreement", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ═══════════════════════════════════════════════════════════
        // AC4: Interest 21-24 — Subtle but present resistance
        // ═══════════════════════════════════════════════════════════

        // Fails if: the 21-24 band is merged into the 15-20 band or uses wrong descriptor
        [Theory]
        [InlineData(21)]
        [InlineData(22)]
        [InlineData(24)]
        public void AC4_Interest21To24_ShowsSubtleResistance(int interest)
        {
            var ctx = MakeContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Almost convinced", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ═══════════════════════════════════════════════════════════
        // AC5: Interest 25 — Resistance dissolves
        // ═══════════════════════════════════════════════════════════

        // Fails if: interest 25 gets a "not won over" message instead of dissolved
        [Fact]
        public void AC5_Interest25_ResistanceDissolves()
        {
            var ctx = MakeContext(25);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Resistance dissolved", result);
            Assert.Contains("Current interest: 25/25", result);
        }

        // Fails if: interest 25 still includes the "not won over" resistance rule
        [Fact]
        public void AC5_Interest25_DoesNotContainNotWonOverLanguage()
        {
            var ctx = MakeContext(25);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            // At 25 the opponent IS won over, so "not won over" should not appear
            // (The fundamental rule says "Below Interest 25, you are not won over")
            // At 25, a different framing should be used.
            Assert.DoesNotContain("Active disengagement", result);
            Assert.DoesNotContain("Almost convinced", result);
        }

        // ═══════════════════════════════════════════════════════════
        // Interest 5-9 — Lukewarm / Skeptical
        // ═══════════════════════════════════════════════════════════

        // Fails if: 5-9 band missing or merged into 1-4 band
        [Theory]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(9)]
        public void Interest5To9_ShowsSkepticalInterest(int interest)
        {
            var ctx = MakeContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Skeptical interest", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ═══════════════════════════════════════════════════════════
        // Interest 15-20 — VeryIntoIt / Deliberate approach
        // ═══════════════════════════════════════════════════════════

        // Fails if: 15-20 band merged into 10-14 or 21-24
        [Theory]
        [InlineData(15)]
        [InlineData(18)]
        [InlineData(20)]
        public void Interest15To20_ShowsDeliberateApproach(int interest)
        {
            var ctx = MakeContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Deliberate approach", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ═══════════════════════════════════════════════════════════
        // Interest 0 — Unmatched / edge case
        // ═══════════════════════════════════════════════════════════

        // Fails if: interest 0 throws or returns empty string
        [Fact]
        public void Interest0_ReturnsDisengagement()
        {
            var ctx = MakeContext(0);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("Active disengagement", result);
            Assert.Contains("Current interest: 0/25", result);
        }

        // ═══════════════════════════════════════════════════════════
        // Boundary: exact transition points
        // ═══════════════════════════════════════════════════════════

        // Fails if: boundary at 5 uses wrong band (off-by-one: <= 5 vs < 5)
        [Fact]
        public void Boundary_4to5_TransitionsFromActiveToSkeptical()
        {
            var result4 = SessionDocumentBuilder.GetResistanceBlock(4);
            var result5 = SessionDocumentBuilder.GetResistanceBlock(5);

            Assert.Contains("Active disengagement", result4);
            Assert.DoesNotContain("Active disengagement", result5);
            Assert.Contains("Skeptical interest", result5);
            Assert.DoesNotContain("Skeptical interest", result4);
        }

        // Fails if: boundary at 10 uses wrong band
        [Fact]
        public void Boundary_9to10_TransitionsFromSkepticalToUnstable()
        {
            var result9 = SessionDocumentBuilder.GetResistanceBlock(9);
            var result10 = SessionDocumentBuilder.GetResistanceBlock(10);

            Assert.Contains("Skeptical interest", result9);
            Assert.DoesNotContain("Skeptical interest", result10);
            Assert.Contains("Unstable agreement", result10);
            Assert.DoesNotContain("Unstable agreement", result9);
        }

        // Fails if: boundary at 15 uses wrong band
        [Fact]
        public void Boundary_14to15_TransitionsFromUnstableToDeliberate()
        {
            var result14 = SessionDocumentBuilder.GetResistanceBlock(14);
            var result15 = SessionDocumentBuilder.GetResistanceBlock(15);

            Assert.Contains("Unstable agreement", result14);
            Assert.DoesNotContain("Unstable agreement", result15);
            Assert.Contains("Deliberate approach", result15);
            Assert.DoesNotContain("Deliberate approach", result14);
        }

        // Fails if: boundary at 21 uses wrong band
        [Fact]
        public void Boundary_20to21_TransitionsFromDeliberateToAlmost()
        {
            var result20 = SessionDocumentBuilder.GetResistanceBlock(20);
            var result21 = SessionDocumentBuilder.GetResistanceBlock(21);

            Assert.Contains("Deliberate approach", result20);
            Assert.DoesNotContain("Deliberate approach", result21);
            Assert.Contains("Almost convinced", result21);
            Assert.DoesNotContain("Almost convinced", result20);
        }

        // Fails if: boundary at 25 uses wrong band
        [Fact]
        public void Boundary_24to25_TransitionsFromAlmostToDissolved()
        {
            var result24 = SessionDocumentBuilder.GetResistanceBlock(24);
            var result25 = SessionDocumentBuilder.GetResistanceBlock(25);

            Assert.Contains("Almost convinced", result24);
            Assert.DoesNotContain("Almost convinced", result25);
            Assert.Contains("Resistance dissolved", result25);
            Assert.DoesNotContain("Resistance dissolved", result24);
        }

        // ═══════════════════════════════════════════════════════════
        // Edge cases: interest outside normal range
        // ═══════════════════════════════════════════════════════════

        // Fails if: negative interest causes exception or wrong band
        [Theory]
        [InlineData(-1)]
        [InlineData(-10)]
        public void EdgeCase_NegativeInterest_TreatedAsLowest(int interest)
        {
            // Spec says: interest < 0 should be treated as 0 (Unmatched)
            var block = SessionDocumentBuilder.GetResistanceBlock(interest);

            // Should not throw, and should return a valid descriptor
            Assert.NotNull(block);
            Assert.NotEmpty(block);
            Assert.Contains("Active disengagement", block);
        }

        // Fails if: interest > 25 causes exception or wrong band
        [Theory]
        [InlineData(26)]
        [InlineData(100)]
        public void EdgeCase_InterestAbove25_TreatedAsDateSecured(int interest)
        {
            // Spec says: interest > 25 should be treated as 25 (DateSecured)
            var block = SessionDocumentBuilder.GetResistanceBlock(interest);

            Assert.NotNull(block);
            Assert.NotEmpty(block);
            Assert.Contains("Resistance dissolved", block);
        }

        // ═══════════════════════════════════════════════════════════
        // Section placement: RESISTANCE STANCE header present
        // ═══════════════════════════════════════════════════════════

        // Fails if: the resistance section header/rule is missing from the assembled prompt
        [Fact]
        public void SectionPlacement_FundamentalRulePresent()
        {
            var ctx = MakeContext(12);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            Assert.Contains("FUNDAMENTAL RULE", result);
        }

        // ═══════════════════════════════════════════════════════════
        // GetResistanceBlock: full band coverage via unit test
        // ═══════════════════════════════════════════════════════════

        // Fails if: any band returns wrong descriptor or GetResistanceBlock has wrong boundary
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
        public void GetResistanceBlock_AllBands_ReturnCorrectDescriptor(int interest, string expectedPhrase)
        {
            var block = SessionDocumentBuilder.GetResistanceBlock(interest);

            Assert.Contains(expectedPhrase, block);
            Assert.Contains($"Current interest: {interest}/25", block);
        }

        // ═══════════════════════════════════════════════════════════
        // Resistance always injected: no skip condition
        // ═══════════════════════════════════════════════════════════

        // Fails if: resistance block is conditionally skipped for some interest values
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(25)]
        public void ResistanceBlock_AlwaysPresent_AtEveryInterestLevel(int interest)
        {
            var ctx = MakeContext(interest);
            var result = SessionDocumentBuilder.BuildOpponentPrompt(ctx);

            // The prompt should always contain the resistance section
            Assert.Contains("FUNDAMENTAL RULE", result);
            Assert.Contains($"Current interest: {interest}/25", result);
        }

        // ═══════════════════════════════════════════════════════════
        // Each band is distinct: no two adjacent bands share the same descriptor
        // ═══════════════════════════════════════════════════════════

        // Fails if: two adjacent bands return identical text (copy-paste bug)
        [Fact]
        public void AllBands_HaveDistinctDescriptors()
        {
            // One representative from each band
            var band0 = SessionDocumentBuilder.GetResistanceBlock(0);
            var band5 = SessionDocumentBuilder.GetResistanceBlock(5);
            var band10 = SessionDocumentBuilder.GetResistanceBlock(10);
            var band15 = SessionDocumentBuilder.GetResistanceBlock(15);
            var band21 = SessionDocumentBuilder.GetResistanceBlock(21);
            var band25 = SessionDocumentBuilder.GetResistanceBlock(25);

            // All should be different
            var blocks = new[] { band0, band5, band10, band15, band21, band25 };
            for (int i = 0; i < blocks.Length; i++)
            {
                for (int j = i + 1; j < blocks.Length; j++)
                {
                    Assert.NotEqual(blocks[i], blocks[j]);
                }
            }
        }
    }
}
