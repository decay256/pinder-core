using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Diagnostics.AgentJournals;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters.AgentJournals;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class DateePerformanceStructuredAdapterTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DateePerformance_UsesStructuredContractAndKeepsSignalsOutOfHistory(bool native)
        {
            var transport = new RecordingStructuredTransport(native, ValidDateeJson(tell: true, weakness: true));
            var adapter = CreateAdapter(transport);

            DateeResponse response = await adapter.GetDateeResponseAsync(MakeContext());

            Assert.Equal(2, transport.StructuredCalls);
            Assert.Equal(DateePerformanceStructuredContract.SchemaName, transport.LastDateeRequest!.SchemaName);
            Assert.Equal(DateePerformanceStructuredContract.SchemaVersion, transport.LastDateeRequest.SchemaVersion);
            Assert.Contains("\"signals\"", transport.LastDateeRequest.JsonSchema, StringComparison.Ordinal);
            Assert.Equal("I did notice that, actually.", response.MessageText);
            Assert.Equal(StatType.Honesty, response.DetectedTell!.Stat);
            Assert.Equal(StatType.SelfAwareness, response.WeaknessWindow!.DefendingStat);
            Assert.DoesNotContain("[SIGNALS]", response.MessageText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TELL:", response.MessageText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WEAKNESS:", response.MessageText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task DateePerformance_StatefulHistoryUsesStructuredConversationWithoutEmbeddingHistory()
        {
            var transport = new RecordingStructuredTransport(true, ValidDateeJson(tell: false, weakness: false));
            var adapter = CreateAdapter(transport);
            var dateeHistory = new[]
            {
                ConversationMessage.User("older typed player line"),
                ConversationMessage.Assistant("older typed datee line"),
            };

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeContext(conversationHistory: new List<(string, string)> { ("P", "embedded history should be absent") }),
                dateeHistory,
                Array.Empty<ConversationMessage>(),
                null,
                null);

            Assert.True(transport.ConversationStructuredCalls > 0);
            Assert.NotNull(transport.LastDateePriorMessages);
            Assert.Contains(transport.LastDateePriorMessages!, m => m.Content == "older typed player line");
            Assert.DoesNotContain("embedded history should be absent", transport.LastDateeRequest!.UserMessage, StringComparison.Ordinal);
            Assert.Equal("I did notice that, actually.", result.Response.MessageText);
            Assert.Collection(
                result.NewHistoryEntries,
                first => Assert.Equal("hello", first.Content),
                second => Assert.Equal("I did notice that, actually.", second.Content));
        }

        [Fact]
        public async Task DateePerformance_InvalidAttemptRetriesWithoutReportingValidationTwice()
        {
            var validations = new List<StructuredLlmValidationResult>();
            var transport = new RecordingStructuredTransport(
                true,
                @"{""schema_version"":""datee_performance.v1"",""message"":""bad [SIGNALS]"",""signals"":{""tell"":null,""weakness"":null}}",
                ValidDateeJson(tell: true, weakness: false))
            {
                ValidationObserver = validations.Add,
            };
            int violations = 0;
            var adapter = CreateAdapter(transport, retries: 1, onViolation: _ => violations++);

            DateeResponse response = await adapter.GetDateeResponseAsync(MakeContext());

            Assert.Equal("I did notice that, actually.", response.MessageText);
            Assert.Equal(3, transport.StructuredCalls);
            Assert.Equal(1, violations);
            Assert.Collection(
                validations,
                first =>
                {
                    Assert.Equal("rejected", first.Outcome);
                    Assert.Equal("legacy_signal_marker", first.RejectionReason);
                },
                second =>
                {
                    Assert.Equal("accepted", second.Outcome);
                    Assert.Null(second.RejectionReason);
                });
        }

        [Fact]
        public async Task DateePerformance_ExhaustedRecoveryLeavesJournalRejectedAndNoMessageLink()
        {
            var records = new List<AgentJournalSinkRecord>();
            var transport = new RecordingStructuredTransport(
                true,
                @"{""schema_version"":""datee_performance.v1"",""message"":""bad [SIGNALS]"",""signals"":{""tell"":null,""weakness"":null}}",
                @"{""schema_version"":""datee_performance.v1"",""message"":"""",""signals"":{""tell"":null,""weakness"":null}}");
            var adapter = CreateAdapter(transport, retries: 1, sink: new RecordingJournalSink(records));

            LlmContractException ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GetDateeResponseAsync(MakeContext(journal: true)));

            Assert.Equal("invalid_message", ex.Reason);
            Assert.DoesNotContain(records, r => r.CustomType == AgentJournalSchemaNames.MessageLinkV1);
            List<AgentJournalSinkRecord> performanceResults = records.FindAll(r =>
                r.CustomType == AgentJournalSchemaNames.LlmResultV1
                && r.Record is LlmResultRecord result
                && result.Correlation.OperationId == GameRunConversationJournalInventory.DateePerformance);
            Assert.Equal(2, performanceResults.Count);
            Assert.All(
                performanceResults,
                record =>
                {
                    var result = Assert.IsType<LlmResultRecord>(record.Record);
                    Assert.Equal(AgentJournalTerminalStatus.Rejected, result.TerminalStatus);
                    Assert.Null(result.OutputText);
                    Assert.Equal(DateePerformanceStructuredContract.SchemaName, result.ResultMetadata!["schema_name"]);
                    Assert.Equal("rejected", result.ResultMetadata["validation_outcome"]);
                });
        }

        [Fact]
        public async Task DateePerformance_AcceptedJournalSeparatesVisibleMessageFromEngineSignals()
        {
            var records = new List<AgentJournalSinkRecord>();
            var transport = new RecordingStructuredTransport(true, ValidDateeJson(tell: true, weakness: true));
            var adapter = CreateAdapter(transport, sink: new RecordingJournalSink(records));

            DateeResponse response = await adapter.GetDateeResponseAsync(MakeContext(journal: true));

            Assert.Equal("I did notice that, actually.", response.MessageText);
            LlmResultRecord result = Assert.IsType<LlmResultRecord>(
                records.FindLast(r => r.CustomType == AgentJournalSchemaNames.LlmResultV1)!.Record);
            Assert.Equal("I did notice that, actually.", result.OutputText);
            Assert.Equal("accepted", result.ResultMetadata!["validation_outcome"]);
            Assert.Equal("True", result.ResultMetadata["tell_present"]);
            Assert.Equal("HONESTY", result.ResultMetadata["engine_signal_tell_stat"]);
            Assert.Equal("SELF_AWARENESS", result.ResultMetadata["engine_signal_weakness_defending_stat"]);
            Assert.DoesNotContain("asks directly", result.OutputText!, StringComparison.Ordinal);
        }

        private static PinderLlmAdapter CreateAdapter(
            RecordingStructuredTransport transport,
            int retries = 0,
            Action<LlmContractViolation>? onViolation = null,
            IAgentJournalSink? sink = null)
        {
            return new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    MaxContractViolationRetries = retries,
                    ContractViolationBackoffMs = 1,
                    OnLlmContractViolation = onViolation,
                    AgentJournalHostSink = sink,
                });
        }

        private static DateeContext MakeContext(
            List<(string, string)>? conversationHistory = null,
            bool journal = false)
        {
            var context = new DateeContext(
                dateePrompt: "datee system prompt",
                conversationHistory: conversationHistory ?? new List<(string, string)> { ("P", "hey"), ("O", "hi") },
                dateeLastMessage: "hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 15,
                playerDeliveredMessage: "hello",
                interestBefore: 14,
                interestAfter: 15,
                responseDelayMinutes: 2.0,
                playerName: "Velvet",
                dateeName: "Sable",
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    TestHelpers.MakePsychiatricDiagnosis()),
                agentJournalContext: journal
                    ? new GameRunAgentJournalContext(
                        "run-1426",
                        "datee-session",
                        requestId: "request-1426",
                        branchId: "main")
                    : null);
            return context;
        }

        private static string ValidDateeJson(bool tell, bool weakness)
        {
            string tellJson = tell
                ? @"{ ""stat"": ""HONESTY"", ""description"": ""asks directly whether the warmth is real"" }"
                : "null";
            string weaknessJson = weakness
                ? @"{ ""defending_stat"": ""SELF_AWARENESS"", ""dc_reduction"": 2, ""description"": ""lets the guard drop for a second"" }"
                : "null";
            return @"{
  ""schema_version"": ""datee_performance.v1"",
  ""message"": ""I did notice that, actually."",
  ""signals"": {
    ""tell"": " + tellJson + @",
    ""weakness"": " + weaknessJson + @"
  }
}";
        }

        private const string ValidDirectorJson =
            "{\"schema_version\":\"emotional_director.v1\",\"primary_emotion\":\"relief\",\"intensity\":\"moderate and steadily rising\",\"underlying_feeling\":\"fear of being dismissed\",\"interpretation\":\"reads the message as specific warmth that is probably meant for them\",\"impulse\":\"leans in with a careful question\",\"restraint\":\"keeps the reply tentative but available\",\"response_posture\":\"Writing from relief, turns warmer while still checking sincerity\"}";

        private sealed class RecordingStructuredTransport : ILlmTransport, IStructuredLlmTransport, IStructuredConversationLlmTransport
        {
            private readonly Queue<string> _dateeResponses = new Queue<string>();
            private readonly bool _native;

            public RecordingStructuredTransport(bool native, params string[] dateeResponses)
            {
                _native = native;
                foreach (string response in dateeResponses)
                {
                    _dateeResponses.Enqueue(response);
                }
            }

            public Action<StructuredLlmValidationResult>? ValidationObserver { get; set; }
            public int StructuredCalls { get; private set; }
            public int ConversationStructuredCalls { get; private set; }
            public bool SupportsStructuredConversationMessages => true;
            public StructuredLlmRequest? LastDateeRequest { get; private set; }
            public IReadOnlyList<ConversationMessage>? LastDateePriorMessages { get; private set; }

            public Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int? maxTokens = null, string? phase = null, CancellationToken ct = default)
                => throw new InvalidOperationException("DATEE performance must not use plain SendAsync.");

            public Task<StructuredLlmResponse> SendStructuredAsync(StructuredLlmRequest request, CancellationToken ct = default)
            {
                StructuredCalls++;
                return Task.FromResult(CreateResponse(request));
            }

            public Task<StructuredLlmResponse> SendStructuredConversationAsync(
                StructuredLlmRequest request,
                IReadOnlyList<ConversationMessage> priorMessages,
                CancellationToken cancellationToken = default)
            {
                StructuredCalls++;
                ConversationStructuredCalls++;
                if (request.SchemaName == DateePerformanceStructuredContract.SchemaName)
                {
                    LastDateePriorMessages = priorMessages;
                }
                return Task.FromResult(CreateResponse(request));
            }

            private StructuredLlmResponse CreateResponse(StructuredLlmRequest request)
            {
                if (request.SchemaName == "emotional_director")
                {
                    return Response(ValidDirectorJson, observeValidation: false);
                }
                Assert.Equal(DateePerformanceStructuredContract.SchemaName, request.SchemaName);
                LastDateeRequest = request;
                return Response(_dateeResponses.Dequeue(), observeValidation: true);
            }

            private StructuredLlmResponse Response(string json, bool observeValidation)
                => new StructuredLlmResponse(
                    json,
                    provider: "test",
                    model: "model",
                    usedNativeStructuredOutput: _native,
                    validationMode: _native ? "native_schema" : "local_validation",
                    validationObserver: observeValidation ? ValidationObserver : null);
        }

        private sealed class RecordingJournalSink : IAgentJournalSink
        {
            private readonly List<AgentJournalSinkRecord> _records;

            public RecordingJournalSink(List<AgentJournalSinkRecord> records)
            {
                _records = records;
            }

            public Task PersistAsync(AgentJournalSinkRecord record, CancellationToken cancellationToken)
            {
                _records.Add(record);
                return Task.CompletedTask;
            }
        }
    }
}
