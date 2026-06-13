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
            string dateeLastMessage = "",
            string[] activeTraps = null,
            int currentInterest = 10,
            int currentTurn = 1,
            string playerName = "P",
            string dateeName = "O",
            Dictionary<ShadowStatType, int> shadowThresholds = null,
            List<CallbackOpportunity> callbackOpportunities = null,
            string[] activeTrapInstructions = null,
            int horninessLevel = 0,
            bool requiresRizzOption = false)
        {
            return new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>(),
                dateeLastMessage: dateeLastMessage,
                activeTraps: activeTraps ?? Array.Empty<string>(),
                currentInterest: currentInterest,
                shadowThresholds: shadowThresholds,
                callbackOpportunities: callbackOpportunities,
                horninessLevel: horninessLevel,
                requiresRizzOption: requiresRizzOption,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                dateeName: dateeName,
                currentTurn: currentTurn);
        }

        private static DeliveryContext MakeDeliveryContext(
            IReadOnlyList<(string Sender, string Text)> conversationHistory = null,
            DialogueOption chosenOption = null,
            FailureTier outcome = FailureTier.None,
            int beatDcBy = 0,
            string[] activeTrapInstructions = null,
            string playerName = "P",
            string dateeName = "O",
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new DeliveryContext(
                playerAvatarPrompt: "player prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>(),
                dateeLastMessage: "",
                chosenOption: chosenOption ?? new DialogueOption(StatType.Charm, "default"),
                outcome: outcome,
                beatDcBy: beatDcBy,
                activeTraps: Array.Empty<string>(),
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                dateeName: dateeName);
        }

        private static DateeContext MakeDateeContext(
            IReadOnlyList<(string Sender, string Text)> conversationHistory = null,
            string playerDeliveredMessage = "Hey",
            int interestBefore = 10,
            int interestAfter = 10,
            double responseDelayMinutes = 1.0,
            string[] activeTrapInstructions = null,
            string playerName = "P",
            string dateeName = "O",
            Dictionary<ShadowStatType, int> shadowThresholds = null)
        {
            return new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: playerDeliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: responseDelayMinutes,
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                dateeName: dateeName);
        }
    }
}
