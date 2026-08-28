using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue1261_DialogueContextInjectionTests
    {
        private static readonly Guid AvatarId = Guid.Parse("850cd09b-7808-4b06-a446-9fe0f9782898");
        private static readonly Guid DateeId = Guid.Parse("9bbc793b-f479-4cef-b35e-fd1f7964a371");

        private static ResolvedRevelationTarget Target()
            => new ResolvedRevelationTarget
            {
                Registry = EmotionStemSelectionRules.StakeRegistry,
                Index = 13,
                Field = "STAKE_LINE",
                Manner = "CURATED_BUFFER",
                StemText = "laminated Camino map",
                TransitionStyle = "buffered disclosure",
            };

        private static DialogueContext MakeDialogueContextWithTarget()
        {
            ResolvedRevelationTarget target = Target();
            AvatarRevelationTarget typedTarget = AvatarRevelationTarget.FromLegacyResolvedTarget(
                target,
                AvatarId,
                AvatarId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceIds.PsychologicalStake(AvatarId, 13));
            var cognitiveFact = new OwnedPromptFactV1(
                AvatarId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.CognitiveSubtext,
                PromptFactSourceIds.CognitiveSubtext(AvatarId, 3),
                "FEAR OF INTIMACY + DEFENSIVE SARCASM");
            
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
                avatarRevelationTarget: typedTarget,
                cognitiveSubtextFact: cognitiveFact,
                recipientCharacterId: AvatarId
            );
        }

        private static DateeContext MakeDateeContextWithTarget()
        {
            ResolvedRevelationTarget target = Target();
            DateeReactionTarget typedTarget = DateeReactionTarget.FromLegacyResolvedTarget(
                target,
                DateeId,
                DateeId,
                ConversationParticipantRole.Datee,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceIds.PsychologicalStake(DateeId, 13));
            var cognitiveFact = new OwnedPromptFactV1(
                DateeId,
                ConversationParticipantRole.Datee,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.CognitiveSubtext,
                PromptFactSourceIds.CognitiveSubtext(DateeId, 3),
                "FEAR OF INTIMACY + DEFENSIVE SARCASM");

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
                currentTurn: 3,
                interestBeforeState: InterestState.Lukewarm,
                interestAfterState: InterestState.Lukewarm,
                dateeReactionTarget: typedTarget,
                cognitiveSubtextFact: cognitiveFact,
                recipientCharacterId: DateeId
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
            Assert.Contains("Transition style for the final option: buffered disclosure", engine);
            Assert.Contains("Cognitive subtext: FEAR OF INTIMACY + DEFENSIVE SARCASM", engine);
            Assert.DoesNotContain("STAKE", engine);
            Assert.DoesNotContain("#13", engine);
            Assert.DoesNotContain("STAKE_LINE", engine);
        }

        [Fact]
        public void BuildDateePrompt_UsesAcceptedPlanInsteadOfRawDirectives()
        {
            var context = MakeDateeContextWithTarget();
            var direction = new CharacterEmotionalDirection(
                "relief",
                CharacterEmotionalDirection.NoneSecondaryEmotion,
                "controlled",
                4,
                "escalating",
                "fear of being dismissed",
                "reads the message as a possible opening",
                "leans toward a careful answer",
                "keeps one boundary",
                "answers with cautious warmth");
            DateeResponsePlanCompilationResult compiled = new DateeResponsePlanCompiler().Compile(
                DateeResponsePlanInput.From(context, direction));
            Assert.Equal(DateeResponsePlanCompilationOutcome.Accepted, compiled.Outcome);

            var trace = SessionDocumentBuilder.BuildDateePerformancePromptEx(context, compiled.Plan!);

            Assert.Contains("[ENGINE — DATEE RESPONSE PLAN]", trace.Text, StringComparison.Ordinal);
            Assert.Contains("\"movement\":\"hold\"", trace.Text, StringComparison.Ordinal);
            Assert.Contains("\"disclosure\":\"voluntary\"", trace.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Transition target:", trace.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Transition style for the datee response:", trace.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Cognitive subtext:", trace.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(trace.Spans, span => span.Key == "datee-transition-directive");
            Assert.DoesNotContain(trace.Spans, span => span.Key == "cognitive-subtext-directive");
            AssertCatalogSpan(trace, "datee-response-plan-performance");
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
