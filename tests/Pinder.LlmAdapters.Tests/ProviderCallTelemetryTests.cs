using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pinder.Core.Interfaces;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;
using Pinder.LlmAdapters.Anthropic.Dto;
using Pinder.LlmAdapters.OpenAi;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class ProviderCallTelemetryTests
    {
        [Fact]
        public async Task AnthropicTransport_EmitsStructuredRetryAndCompletionTelemetry()
        {
            const string secretBody = "{\"error\":\"SECRET_PROVIDER_BODY_DO_NOT_LOG\"}";
            var events = new List<LlmCallTelemetryEvent>();
            var telemetry = new LlmCallTelemetryOptions(
                events.Add,
                sessionId: "session-123",
                turn: 7,
                branch: "branch-a",
                option: "option-b");

            var handler = new SequenceHandler(
                () =>
                {
                    var response = new HttpResponseMessage((HttpStatusCode)429)
                    {
                        Content = new StringContent(secretBody, Encoding.UTF8, "application/json")
                    };
                    response.Headers.TryAddWithoutValidation("Retry-After", "0");
                    return response;
                },
                () => AnthropicSuccess("recovered"));

            using var http = new HttpClient(handler);
            using var transport = new AnthropicTransport(
                "sk-ant-test",
                AnthropicModelIds.DefaultModel,
                http,
                telemetry);

            var result = await transport.SendAsync(
                "system",
                "user",
                phase: LlmPhase.Delivery);

            Assert.Equal("recovered", result);
            Assert.Equal(2, handler.CallCount);

            Assert.Collection(
                events,
                started => AssertEvent(
                    started,
                    LlmCallTelemetryEventNames.Started,
                    statusCode: null,
                    attempt: 1,
                    retryAfter: null,
                    exceptionType: null),
                retry => AssertEvent(
                    retry,
                    LlmCallTelemetryEventNames.Retry,
                    statusCode: 429,
                    attempt: 1,
                    retryAfter: TimeSpan.Zero,
                    exceptionType: null),
                completed => AssertEvent(
                    completed,
                    LlmCallTelemetryEventNames.Completed,
                    statusCode: 200,
                    attempt: 2,
                    retryAfter: null,
                    exceptionType: null));

            Assert.DoesNotContain(secretBody, Flatten(events));
            Assert.DoesNotContain("SECRET_PROVIDER_BODY_DO_NOT_LOG", Flatten(events));
        }

        [Fact]
        public async Task OpenAiTransport_EmitsSanitizedFailureTelemetry()
        {
            const string secretBody = "{\"error\":{\"message\":\"SECRET_PROVIDER_BODY_DO_NOT_LOG\"}}";
            var events = new List<LlmCallTelemetryEvent>();
            var telemetry = new LlmCallTelemetryOptions(
                events.Add,
                sessionId: "session-456",
                turn: 9,
                branch: "branch-b",
                option: "option-c");

            var handler = new SequenceHandler(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(secretBody, Encoding.UTF8, "application/json")
            });

            using var http = new HttpClient(handler);
            using var transport = new OpenAiTransport(
                "sk-test",
                "https://example.test",
                "gpt-test",
                http,
                telemetry: telemetry);

            var ex = await Assert.ThrowsAsync<LlmTransportException>(() =>
                transport.SendAsync("system", "user", phase: LlmPhase.OpponentResponse));

            Assert.DoesNotContain("SECRET_PROVIDER_BODY_DO_NOT_LOG", ex.Message);
            Assert.Equal(LlmFailureKind.Unknown, ex.FailureKind);
            Assert.IsType<HttpRequestException>(ex.InnerException);
            Assert.Collection(
                events,
                started => AssertOpenAiEvent(
                    started,
                    LlmCallTelemetryEventNames.Started,
                    statusCode: null,
                    attempt: 1,
                    exceptionType: null),
                failed => AssertOpenAiEvent(
                    failed,
                    LlmCallTelemetryEventNames.Failed,
                    statusCode: 400,
                    attempt: 1,
                    exceptionType: nameof(HttpRequestException)));

            Assert.DoesNotContain(secretBody, Flatten(events));
            Assert.DoesNotContain("SECRET_PROVIDER_BODY_DO_NOT_LOG", Flatten(events));
        }

        private static void AssertEvent(
            LlmCallTelemetryEvent actual,
            string eventName,
            int? statusCode,
            int attempt,
            TimeSpan? retryAfter,
            string? exceptionType)
        {
            Assert.Equal(eventName, actual.EventName);
            Assert.Equal("anthropic", actual.Provider);
            Assert.Equal(AnthropicModelIds.DefaultModel, actual.Model);
            Assert.Equal(LlmPhase.Delivery, actual.Phase);
            Assert.Equal("session-123", actual.SessionId);
            Assert.Equal(7, actual.Turn);
            Assert.Equal("branch-a", actual.Branch);
            Assert.Equal("option-b", actual.Option);
            Assert.Equal(statusCode, actual.StatusCode);
            Assert.Equal(attempt, actual.Attempt);
            Assert.Equal(retryAfter, actual.RetryAfter);
            Assert.Equal(exceptionType, actual.ExceptionType);
            Assert.True(actual.Duration >= TimeSpan.Zero);
        }

        private static void AssertOpenAiEvent(
            LlmCallTelemetryEvent actual,
            string eventName,
            int? statusCode,
            int attempt,
            string? exceptionType)
        {
            Assert.Equal(eventName, actual.EventName);
            Assert.Equal("openai-compatible", actual.Provider);
            Assert.Equal("gpt-test", actual.Model);
            Assert.Equal(LlmPhase.OpponentResponse, actual.Phase);
            Assert.Equal("session-456", actual.SessionId);
            Assert.Equal(9, actual.Turn);
            Assert.Equal("branch-b", actual.Branch);
            Assert.Equal("option-c", actual.Option);
            Assert.Equal(statusCode, actual.StatusCode);
            Assert.Equal(attempt, actual.Attempt);
            Assert.Equal(exceptionType, actual.ExceptionType);
            Assert.True(actual.Duration >= TimeSpan.Zero);
        }

        private static HttpResponseMessage AnthropicSuccess(string text)
        {
            var json = JsonConvert.SerializeObject(new MessagesResponse
            {
                Content = new[] { new ResponseContent { Type = "text", Text = text } },
                Usage = new UsageStats { InputTokens = 10, OutputTokens = 5 }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private static string Flatten(IEnumerable<LlmCallTelemetryEvent> events)
        {
            return string.Join("|", events.Select(e =>
                string.Join(",",
                    e.EventName,
                    e.Provider,
                    e.Model,
                    e.Phase,
                    e.SessionId,
                    e.Turn?.ToString(),
                    e.Branch,
                    e.Option,
                    e.StatusCode?.ToString(),
                    e.Attempt.ToString(),
                    e.RetryAfter?.ToString(),
                    e.ExceptionType)));
        }

        private sealed class SequenceHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses;
            private int _callCount;

            public SequenceHandler(params Func<HttpResponseMessage>[] responses)
            {
                _responses = new Queue<Func<HttpResponseMessage>>(responses);
            }

            public int CallCount => _callCount;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref _callCount);
                var response = _responses.Count == 0
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    : _responses.Dequeue()();
                return Task.FromResult(response);
            }
        }
    }
}
