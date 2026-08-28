using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Text;
using Pinder.LlmAdapters;
using Pinder.Core.TestCommon;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class Issue1066_PromptTraceTests
    {
        [Fact]
        public void Test_AnnotatedStringBuilder_SpansAndTracking()
        {
            var sb = new AnnotatedStringBuilder();
            sb.Append("Header text\n", "file1.yaml", "key1");
            sb.Append("Body text\n", "file2.yaml", "key2");

            Assert.Equal("Header text\nBody text\n", sb.ToString());
            Assert.Equal(2, sb.Spans.Count);

            var span1 = sb.Spans[0];
            Assert.Equal(0, span1.Start);
            Assert.Equal(12, span1.End);
            Assert.Equal("file1.yaml", span1.SourceFile);
            Assert.Equal("key1", span1.Key);

            var span2 = sb.Spans[1];
            Assert.Equal(12, span2.Start);
            Assert.Equal(22, span2.End);
            Assert.Equal("file2.yaml", span2.SourceFile);
            Assert.Equal("key2", span2.Key);
        }

        [Fact]
        public void Test_SessionDocumentBuilder_DialogueOptionsPrompt_GeneratesAnnotatedResult()
        {
            PromptCatalogInitializer.Initialize();

            var context = new DialogueContext(
                playerAvatarPrompt: "You are reuben.",
                dateePrompt: "You are talking to velvet.",
                conversationHistory: new List<(string Sender, string Text)> { ("Velvet", "Hello") },
                dateeLastMessage: "Hello",
                activeTraps: new List<string>(),
                currentInterest: 10,
                currentTurn: 3
            , availableStats: new[] { Pinder.Core.Stats.StatType.Charm, Pinder.Core.Stats.StatType.Rizz, Pinder.Core.Stats.StatType.Honesty,  }, playerName: "P", dateeName: "O");

            PromptTraceResult trace = SessionDocumentBuilder.BuildDialogueOptionsPromptEx(context);

            Assert.NotEmpty(trace.Text);

            // Verify that we tracked structural keys and templates
            Assert.Contains(trace.Spans, s => s.Key == "pivot-directive");
            Assert.Contains(trace.Spans, s => s.Key == "engine-options-block");
            Assert.Contains(trace.Spans, s => s.Key == "dialogue-options-instruction");
        }

        [Fact]
        public void Test_SessionDocumentBuilder_GameplayDirectivesUseCatalogSpans()
        {
            PromptCatalogInitializer.Initialize();

            var target = new ResolvedRevelationTarget
            {
                Registry = "STAKE",
                Index = 7,
                Field = "STAKE_LINE",
                Manner = "ACCIDENTAL_SLIP",
                StemText = "the song I still cannot hear",
                TransitionStyle = "ACCIDENTAL_SLIP",
            };
            Guid avatarId = Guid.Parse("10660000-0000-0000-0000-000000000001");
            var targetFact = new OwnedPromptFactV1(
                avatarId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.PsychologicalStake,
                PromptFactSourceIds.PsychologicalStake(avatarId, target.Index),
                target.StemText);
            var cognitiveFact = new OwnedPromptFactV1(
                avatarId,
                ConversationParticipantRole.PlayerAvatar,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.CognitiveSubtext,
                PromptFactSourceIds.CognitiveSubtext(avatarId, 3),
                "fear of being too visible");

            var dialogueContext = new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string Sender, string Text)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                shadowThresholds: new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Fixation, 8 },
                },
                playerName: "P",
                dateeName: "O",
                currentTurn: 3,
                availableStats: new[] { StatType.Charm, StatType.Rizz, StatType.Honesty },
                stakeLines: new[] { "1. The song I still cannot hear without leaving the room" },
                avatarRevelationTarget: AvatarRevelationTarget.Create(avatarId, targetFact, target),
                cognitiveSubtextFact: cognitiveFact,
                recipientCharacterId: avatarId);

            var dialogueTrace = SessionDocumentBuilder.BuildDialogueOptionsPromptEx(dialogueContext);

            AssertCatalogSpan(dialogueTrace, "cold-opener-rule");
            AssertCatalogSpan(dialogueTrace, "shadow-state-heading");
            AssertCatalogSpan(dialogueTrace, "shadow-taint-fixation");
            AssertCatalogSpan(dialogueTrace, "stake-coverage-summary");
            AssertCatalogSpan(dialogueTrace, "stake-coverage-untouched-directive");
            AssertCatalogSpan(dialogueTrace, "engine-state-transition-target-line");
            AssertCatalogSpan(dialogueTrace, "engine-state-transition-style-line");
            AssertCatalogSpan(dialogueTrace, "engine-state-cognitive-subtext-line");

            Guid dateeId = Guid.Parse("10660000-0000-0000-0000-000000000002");
            var dateeResolvedTarget = new ResolvedRevelationTarget
            {
                Registry = target.Registry,
                Index = target.Index,
                Field = target.Field,
                Manner = "INTIMATE_BREAKTHROUGH",
                StemText = target.StemText,
                TransitionStyle = "INTIMATE_BREAKTHROUGH",
            };
            var dateeTarget = DateeReactionTarget.FromLegacyResolvedTarget(
                dateeResolvedTarget,
                dateeId,
                dateeId,
                ConversationParticipantRole.Datee,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceIds.PsychologicalStake(dateeId, target.Index));
            var dateeCognitiveFact = new OwnedPromptFactV1(
                dateeId,
                ConversationParticipantRole.Datee,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.CognitiveSubtext,
                PromptFactSourceIds.CognitiveSubtext(dateeId, 3),
                "fear of being too visible");
            var dateeContext = new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: new List<(string Sender, string Text)>
                {
                    ("P", "hey"),
                },
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "hey",
                interestBefore: 8,
                interestAfter: 10,
                responseDelayMinutes: 2.5,
                shadowThresholds: new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Fixation, 8 },
                },
                playerName: "P",
                dateeName: "O",
                currentTurn: 3,
                dateeReactionTarget: dateeTarget,
                cognitiveSubtextFact: dateeCognitiveFact,
                recipientCharacterId: dateeId);

            var dateeTrace = DateePromptTestBuilder.BuildEx(dateeContext);

            Assert.DoesNotContain(dateeTrace.Spans, s => s.Key == "response-timing-header");
            Assert.DoesNotContain(dateeTrace.Spans, s => s.Key == "response-timing-approximate");
            AssertCatalogSpan(dateeTrace, "datee-shadow-state-heading");
            AssertCatalogSpan(dateeTrace, "shadow-taint-fixation");
            AssertCatalogSpan(dateeTrace, "datee-response-plan-performance");
            Assert.DoesNotContain(dateeTrace.Spans, s => s.Key == "engine-state-transition-target-line");
            Assert.DoesNotContain(dateeTrace.Spans, s => s.Key == "engine-state-transition-style-line");
            Assert.DoesNotContain(dateeTrace.Spans, s => s.Key == "engine-state-cognitive-subtext-line");
        }

        private static void AssertCatalogSpan(PromptTraceResult trace, string key)
        {
            Assert.Contains(
                trace.Spans,
                s => s.Key == key && s.SourceFile == "data/prompts/templates.yaml");
        }
    }
}
