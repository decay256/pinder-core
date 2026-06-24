using System;
using Pinder.Core.Rolls;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue1249_DateeHorninessTierReactionTests
    {
        // Candidate words required by the test contract
        // Implementer is required to make catastrophe carry an escalated-reaction directive
        // such as "severe", "strong", "major", "really", "visibly", "badly", "seriously", "disturb", "alarm", "recoil"
        private static readonly string[] StrongReactionWords = { "severe", "strong", "major", "really", "visibly", "badly", "seriously", "disturb", "alarm", "recoil" };
        
        // Fumble carries mild ones
        private static readonly string[] MildReactionWords = { "slight", "small", "mild", "barely", "minor", "a little" };

        [Theory]
        [InlineData(FailureTier.Fumble)]
        [InlineData(FailureTier.Misfire)]
        [InlineData(FailureTier.TropeTrap)]
        [InlineData(FailureTier.Catastrophe)]
        public void OverlayAppliedFalse_ReturnsEmptyRegardlessOfTier(FailureTier tier)
        {
            string guidance = SessionDocumentBuilder.GetHorninessReactionGuidance(10, overlayApplied: false, tier);
            Assert.Equal(string.Empty, guidance);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(5)]
        [InlineData(17)]
        public void CatastropheGuidance_IsMateriallyStronger_ThanFumbleAndMisfire(int interest)
        {
            string catastrophe = SessionDocumentBuilder.GetHorninessReactionGuidance(interest, overlayApplied: true, FailureTier.Catastrophe);
            string fumble = SessionDocumentBuilder.GetHorninessReactionGuidance(interest, overlayApplied: true, FailureTier.Fumble);
            string misfire = SessionDocumentBuilder.GetHorninessReactionGuidance(interest, overlayApplied: true, FailureTier.Misfire);

            // Tier ordering sanity: Catastrophe must differ from BOTH
            Assert.NotEqual(fumble, catastrophe);
            Assert.NotEqual(misfire, catastrophe);

            // Check that catastrophe is longer or stronger than fumble
            // Given the requirements: "catastrophe-block is longer-or-stronger; plus a tolerant ContainsAny strong-cue check on catastrophe and its ABSENCE on fumble."
            bool isStrongerOrLonger = catastrophe.Length > fumble.Length || ContainsAny(catastrophe, StrongReactionWords);
            Assert.True(isStrongerOrLonger, "Catastrophe guidance must be materially stronger or longer than Fumble guidance.");

            bool catastropheHasStrongCue = ContainsAny(catastrophe, StrongReactionWords);
            Assert.True(catastropheHasStrongCue, "Catastrophe must contain a strong reaction cue.");

            bool fumbleHasStrongCue = ContainsAny(fumble, StrongReactionWords);
            Assert.False(fumbleHasStrongCue, "Fumble must NOT contain a strong reaction cue.");
        }

        [Fact]
        public void Reaffirmation25Gate_IsPreservedInCatastrophe()
        {
            // The 25-gate reaffirmation ("25") must remain present in the catastrophe guidance too (don't lose the #1248 invariant).
            string catastrophe = SessionDocumentBuilder.GetHorninessReactionGuidance(10, overlayApplied: true, FailureTier.Catastrophe);
            Assert.Contains("25", catastrophe);
        }

        private bool ContainsAny(string source, string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (source.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}