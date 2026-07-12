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
    [Collection("PromptTraceSingleton")]
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
        public void Test_InMemoryPromptTraceService_RecordingAndRetrieval()
        {
            var service = new InMemoryPromptTraceService();
            service.Clear();

            var spans = new List<AnnotatedSpan> { new AnnotatedSpan(0, 10, "file.yaml", "key") };
            var result = new PromptTraceResult("Hello World", spans);

            service.RecordTrace("dialogue-options", result);

            var retrieved = service.GetLastTrace("dialogue-options");
            Assert.NotNull(retrieved);
            Assert.Equal("Hello World", retrieved!.Text);
            Assert.Single(retrieved.Spans);
            Assert.Equal("file.yaml", retrieved.Spans[0].SourceFile);
            Assert.Equal("key", retrieved.Spans[0].Key);
        }

        [Fact]
        public void Test_InMemoryPromptTraceService_ScopesTracesBySession()
        {
            var service = new InMemoryPromptTraceService();
            service.Clear();

            var spans = new List<AnnotatedSpan> { new AnnotatedSpan(0, 5, "file.yaml", "key") };

            using (service.BeginSessionScope("session-a", "anthropic/test-model", "speculation", 3, 2))
            {
                service.RecordTrace("dialogue-options", new PromptTraceResult("alpha", spans));
                service.RecordTrace("datee", new PromptTraceResult("alpha datee", spans));
            }

            using (service.BeginSessionScope("session-b", "openai/test-model", "live_turn"))
            {
                service.RecordTrace("dialogue-options", new PromptTraceResult("beta", spans));
            }

            var sessionA = service.GetSequence("session-a");
            var sessionB = service.GetSequence("session-b");

            Assert.Equal(2, sessionA.Count);
            Assert.Single(sessionB);
            Assert.All(sessionA, r => Assert.Equal("session-a", r.SessionId));
            Assert.All(sessionA, r => Assert.Equal(sessionA[0].RunId, r.RunId));
            Assert.All(sessionA, r => Assert.Equal("speculation", r.RunKind));
            Assert.All(sessionA, r => Assert.Equal("anthropic", r.Provider));
            Assert.All(sessionA, r => Assert.Equal("anthropic/test-model", r.ProviderModel));
            Assert.All(sessionA, r => Assert.Equal(3, r.TurnNumber));
            Assert.All(sessionA, r => Assert.Equal(2, r.BranchOption));
            Assert.Equal("alpha", service.GetLastTrace("dialogue-options", "session-a")!.Text);
            Assert.Equal("beta", service.GetLastTrace("dialogue-options", "session-b")!.Text);

            service.ClearSession("session-a");

            Assert.Empty(service.GetSequence("session-a"));
            Assert.Single(service.GetSequence("session-b"));
        }

        [Fact]
        public void Test_SessionDocumentBuilder_DialogueOptionsPrompt_GeneratesTrace()
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

            InMemoryPromptTraceService.Instance.Clear();
            var prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);

            Assert.NotEmpty(prompt);

            var trace = InMemoryPromptTraceService.Instance.GetLastTrace("dialogue-options");
            Assert.NotNull(trace);
            Assert.Equal(prompt, trace!.Text);

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
                resolvedTarget: target,
                cognitiveSubtext: "fear of being too visible");

            var dialogueTrace = SessionDocumentBuilder.BuildDialogueOptionsPromptEx(dialogueContext);

            AssertCatalogSpan(dialogueTrace, "cold-opener-rule");
            AssertCatalogSpan(dialogueTrace, "shadow-state-heading");
            AssertCatalogSpan(dialogueTrace, "shadow-taint-fixation");
            AssertCatalogSpan(dialogueTrace, "stake-coverage-summary");
            AssertCatalogSpan(dialogueTrace, "stake-coverage-untouched-directive");
            AssertCatalogSpan(dialogueTrace, "player-transition-directive");
            AssertCatalogSpan(dialogueTrace, "cognitive-subtext-directive");

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
                resolvedTarget: target,
                cognitiveSubtext: "fear of being too visible");

            var dateeTrace = SessionDocumentBuilder.BuildDateePromptEx(dateeContext);

            AssertCatalogSpan(dateeTrace, "response-timing-header");
            AssertCatalogSpan(dateeTrace, "response-timing-approximate");
            AssertCatalogSpan(dateeTrace, "datee-shadow-state-heading");
            AssertCatalogSpan(dateeTrace, "shadow-taint-fixation");
            AssertCatalogSpan(dateeTrace, "datee-transition-directive");
            AssertCatalogSpan(dateeTrace, "cognitive-subtext-directive");
        }

        private static void AssertCatalogSpan(PromptTraceResult trace, string key)
        {
            Assert.Contains(
                trace.Spans,
                s => s.Key == key && s.SourceFile == "data/prompts/templates.yaml");
        }
    }
}
