using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public class Issue1261_DialogueContextInjectionTests
    {
        private static DialogueContext MakeDialogueContextWithTarget()
        {
            var target = new ResolvedRevelationTarget 
            { 
                Registry = "STAKE", 
                Index = 13, 
                Field = "STAKE_LINE", 
                Manner = "ACCIDENTAL_SLIP", 
                StemText = "laminated Camino map", 
                TransitionStyle = "ACCIDENTAL_SLIP" 
            };
            
            return new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)>(),
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "GERALD",
                dateeName: "VELVET",
                currentTurn: 3,
                availableStats: new[] { StatType.Charm, StatType.Rizz, StatType.Honesty },
                resolvedTarget: target,
                cognitiveSubtext: "FEAR OF INTIMACY + DEFENSIVE SARCASM"
            );
        }

        private static DateeContext MakeDateeContextWithTarget()
        {
            var target = new ResolvedRevelationTarget 
            { 
                Registry = "STAKE", 
                Index = 13, 
                Field = "STAKE_LINE", 
                Manner = "ACCIDENTAL_SLIP", 
                StemText = "laminated Camino map", 
                TransitionStyle = "ACCIDENTAL_SLIP" 
            };

            return new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string, string)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hey",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 1.0,
                playerName: "GERALD",
                dateeName: "VELVET",
                resolvedTarget: target,
                cognitiveSubtext: "FEAR OF INTIMACY + DEFENSIVE SARCASM"
            );
        }

        [Fact]
        public void BuildDialogueOptionsPrompt_InjectsTransitionDirective_WhenResolvedTargetIsPresent()
        {
            var context = MakeDialogueContextWithTarget();
            var trace = SessionDocumentBuilder.BuildDialogueOptionsPromptEx(context);

            string engine = ExtractEngineState(trace.Text);

            Assert.Contains("Transition target: laminated Camino map", engine);
            Assert.Contains("Apply this specifically to the final option", engine);
            Assert.Contains("Transition style for the final option: ACCIDENTAL_SLIP", engine);
            Assert.Contains("Cognitive subtext: FEAR OF INTIMACY + DEFENSIVE SARCASM", engine);
            Assert.DoesNotContain("STAKE", engine);
            Assert.DoesNotContain("#13", engine);
            Assert.DoesNotContain("STAKE_LINE", engine);
        }

        [Fact]
        public void BuildDateePrompt_InjectsTransitionDirective_WhenResolvedTargetIsPresent()
        {
            var context = MakeDateeContextWithTarget();
            var trace = SessionDocumentBuilder.BuildDateePromptEx(context);

            string engine = ExtractEngineState(trace.Text);

            Assert.Contains("Transition target: laminated Camino map", engine);
            Assert.Contains("Apply this specifically to the datee response", engine);
            Assert.Contains("Transition style for the datee response: ACCIDENTAL_SLIP", engine);
            Assert.Contains("Cognitive subtext: FEAR OF INTIMACY + DEFENSIVE SARCASM", engine);
            Assert.DoesNotContain("STAKE", engine);
            Assert.DoesNotContain("#13", engine);
            Assert.DoesNotContain("STAKE_LINE", engine);
            Assert.DoesNotContain(trace.Spans, span => span.Key == "datee-transition-directive");
            Assert.DoesNotContain(trace.Spans, span => span.Key == "cognitive-subtext-directive");
            AssertCatalogSpan(trace, "engine-state-transition-target-line");
            AssertCatalogSpan(trace, "engine-state-transition-style-line");
            AssertCatalogSpan(trace, "engine-state-cognitive-subtext-line");
        }

        private static string ExtractEngineState(string prompt)
        {
            int start = prompt.IndexOf("<ENGINE_STATE>", StringComparison.Ordinal);
            int end = prompt.IndexOf("</ENGINE_STATE>", start, StringComparison.Ordinal);
            return prompt.Substring(start, end - start);
        }

        private static void AssertCatalogSpan(Pinder.Core.Text.PromptTraceResult trace, string key)
        {
            Assert.Contains(
                trace.Spans,
                span => span.Key == key && span.SourceFile == "data/prompts/templates.yaml");
        }
    }
}
