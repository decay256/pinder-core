using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;

namespace Pinder.LlmAdapters.Tests
{
    public partial class SessionDocumentBuilderTests
    {
        private static DialogueContext MakeDialogueContext(
            IReadOnlyList<(string Sender, string Text)> conversationHistory = null,
            string opponentLastMessage = "",
            string[] activeTraps = null,
            int currentInterest = 10,
            int currentTurn = 1,
            string playerName = "P",
            string opponentName = "O",
            Dictionary<ShadowStatType, int> shadowThresholds = null,
            List<CallbackOpportunity> callbackOpportunities = null,
            string[] activeTrapInstructions = null,
            int horninessLevel = 0,
            bool requiresRizzOption = false)
        {
            return new DialogueContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>(),
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

        private static DeliveryContext MakeDeliveryContext(
            IReadOnlyList<(string Sender, string Text)> conversationHistory = null,
            DialogueOption chosenOption = null,
            FailureTier outcome = FailureTier.None,
            int beatDcBy = 0,
            string[] activeTrapInstructions = null,
            string playerName = "P",
            string opponentName = "O",
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new DeliveryContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>(),
                opponentLastMessage: "",
                chosenOption: chosenOption ?? new DialogueOption(StatType.Charm, "default"),
                outcome: outcome,
                beatDcBy: beatDcBy,
                activeTraps: Array.Empty<string>(),
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                opponentName: opponentName);
        }

        private static OpponentContext MakeOpponentContext(
            IReadOnlyList<(string Sender, string Text)> conversationHistory = null,
            string playerDeliveredMessage = "Hey",
            int interestBefore = 10,
            int interestAfter = 10,
            double responseDelayMinutes = 1.0,
            string[] activeTrapInstructions = null,
            string playerName = "P",
            string opponentName = "O",
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new OpponentContext(
                playerPrompt: "player prompt",
                opponentPrompt: "opponent prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>(),
                opponentLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: playerDeliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: responseDelayMinutes,
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                opponentName: opponentName);
        }
    }
}
