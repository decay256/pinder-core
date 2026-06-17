using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    /// <summary>
    /// Issue #1173 — Displays with fixed denominator cannot exceed denominator.
    /// Added regression tests to satisfy the DoD and guard against regression.
    /// </summary>
    [Collection("PromptTraceSingleton")]
    public class Issue1173_DisplayDenominatorAuditTests
    {
        private static DialogueContext MakeDialogueContext(
            int currentTurn = 3,
            int currentInterest = 14,
            int horninessLevel = 0,
            string playerName = "Velvet",
            string dateeName = "Sable")
        {
            return new DialogueContext(
                playerAvatarPrompt: "velvet system prompt",
                dateePrompt: "sable system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                dateeLastMessage: "omg hi!!",
                activeTraps: Array.Empty<string>(),
                currentInterest: currentInterest,
                shadowThresholds: null,
                callbackOpportunities: null,
                horninessLevel: horninessLevel,
                requiresRizzOption: false,
                activeTrapInstructions: null,
                playerName: playerName,
                dateeName: dateeName,
                currentTurn: currentTurn,
                availableStats: new[] { StatType.Charm, StatType.Rizz, StatType.Honesty });
        }

        /// <summary>
        /// a. gameState render with a horniness value > 10 (e.g. HorninessLevel = 13)
        /// does NOT contain '13/10' nor ANY '/10' substring where N>10.
        /// </summary>
        [Fact]
        public void HorninessLevel_ExceedsTen_DoesNotRenderDenominatorOrExceededSlashFormat()
        {
            var context = MakeDialogueContext(horninessLevel: 13);
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            // Proves the corrected format does not contain "/10"
            Assert.DoesNotContain("/10", result);
            Assert.DoesNotContain("13/10", result);
            
            // Re-verify that Horniness value is outputted
            Assert.Contains("Horniness: 13", result);
        }

        /// <summary>
        /// b. Interest display STILL renders '/25' (guard the correct one isn't collateral-damaged) — 
        /// assert the built prompt contains 'Interest: {value}/25' for a normal interest value.
        /// </summary>
        [Fact]
        public void InterestDisplay_StillRendersSlashTwentyFive()
        {
            var context = MakeDialogueContext(currentInterest: 18);
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            // Guard the correct interest denominator display wasn't damaged
            Assert.Contains("Interest: 18/25", result);
        }

        /// <summary>
        /// c. AUDIT-CLASS GUARD: assert that the built gameState (across a horniness value at its max 13 and interest at its max 25) 
        /// contains NO substring matching a value/denominator where value > denominator.
        /// </summary>
        [Fact]
        public void AuditClassGuard_NoValueOverDenominatorDisplayWhereValueExceedsDenominator()
        {
            // Set Horniness Level to 13 (max possible, exceeds 10) and Current Interest to 25 (max possible, <= 25)
            var context = MakeDialogueContext(horninessLevel: 13, currentInterest: 25);
            var result = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            // Ensure no invalid denominators like '/10' or '/25' with exceeded value are present.
            // 11/10, 12/10, 13/10, 26/25 etc. must not exist.
            Assert.DoesNotContain("11/10", result);
            Assert.DoesNotContain("12/10", result);
            Assert.DoesNotContain("13/10", result);
            Assert.DoesNotContain("/10", result);

            // Interest at 25/25 is correct, so Interest: 25/25 should be fine
            Assert.Contains("Interest: 25/25", result);
            
            // Ensure no string contains N/25 where N > 25
            for (int i = 26; i <= 50; i++)
            {
                Assert.DoesNotContain($"{i}/25", result);
            }
        }
    }
}
