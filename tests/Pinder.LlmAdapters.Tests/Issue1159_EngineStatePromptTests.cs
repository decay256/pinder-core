using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public partial class EngineInjectionBlockTests
    {
        [Fact]
        public void OptionsPrompt_EngineStateIncludesHfiTorCognitiveAndTransitionLines()
        {
            var context = new DialogueContext(
                playerAvatarPrompt: "velvet system prompt",
                dateePrompt: "sable system prompt",
                conversationHistory: new List<(string, string)> { ("Velvet", "hey"), ("Sable", "hi") },
                dateeLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 14,
                playerName: "Velvet",
                dateeName: "Sable",
                currentTurn: 2,
                availableStats: new[] { StatType.Charm, StatType.Honesty },
                resolvedTarget: new ResolvedRevelationTarget
                {
                    Registry = "BACKSTORY",
                    Index = 3,
                    Field = "BIO_LIE",
                    StemText = "pretending the move was easy",
                    TransitionStyle = "buffered disclosure"
                },
                cognitiveSubtext: "ABANDONMENT + DEFLECTION",
                playerHungerForIntimacy: 7,
                playerTerrorOfRejection: 12,
                dateeHungerForIntimacy: 9,
                dateeTerrorOfRejection: 14);

            string prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);
            string engine = prompt.Substring(
                prompt.IndexOf("<ENGINE_STATE>", StringComparison.Ordinal),
                prompt.IndexOf("</ENGINE_STATE>", StringComparison.Ordinal) - prompt.IndexOf("<ENGINE_STATE>", StringComparison.Ordinal));

            Assert.Contains("HFI (Hunger for Intimacy): player 7, datee 9", engine);
            Assert.Contains("TOR (Terror of Rejection): player 12, datee 14", engine);
            Assert.Contains("Cognitive subtext: ABANDONMENT + DEFLECTION", engine);
            Assert.Contains("Transition target: pretending the move was easy", engine);
            Assert.Contains("Apply this specifically to the final option", engine);
            Assert.Contains("Transition style for the final option: buffered disclosure", engine);
            Assert.DoesNotContain("BACKSTORY", engine);
            Assert.DoesNotContain("BIO_LIE", engine);
            Assert.DoesNotContain(":3", engine);

            var trace = SessionDocumentBuilder.BuildDialogueOptionsPromptEx(context);
            AssertCatalogSpan(trace, "engine-state-hfi-line");
            AssertCatalogSpan(trace, "engine-state-tor-line");
            AssertCatalogSpan(trace, "engine-state-cognitive-subtext-line");
            AssertCatalogSpan(trace, "engine-state-transition-target-line");
            AssertCatalogSpan(trace, "engine-state-transition-style-line");
        }

        [Fact]
        public void OptionsPrompt_FirstTurnDoesNotRenderTransitionTarget()
        {
            string prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(
                MakeDialogueContext(currentTurn: 1, conversationHistory: Array.Empty<(string Sender, string Text)>()));

            Assert.DoesNotContain("transition target:", prompt);
            Assert.DoesNotContain("transition style:", prompt);
        }

        private static void AssertCatalogSpan(PromptTraceResult trace, string key)
        {
            Assert.Contains(
                trace.Spans,
                span => span.Key == key && span.SourceFile == "data/prompts/templates.yaml");
        }
    }
}
