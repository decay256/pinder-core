using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class SessionDocumentBuilderPromptTests
    {
        private static DialogueContext MakeDialogueContext(
            List<CallbackOpportunity> callbackOpportunities = null,
            string[] activeTrapInstructions = null,
            int horninessLevel = 0,
            bool requiresRizzOption = false,
            int currentInterest = 10,
            int currentTurn = 3,
            IReadOnlyList<(string Sender, string Text)> conversationHistory = null,
            string opponentLastMessage = "hey",
            string[] activeTraps = null,
            string playerName = "Player",
            string opponentName = "Opponent",
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new DialogueContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: conversationHistory ?? Array.Empty<(string, string)>(),
                opponentLastMessage: opponentLastMessage,
                activeTraps: activeTraps ?? Array.Empty<string>(),
                currentInterest: currentInterest,
                shadowThresholds: shadowThresholds,
                callbackOpportunities: callbackOpportunities,
                horninessLevel: horninessLevel,
                requiresRizzOption: requiresRizzOption,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                opponentName: opponentName,
                currentTurn: currentTurn);
        }

        private static string BuildMinimal(
            List<CallbackOpportunity> callbackOpportunities = null,
            string[] activeTrapInstructions = null,
            int horninessLevel = 0,
            bool requiresRizzOption = false,
            int currentInterest = 10,
            int currentTurn = 3)
        {
            return SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(
                    callbackOpportunities: callbackOpportunities,
                    activeTrapInstructions: activeTrapInstructions,
                    horninessLevel: horninessLevel,
                    requiresRizzOption: requiresRizzOption,
                    currentInterest: currentInterest,
                    currentTurn: currentTurn));
        }

        // ── Callback Opportunities ──

        [Fact]
        public void CallbackOpportunities_Appear_In_Prompt()
        {
            var cbs = new List<CallbackOpportunity>
            {
                new CallbackOpportunity("cats", 1),
                new CallbackOpportunity("hiking", 2)
            };

            var result = BuildMinimal(callbackOpportunities: cbs, currentTurn: 5);

            Assert.Contains("Callback opportunities:", result);
            Assert.Contains("\"cats\"", result);
            Assert.Contains("\"hiking\"", result);
            Assert.Contains("+2 hidden", result); // cats: 5-1=4 turns ago
            Assert.Contains("+1 hidden", result); // hiking: 5-2=3 turns ago
        }

        [Fact]
        public void CallbackOpportunities_Opener_Bonus()
        {
            var cbs = new List<CallbackOpportunity>
            {
                new CallbackOpportunity("opener-topic", 3)
            };

            var result = BuildMinimal(callbackOpportunities: cbs, currentTurn: 3);

            Assert.Contains("+3 hidden (opener)", result);
        }

        [Fact]
        public void CallbackOpportunities_Null_No_Section()
        {
            var result = BuildMinimal(callbackOpportunities: null);
            Assert.DoesNotContain("CALLBACK OPPORTUNITIES", result);
        }

        // ── Active Trap Instructions ──

        [Fact]
        public void ActiveTrapInstructions_Appear_In_DialogueOptions_Prompt()
        {
            var instructions = new[] { "All options must include a pun about cats." };

            var result = BuildMinimal(activeTrapInstructions: instructions);

            Assert.Contains("ACTIVE TRAP INSTRUCTIONS", result);
            Assert.Contains("All options must include a pun about cats.", result);
        }

        [Fact]
        public void ActiveTrapInstructions_Null_No_Section()
        {
            var result = BuildMinimal(activeTrapInstructions: null);
            Assert.DoesNotContain("ACTIVE TRAP INSTRUCTIONS", result);
        }

        // ── Horniness & Rizz ──

        [Fact]
        public void Horniness_GE6_Injects_Note()
        {
            var result = BuildMinimal(horninessLevel: 7);

            Assert.Contains("Horniness: 7/10", result);
        }

        [Fact]
        public void Horniness_Below6_No_Note()
        {
            var result = BuildMinimal(horninessLevel: 5);

            Assert.DoesNotContain("Horniness:", result);
        }

        [Fact]
        public void RequiresRizzOption_True_Injects_Fire_Line()
        {
            var result = BuildMinimal(requiresRizzOption: true);

            Assert.Contains("REQUIRED: Include at least one Rizz option", result);
        }

        [Fact]
        public void RequiresRizzOption_False_No_Fire_Line()
        {
            var result = BuildMinimal(requiresRizzOption: false);

            Assert.DoesNotContain("REQUIRED: Include at least one Rizz option", result);
        }

        // ── Turn Number & Interest State ──

        [Fact]
        public void Turn_Number_Appears_In_GameState()
        {
            var result = BuildMinimal(currentTurn: 7);

            Assert.Contains("[ENGINE — Turn 7]", result);
        }

        [Fact]
        public void Interest_High_Shows_AlmostThere()
        {
            var result = BuildMinimal(currentInterest: 22);

            Assert.Contains("Interest: 22/25", result);
            Assert.Contains("Almost There", result);
        }

        [Fact]
        public void Interest_Mid_Shows_Interested()
        {
            var result = BuildMinimal(currentInterest: 12);

            Assert.Contains("Interest: 12/25", result);
            Assert.Contains("Interested", result);
        }

        [Fact]
        public void Interest_Low_Shows_Bored()
        {
            var result = BuildMinimal(currentInterest: 2);

            Assert.Contains("Interest: 2/25", result);
            Assert.Contains("Bored", result);
            Assert.Contains("disadvantage", result);
        }

        [Fact]
        public void Interest_Zero_Shows_Unmatched()
        {
            var result = BuildMinimal(currentInterest: 0);

            Assert.Contains("Interest: 0/25", result);
            Assert.Contains("Unmatched", result);
        }

        [Fact]
        public void Interest_VeryIntoIt_Shows_PlayerAdvantage()
        {
            var result = BuildMinimal(currentInterest: 18);

            Assert.Contains("Very Into It", result);
            Assert.Contains("advantage", result);
        }

        [Fact]
        public void Interest_Lukewarm_Range()
        {
            var result = BuildMinimal(currentInterest: 6);

            Assert.Contains("Lukewarm", result);
        }
    }
}
