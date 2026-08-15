using System;
using System.Linq;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Text;

namespace Pinder.Core.Tests.AgentJournals
{
    public sealed class PromptTraceAgentJournalAdapterTests
    {
        private static readonly IPromptTraceSourceIdentityResolver Resolver
            = PromptTraceSourceIdentityTestResolver.Map("prompt.catalog", "prompt.catalog");

        [Fact]
        public void Adapter_PreservesCompiledTextAndExistingSpanOffsets()
        {
            var trace = new PromptTraceResult(
                "raw-alpha-gap-omega",
                new[]
                {
                    new AnnotatedSpan(4, 9, "prompt.catalog", "alpha"),
                    new AnnotatedSpan(14, 19, "prompt.catalog", "omega"),
                });

            var document = trace.ToAgentJournalInputDocument("doc.user", AgentJournalInputRole.User, Resolver);

            Assert.Equal(trace.Text, document.Text);
            Assert.Equal(new[] { 0, 4, 9, 14 }, document.Ranges.Select(range => range.StartUtf16).ToArray());
            Assert.Equal(new[] { 4, 9, 14, 19 }, document.Ranges.Select(range => range.EndUtf16).ToArray());
            Assert.Equal(AgentJournalRangeKind.RuntimeGenerated, document.Ranges[0].RangeKind);
            Assert.Equal(AgentJournalRangeKind.Configured, document.Ranges[1].RangeKind);
            Assert.Equal(AgentJournalRangeKind.RuntimeGenerated, document.Ranges[2].RangeKind);
            Assert.Equal(AgentJournalRangeKind.Configured, document.Ranges[3].RangeKind);
            Assert.True(AgentJournalValidator.Validate(AgentJournalTestRecords.Invocation(documents: new[] { document })).IsValid);
        }

        [Fact]
        public void Adapter_UsesUtf16CodeUnitOffsets()
        {
            string text = "A\U0001F600B";
            var trace = new PromptTraceResult(text, new[] { new AnnotatedSpan(1, 3, "prompt.catalog", "emoji") });

            var document = trace.ToAgentJournalInputDocument("doc.user", AgentJournalInputRole.User, Resolver);

            Assert.Equal(4, text.Length);
            Assert.Equal(new[] { 0, 1, 3 }, document.Ranges.Select(range => range.StartUtf16).ToArray());
            Assert.Equal(new[] { 1, 3, 4 }, document.Ranges.Select(range => range.EndUtf16).ToArray());
        }

        [Fact]
        public void ExistingPromptTraceConstruction_RemainsBehaviorCompatible()
        {
            var builder = new AnnotatedStringBuilder();
            builder.Append("raw:");
            builder.Append("configured", "prompt.catalog", "key");
            builder.AppendLine();
            var result = new PromptTraceResult(builder.ToString(), builder.Spans);

            Assert.Equal("raw:configured" + Environment.NewLine, result.Text);
            Assert.Single(result.Spans);
            Assert.Equal(4, result.Spans[0].Start);
            Assert.Equal(14, result.Spans[0].End);
        }
    }
}
