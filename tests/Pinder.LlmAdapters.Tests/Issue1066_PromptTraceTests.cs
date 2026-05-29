using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Pinder.Core.Conversation;
using Pinder.Core.Rolls;
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
        public void Test_SessionDocumentBuilder_DialogueOptionsPrompt_GeneratesTrace()
        {
            PromptCatalogInitializer.Initialize();

            var context = new DialogueContext(
                playerPrompt: "You are reuben.",
                opponentPrompt: "You are talking to velvet.",
                conversationHistory: new List<(string Sender, string Text)> { ("Velvet", "Hello") },
                opponentLastMessage: "Hello",
                activeTraps: new List<string>(),
                currentInterest: 10,
                currentTurn: 3
            );

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
    }
}
