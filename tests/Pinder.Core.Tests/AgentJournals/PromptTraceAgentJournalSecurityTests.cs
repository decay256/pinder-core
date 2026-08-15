using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Text;

namespace Pinder.Core.Tests.AgentJournals
{
    public sealed class PromptTraceAgentJournalSecurityTests
    {
        [Theory]
        [InlineData("/etc/passwd")]
        [InlineData("https://example.invalid/source")]
        [InlineData("gh" + "p_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")]
        [InlineData("xo" + "xb-1234567890-abcdefghijklmnop")]
        [InlineData("AK" + "IAIOSFODNN7EXAMPLE")]
        public void Adapter_RejectsUnsafeResolverOutput(string resolvedSourceId)
        {
            var trace = new PromptTraceResult(
                "text",
                new[] { new AnnotatedSpan(0, 4, "data/prompts/structural.yaml", "prompt.key") });
            var resolver = PromptTraceSourceIdentityTestResolver.Map(
                "data/prompts/structural.yaml",
                resolvedSourceId);

            var error = Assert.Throws<PromptTraceSourceIdentityException>(() =>
                trace.ToAgentJournalInputDocument("doc.user", AgentJournalInputRole.User, resolver));

            Assert.Equal(PromptTraceSourceIdentityException.InvalidResolvedSourceIdentity, error.Code);
            Assert.DoesNotContain("data/prompts/structural.yaml", error.Message);
        }
    }
}
