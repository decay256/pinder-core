using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;

namespace Pinder.LlmAdapters.Tests
{
    public partial class EngineInjectionBlockTests
    {
        // Wire prompt catalog once for all tests (Phase 5 of #871: no const fallbacks).
        static EngineInjectionBlockTests()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                var candidate = System.IO.Path.Combine(dir, "data", "prompts");
                if (System.IO.Directory.Exists(candidate))
                {
                    var catalog = Pinder.LlmAdapters.PromptCatalog.LoadFromDirectory(candidate);
                    Pinder.LlmAdapters.PromptTemplates.Catalog = catalog;
                    Pinder.Core.Prompts.PromptBuilder.StructuralFragmentLookup =
                        key => catalog.TryGet(key)?.SystemPrompt;
                    Pinder.LlmAdapters.ArchetypeYamlLoader.LoadFromPromptCatalog(catalog);
                    return;
                }
                var parent = System.IO.Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
        }

        // ── Helpers ──

        private static DialogueContext MakeDialogueContext(
            int currentTurn = 3,
            string playerName = "Velvet",
            string dateeName = "Sable",
            int currentInterest = 14,
            IReadOnlyList<(string Sender, string Text)>? conversationHistory = null,
            string dateeLastMessage = "hey",
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            List<CallbackOpportunity>? callbackOpportunities = null,
            int horninessLevel = 0,
            bool requiresRizzOption = false,
            string[]? activeTrapInstructions = null,
            string[]? activeTraps = null,
            string playerTextingStyle = "")
        {
            return new DialogueContext(
                playerPrompt: "velvet system prompt",
                dateePrompt: "sable system prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!"),
                    ("Velvet", "how's your night going"),
                    ("Sable", "so good lol")
                },
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
                currentTurn: currentTurn,
                playerTextingStyle: playerTextingStyle);
        }

        private static DeliveryContext MakeDeliveryContext(
            DialogueOption? chosenOption = null,
            FailureTier outcome = FailureTier.None,
            int beatDcBy = 5,
            string playerName = "Velvet",
            string dateeName = "Sable",
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            string[]? activeTrapInstructions = null)
        {
            return new DeliveryContext(
                playerPrompt: "velvet system prompt",
                dateePrompt: "sable system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                dateeLastMessage: "omg hi!!",
                chosenOption: chosenOption ?? new DialogueOption(StatType.Wit, "you remind me of a song I can't quite place"),
                outcome: outcome,
                beatDcBy: beatDcBy,
                activeTraps: Array.Empty<string>(),
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                dateeName: dateeName);
        }

        private static DateeContext MakeDateeContext(
            int interestBefore = 12,
            int interestAfter = 14,
            string playerDeliveredMessage = "you remind me of a song I can't quite place",
            double responseDelayMinutes = 2.5,
            string playerName = "Velvet",
            string dateeName = "Sable",
            Dictionary<ShadowStatType, int>? shadowThresholds = null,
            string[]? activeTrapInstructions = null,
            FailureTier deliveryTier = FailureTier.None)
        {
            return new DateeContext(
                playerPrompt: "velvet system prompt",
                dateePrompt: "sable system prompt",
                conversationHistory: new List<(string, string)>
                {
                    ("Velvet", "hey there"),
                    ("Sable", "omg hi!!")
                },
                dateeLastMessage: "omg hi!!",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: playerDeliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: responseDelayMinutes,
                shadowThresholds: shadowThresholds,
                activeTrapInstructions: activeTrapInstructions,
                playerName: playerName,
                dateeName: dateeName,
                deliveryTier: deliveryTier);
        }

        private static string? FindYamlPath(string relativePath)
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir, relativePath);
                if (System.IO.File.Exists(candidate)) return candidate;
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
