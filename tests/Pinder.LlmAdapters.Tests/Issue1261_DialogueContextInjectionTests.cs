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

            // Verify injection format inside [ENGINE] block (per issue #1261)
            // - names the target by index, field, literal string
            // - steers exactly one option (OPTION_C) for player
            // - therapeutic cognitive subtext (DERIVED FEELING + DEFENSE REACTION)
            Assert.DoesNotContain("STAKE #13", trace.Text);
            Assert.Contains("laminated Camino map", trace.Text);
            Assert.Contains("ACCIDENTAL_SLIP", trace.Text);
            Assert.Contains("FEAR OF INTIMACY + DEFENSIVE SARCASM", trace.Text);
            Assert.Contains("OPTION_C", trace.Text);
            Assert.Contains("Treat the idea as emotional guidance", trace.Text);
            Assert.Contains("Keep it as subtext", trace.Text);
        }

        [Fact]
        public void BuildDateePrompt_InjectsTransitionDirective_WhenResolvedTargetIsPresent()
        {
            var context = MakeDateeContextWithTarget();
            var trace = SessionDocumentBuilder.BuildDateePromptEx(context);

            Assert.DoesNotContain("STAKE #13", trace.Text);
            Assert.Contains("laminated Camino map", trace.Text);
            Assert.Contains("ACCIDENTAL_SLIP", trace.Text);
            Assert.Contains("FEAR OF INTIMACY + DEFENSIVE SARCASM", trace.Text);
            Assert.Contains("Treat the idea as emotional guidance", trace.Text);
            Assert.Contains("Keep it as subtext", trace.Text);
        }
    }
}
