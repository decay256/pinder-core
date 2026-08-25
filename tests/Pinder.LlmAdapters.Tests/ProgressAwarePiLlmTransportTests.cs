using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pi.AI;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters.Pi;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class ProgressAwarePiLlmTransportTests
    {
        [Fact]
        public async Task ProgressAwarePi_SendAsyncReportsSemanticEventsWithoutChangingText()
        {
            AssistantMessage final = TextAndToolResponse("accepted answer");
            var transport = Transport(final);
            var progress = new RecordingProgress();

            string result = await ((IProgressAwareLlmTransport)transport)
                .SendWithProgressAsync("system secret prompt", "user secret prompt", progress);

            Assert.Equal("accepted answer", result);
            AssertProgressKinds(progress);
            AssertProgressShapeIsClassificationOnly();
        }

        [Fact]
        public async Task ProgressAwarePi_AllBufferedCallShapesReportProgressKinds()
        {
            AssistantMessage textFinal = TextAndToolResponse("accepted answer");
            AssistantMessage structuredFinal = StructuredToolResponse();
            StructuredLlmRequest request = Request();

            var simpleProgress = new RecordingProgress();
            await ((IProgressAwareLlmTransport)Transport(textFinal))
                .SendWithProgressAsync("system", "user", simpleProgress);

            var conversationProgress = new RecordingProgress();
            await ((IProgressAwareConversationLlmTransport)Transport(textFinal))
                .SendConversationWithProgressAsync(
                    "system",
                    new[] { ConversationMessage.User("prior player line") },
                    "user",
                    conversationProgress);

            var structuredProgress = new RecordingProgress();
            await ((IProgressAwareStructuredLlmTransport)Transport(structuredFinal))
                .SendStructuredWithProgressAsync(request, structuredProgress);

            var structuredConversationProgress = new RecordingProgress();
            await ((IProgressAwareStructuredConversationLlmTransport)Transport(structuredFinal))
                .SendStructuredConversationWithProgressAsync(
                    request,
                    new[] { ConversationMessage.Assistant("prior assistant line") },
                    structuredConversationProgress);

            AssertProgressKinds(simpleProgress);
            AssertProgressKinds(conversationProgress);
            AssertProgressKinds(structuredProgress);
            AssertProgressKinds(structuredConversationProgress);
        }

        [Fact]
        public async Task ProgressAwarePi_StructuredResultMatchesBufferedCompletePath()
        {
            AssistantMessage final = StructuredToolResponse();
            StructuredLlmRequest request = Request(new Dictionary<string, string> { ["trace"] = "classification-only" });
            var progress = new RecordingProgress();
            var completeTransport = Transport(final);
            var progressTransport = Transport(final);

            StructuredLlmResponse complete = await completeTransport.SendStructuredAsync(request);
            StructuredLlmResponse aware = await ((IProgressAwareStructuredLlmTransport)progressTransport)
                .SendStructuredWithProgressAsync(request, progress);

            Assert.Equal(complete.JsonText, aware.JsonText);
            Assert.Equal(complete.Provider, aware.Provider);
            Assert.Equal(complete.Model, aware.Model);
            Assert.Equal(complete.UsedNativeStructuredOutput, aware.UsedNativeStructuredOutput);
            Assert.Equal(complete.ValidationMode, aware.ValidationMode);
            Assert.Equal(complete.Metadata, aware.Metadata);
            AssertProgressKinds(progress);
        }

        [Fact]
        public async Task ProgressAwarePi_TextFallbackStructuredResultMatchesBufferedCompletePath()
        {
            AssistantMessage final = Response("{\"ok\":true}");
            StructuredLlmRequest request = Request();
            var progress = new RecordingProgress();
            var completeTransport = Transport(final, includeToolEvents: false);
            var progressTransport = Transport(final, includeToolEvents: false);

            StructuredLlmResponse complete = await completeTransport.SendStructuredAsync(request);
            StructuredLlmResponse aware = await ((IProgressAwareStructuredLlmTransport)progressTransport)
                .SendStructuredWithProgressAsync(request, progress);

            Assert.Equal(complete.JsonText, aware.JsonText);
            Assert.False(aware.UsedNativeStructuredOutput);
            Assert.Equal("pi_text_fallback", aware.ValidationMode);
            Assert.Equal(complete.ValidationMode, aware.ValidationMode);
            Assert.Contains(progress.Events, e => e.Kind == LlmProgressKind.Text);
            Assert.Contains(progress.Events, e => e.Kind == LlmProgressKind.Completion);
        }

        [Fact]
        public async Task ProgressAwarePi_CancellationRejectsPartialBufferedText()
        {
            using var cts = new CancellationTokenSource();
            var progress = new RecordingProgress
            {
                OnReport = entry =>
                {
                    if (entry.Kind == LlmProgressKind.Text) cts.Cancel();
                }
            };
            var transport = Transport(TextAndToolResponse("accepted answer"));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ((IProgressAwareLlmTransport)transport)
                    .SendWithProgressAsync("system", "user", progress, ct: cts.Token));

            Assert.Contains(progress.Events, e => e.Kind == LlmProgressKind.Text);
            Assert.DoesNotContain(progress.Events, e => e.Kind == LlmProgressKind.Completion);
        }

        [Fact]
        public async Task ProgressAwarePi_RawStreamResultFailureIsNormalizedWithoutPartialAcceptedResult()
        {
            const string partialContent = "partial secret output";
            AssistantMessage partial = Response(partialContent);
            var progress = new RecordingProgress();
            var transport = new PiLlmTransport(
                Model("model-1"),
                (_, __, ___) => Task.FromResult(partial),
                (_, __, ___) => FailingResultStream(partial, partialContent));

            LlmTransportException error = await Assert.ThrowsAsync<LlmTransportException>(() =>
                ((IProgressAwareLlmTransport)transport)
                    .SendWithProgressAsync("system", "user", progress));

            Assert.Equal(LlmFailureKind.Unknown, error.FailureKind);
            Assert.Equal("The LLM response stream failed.", error.Message);
            Assert.Null(error.InnerException);
            Assert.DoesNotContain(partialContent, error.ToString(), StringComparison.Ordinal);
            Assert.Contains(progress.Events, e => e.Kind == LlmProgressKind.ResponseStarted);
            Assert.Contains(progress.Events, e => e.Kind == LlmProgressKind.Text);
            Assert.DoesNotContain(progress.Events, e => e.Kind == LlmProgressKind.Completion);
        }

        private static AssistantMessageEventStream FailingResultStream(
            AssistantMessage partial,
            string partialContent)
        {
            AssistantMessageEventStream stream = EventStreams.CreateAssistantMessageEventStream();
            stream.Push(new AssistantMessageStartEvent(partial));
            stream.Push(new TextDeltaEvent(0, partialContent, partial));
            FieldInfo finalResultField = typeof(AssistantMessageEventStream).BaseType!
                .GetField("finalResult", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var finalResult = (TaskCompletionSource<AssistantMessage>)finalResultField.GetValue(stream)!;
            Assert.True(finalResult.TrySetException(
                new InvalidOperationException("Malformed provider response stream: " + partialContent)));
            stream.End();
            return stream;
        }

        private static PiLlmTransport Transport(
            AssistantMessage final,
            bool includeTextEvents = true,
            bool includeToolEvents = true)
        {
            return new PiLlmTransport(
                Model("model-1"),
                (_, __, ___) => Task.FromResult(final),
                (_, __, ___) => Stream(final, includeTextEvents, includeToolEvents));
        }

        private static AssistantMessageEventStream Stream(
            AssistantMessage final,
            bool includeTextEvents,
            bool includeToolEvents)
        {
            AssistantMessageEventStream stream = EventStreams.CreateAssistantMessageEventStream();
            stream.Push(new AssistantMessageStartEvent(final));
            stream.Push(new ThinkingStartEvent(0, final));
            stream.Push(new ThinkingDeltaEvent(0, "private chain of thought", final));
            stream.Push(new ThinkingEndEvent(0, "private chain of thought", final));
            if (includeTextEvents)
            {
                stream.Push(new TextStartEvent(1, final));
                stream.Push(new TextDeltaEvent(1, "secret output", final));
                stream.Push(new TextEndEvent(1, "secret output", final));
            }

            if (includeToolEvents)
            {
                var call = new ToolCall("call-1", "datee_reaction", new Dictionary<string, object?>
                {
                    ["emotion"] = "suspicion",
                    ["private_argument"] = "must not enter progress"
                });
                stream.Push(new ToolCallStartEvent(2, final));
                stream.Push(new ToolCallDeltaEvent(2, "{\"private_argument\":\"must not enter progress\"}", final));
                stream.Push(new ToolCallEndEvent(2, call, final));
            }

            stream.Push(new AssistantMessageDoneEvent(final.StopReason, final));
            return stream;
        }

        private static StructuredLlmRequest Request(IReadOnlyDictionary<string, string>? metadata = null)
            => new StructuredLlmRequest(
                "datee reaction",
                "1",
                "{\"type\":\"object\",\"properties\":{\"emotion\":{\"type\":\"string\"},\"intensity\":{\"type\":\"integer\"}},\"required\":[\"emotion\",\"intensity\"],\"additionalProperties\":false}",
                "system",
                "user",
                0.2,
                256,
                "emotional_director",
                metadata);

        private static AssistantMessage TextAndToolResponse(string text)
            => new AssistantMessage(
                new IAssistantMessageContent[]
                {
                    new TextContent(text),
                    new ToolCall("call-1", "datee_reaction", new Dictionary<string, object?>
                    {
                        ["emotion"] = "suspicion"
                    })
                },
                KnownApi.OpenAIResponses,
                KnownProvider.OpenAI,
                "model-1",
                Usage.Zero,
                StopReason.Stop,
                1234L);

        private static AssistantMessage StructuredToolResponse()
            => new AssistantMessage(
                new IAssistantMessageContent[]
                {
                    new ToolCall("call-1", "datee_reaction", new Dictionary<string, object?>
                    {
                        ["emotion"] = "suspicion",
                        ["intensity"] = 4L
                    })
                },
                KnownApi.OpenAIResponses,
                KnownProvider.OpenAI,
                "model-1",
                Usage.Zero,
                StopReason.ToolUse,
                1234L);

        private static AssistantMessage Response(string text)
            => new AssistantMessage(
                new List<IAssistantMessageContent> { new TextContent(text) },
                KnownApi.OpenAIResponses,
                KnownProvider.OpenAI,
                "model-1",
                Usage.Zero,
                StopReason.Stop,
                1234L);

        private static Model Model(string id)
            => new Model
            {
                Id = id,
                Name = id,
                Api = KnownApi.OpenAIResponses,
                Provider = KnownProvider.OpenAI
            };

        private static void AssertProgressKinds(RecordingProgress progress)
        {
            LlmProgressKind[] firstSeen = progress.Events
                .Select(e => e.Kind)
                .Distinct()
                .ToArray();
            Assert.Equal(
                new[]
                {
                    LlmProgressKind.ResponseStarted,
                    LlmProgressKind.Reasoning,
                    LlmProgressKind.Text,
                    LlmProgressKind.ToolCall,
                    LlmProgressKind.Completion
                },
                firstSeen);
            Assert.All(progress.Events, e => Assert.NotEqual(default, e.Timestamp));
        }

        private static void AssertProgressShapeIsClassificationOnly()
        {
            string[] publicProperties = typeof(LlmProgressEvent)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(new[] { "Kind", "Timestamp" }, publicProperties);
        }

        private sealed class RecordingProgress : IProgress<LlmProgressEvent>
        {
            public List<LlmProgressEvent> Events { get; } = new List<LlmProgressEvent>();

            public Action<LlmProgressEvent>? OnReport { get; set; }

            public void Report(LlmProgressEvent value)
            {
                Events.Add(value);
                OnReport?.Invoke(value);
            }
        }
    }
}
