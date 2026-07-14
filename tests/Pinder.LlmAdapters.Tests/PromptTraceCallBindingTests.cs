using System;
using System.Linq;
using System.Threading.Tasks;
using Pinder.Core.Text;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class PromptTraceCallBindingTests
    {
        [Fact]
        public void ExplicitCallIds_BindOnlyTracesPendingAtEachCall()
        {
            var service = new InMemoryPromptTraceService();
            IPromptTraceService contract = service;

            using (service.BeginSessionScope("session-a"))
            {
                service.RecordTrace("first-system", Trace("first system"));
                service.RecordTrace("first-user", Trace("first user"));
                contract.RecordModelResponse("first response", "call-1");

                service.RecordTrace("second-user", Trace("second user"));
                service.RecordModelResponse("second response", "call-2");
            }

            var sequence = service.GetSequence("session-a");
            Assert.Equal(3, sequence.Count);
            Assert.All(sequence.Take(2), run =>
            {
                Assert.Equal("call-1", run.CallId);
                Assert.Equal("first response", run.ModelResponse);
            });
            Assert.Equal("call-2", sequence[2].CallId);
            Assert.Equal("second response", sequence[2].ModelResponse);
        }

        [Fact]
        public void ExplicitCallId_DoesNotRewriteAlreadyBoundRetryTrace()
        {
            var service = new InMemoryPromptTraceService();

            using (service.BeginSessionScope("session-a"))
            {
                service.RecordTrace("datee", Trace("same retry prompt"));
                service.RecordModelResponse("first response", "attempt-1");
                service.RecordModelResponse("retry response", "attempt-2");
            }

            var run = Assert.Single(service.GetSequence("session-a"));
            Assert.Equal("attempt-1", run.CallId);
            Assert.Equal("first response", run.ModelResponse);
        }

        [Fact]
        public void NestedScopes_BindResponsesToTheirExactRuns()
        {
            var service = new InMemoryPromptTraceService();

            using (service.BeginSessionScope("shared-session"))
            {
                service.RecordTrace("outer", Trace("outer prompt"));

                using (service.BeginSessionScope("shared-session"))
                {
                    service.RecordTrace("inner", Trace("inner prompt"));
                    service.RecordModelResponse("inner response", "inner-call");
                }

                service.RecordModelResponse("outer response", "outer-call");
            }

            var sequence = service.GetSequence("shared-session");
            Assert.Equal(2, sequence.Count);
            var outer = Assert.Single(sequence.Where(run => run.PromptType == "outer"));
            var inner = Assert.Single(sequence.Where(run => run.PromptType == "inner"));
            Assert.Equal("outer-call", outer.CallId);
            Assert.Equal("outer response", outer.ModelResponse);
            Assert.Equal("inner-call", inner.CallId);
            Assert.Equal("inner response", inner.ModelResponse);
            Assert.NotEqual(outer.RunId, inner.RunId);
        }

        [Fact]
        public async Task ParallelScopes_DoNotCrossBindResponses()
        {
            var service = new InMemoryPromptTraceService();

            await Task.WhenAll(
                Task.Run(() => RecordScopedCall(service, "session-a", "prompt-a", "response-a", "call-a")),
                Task.Run(() => RecordScopedCall(service, "session-b", "prompt-b", "response-b", "call-b")));

            var first = Assert.Single(service.GetSequence("session-a"));
            var second = Assert.Single(service.GetSequence("session-b"));
            Assert.Equal("call-a", first.CallId);
            Assert.Equal("response-a", first.ModelResponse);
            Assert.Equal("call-b", second.CallId);
            Assert.Equal("response-b", second.ModelResponse);
            Assert.NotEqual(first.RunId, second.RunId);
        }

        [Fact]
        public void LegacyResponseBinding_RemainsUnboundAndDoesNotOverwrite()
        {
            var service = new InMemoryPromptTraceService();

            using (service.BeginSessionScope("legacy-session"))
            {
                service.RecordTrace("dialogue-options", Trace("legacy prompt"));
                service.RecordModelResponse("legacy response");
                service.RecordModelResponse("later response");
            }

            var run = Assert.Single(service.GetSequence("legacy-session"));
            Assert.Null(run.CallId);
            Assert.Equal("legacy response", run.ModelResponse);
            Assert.NotNull(run.ResponseTimestamp);
        }

        private static void RecordScopedCall(
            InMemoryPromptTraceService service,
            string sessionId,
            string prompt,
            string response,
            string callId)
        {
            using (service.BeginSessionScope(sessionId))
            {
                service.RecordTrace("datee", Trace(prompt));
                service.RecordModelResponse(response, callId);
            }
        }

        private static PromptTraceResult Trace(string text)
            => new PromptTraceResult(text, Array.Empty<AnnotatedSpan>());
    }
}
