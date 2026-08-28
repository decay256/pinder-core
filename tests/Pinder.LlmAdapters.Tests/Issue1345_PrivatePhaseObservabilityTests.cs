using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class Issue1345_PrivatePhaseObservabilityTests
    {
        private const string PrivateDiagnosis = "PRIVATE-DIAGNOSIS-DO-NOT-LOG-1345";
        private const string PrivateDirectorValue = "secret tender marker should stay private 1345";
        private const string PrivateRejectedDirector = "PRIVATE-REJECTED-DIRECTOR-DO-NOT-LOG-1345";
        private const string PrivateRejectedPerformance = "PRIVATE-REJECTED-PERFORMANCE-DO-NOT-LOG-1345";

        static Issue1345_PrivatePhaseObservabilityTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        public async Task DateePrivatePhaseDiagnosticsExposeOperationalFactsWithoutPrivateProse()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var transport = new StructuredUsageTransport(
                structuredResponses: new[]
                {
                    new StructuredLlmResponse(
                        "{\"schema_version\":\"wrong\",\"primary_emotion\":\"" + PrivateRejectedDirector + "\"}",
                        provider: "unit-provider",
                        model: "unit-director-model",
                        usedNativeStructuredOutput: true),
                    new StructuredLlmResponse(
                        ValidDirectionJson(underlyingFeeling: PrivateDirectorValue),
                        provider: "unit-provider",
                        model: "unit-director-model",
                        usedNativeStructuredOutput: true),
                },
                plainResponses: new[]
                {
                    "DATEE EMOTIONAL PERFORMANCE DIRECTION " + PrivateRejectedPerformance,
                    "Visible accepted DATEE reply.",
                });
            var adapter = CreateAdapter(transport, diagnostics.Add, retries: 1);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeContext(),
                Array.Empty<ConversationMessage>());

            Assert.Equal("Visible accepted DATEE reply.", result.Response.MessageText);
            Assert.Equal("Visible accepted DATEE reply.", result.NewHistoryEntries[1].Content);

            string flattened = FlattenDiagnostics(diagnostics);
            Assert.DoesNotContain(PrivateDiagnosis, flattened, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateDirectorValue, flattened, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateRejectedDirector, flattened, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateRejectedPerformance, flattened, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateDiagnosis, FlattenHistory(result.NewHistoryEntries), StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateDirectorValue, FlattenHistory(result.NewHistoryEntries), StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateRejectedPerformance, FlattenHistory(result.NewHistoryEntries), StringComparison.Ordinal);

            var rejected = diagnostics
                .Where(diagnostic => diagnostic.EventName == "LlmContractRejected")
                .ToArray();
            Assert.Equal(2, rejected.Length);
            Assert.Contains(rejected, diagnostic =>
                diagnostic.CorrelationHints["datee_private_phase"] == "director"
                && diagnostic.CorrelationHints["attempt"] == "1"
                && diagnostic.CorrelationHints["total_attempts"] == "2"
                && diagnostic.CorrelationHints["will_retry"] == "true"
                && diagnostic.CorrelationHints["reason"] == "invalid_schema_version");
            Assert.Contains(rejected, diagnostic =>
                diagnostic.CorrelationHints["datee_private_phase"] == "performance"
                && diagnostic.CorrelationHints["attempt"] == "1"
                && diagnostic.CorrelationHints["total_attempts"] == "2"
                && diagnostic.CorrelationHints["will_retry"] == "true"
                && diagnostic.CorrelationHints["reason"] == "private_direction_leak");

            var directorTerminal = diagnostics.Last(diagnostic =>
                diagnostic.EventName == "LlmTransportSucceeded"
                && diagnostic.PhaseCode == LlmPhase.EmotionalDirector);
            Assert.Equal("datee_emotional_director", directorTerminal.OperationKind);
            Assert.Equal("director", directorTerminal.CorrelationHints["datee_private_phase"]);
            Assert.Equal("2", directorTerminal.CorrelationHints["attempt"]);
            Assert.Equal("2", directorTerminal.CorrelationHints["total_attempts"]);
            Assert.Equal("emotional-reaction-director", directorTerminal.CorrelationHints["prompt_key"]);
            Assert.Contains("data/prompts/emotional-reactions.yaml", directorTerminal.CorrelationHints["system_prompt_source"], StringComparison.Ordinal);
            Assert.Contains("character:psychiatric_diagnosis", directorTerminal.CorrelationHints["compiled_input_sources"], StringComparison.Ordinal);
            Assert.Contains(TherapistDiagnosisContract.DerivedFeelingKey, directorTerminal.CorrelationHints["compiled_input_keys"], StringComparison.Ordinal);
            Assert.True(directorTerminal.CorrelationHints["compiled_input_keys"].Length > 256);
            AssertSafePromptTraceHints(directorTerminal);
            Assert.Equal("emotional_director", directorTerminal.CorrelationHints["schema_name"]);
            Assert.Equal(CharacterEmotionalDirectionContract.SchemaVersion, directorTerminal.CorrelationHints["schema_version"]);
            Assert.Equal("unit-provider", directorTerminal.CorrelationHints["provider"]);
            Assert.Equal("unit-director-model", directorTerminal.CorrelationHints["model"]);
            AssertNonNegativeIntHint(directorTerminal, "elapsed_ms");
            Assert.Equal("ITokenUsageProvider.session_delta", directorTerminal.CorrelationHints["token_source"]);
            Assert.Equal("13", directorTerminal.CorrelationHints["input_tokens"]);
            Assert.Equal("7", directorTerminal.CorrelationHints["output_tokens"]);
            Assert.Equal("3", directorTerminal.CorrelationHints["cache_read_input_tokens"]);
            Assert.Equal("2", directorTerminal.CorrelationHints["cache_creation_input_tokens"]);
            Assert.Equal("1", directorTerminal.CorrelationHints["call_count_delta"]);

            var performanceTerminal = diagnostics.Last(diagnostic =>
                diagnostic.EventName == "LlmTransportSucceeded"
                && diagnostic.PhaseCode == LlmPhase.OpponentResponse);
            Assert.Equal(OperationalDiagnosticOperationKind.DateeResponse, performanceTerminal.OperationKind);
            Assert.Equal("performance", performanceTerminal.CorrelationHints["datee_private_phase"]);
            Assert.Equal("2", performanceTerminal.CorrelationHints["attempt"]);
            Assert.Equal("2", performanceTerminal.CorrelationHints["total_attempts"]);
            Assert.Equal("datee", performanceTerminal.CorrelationHints["prompt_trace_type"]);
            Assert.Contains("runtime:datee-response-plan", performanceTerminal.CorrelationHints["prompt_trace_sources"], StringComparison.Ordinal);
            Assert.Contains("conversation-history", performanceTerminal.CorrelationHints["prompt_trace_sources"], StringComparison.Ordinal);
            Assert.Contains(DateeResponsePlan.CurrentSchemaVersion, performanceTerminal.CorrelationHints["prompt_trace_keys"], StringComparison.Ordinal);
            AssertSafePromptTraceHints(performanceTerminal);
            AssertNonNegativeIntHint(performanceTerminal, "elapsed_ms");
            Assert.Equal("ITokenUsageProvider.session_delta", performanceTerminal.CorrelationHints["token_source"]);
            Assert.Equal("23", performanceTerminal.CorrelationHints["input_tokens"]);
            Assert.Equal("11", performanceTerminal.CorrelationHints["output_tokens"]);
            Assert.Equal("5", performanceTerminal.CorrelationHints["cache_read_input_tokens"]);
            Assert.Equal("4", performanceTerminal.CorrelationHints["cache_creation_input_tokens"]);
            Assert.Equal("1", performanceTerminal.CorrelationHints["call_count_delta"]);
        }

        [Fact]
        public async Task PrivatePhaseTransportFailureDiagnosticKeepsExceptionMessageOutOfOperationalEvent()
        {
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var transport = new FailingPerformanceTransport(
                ValidDirectionJson(),
                new InvalidOperationException("provider included " + PrivateRejectedPerformance));
            var adapter = CreateAdapter(transport, diagnostics.Add, retries: 0);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => adapter.GetDateeResponseAsync(MakeContext(), Array.Empty<ConversationMessage>()));

            Assert.Contains(PrivateRejectedPerformance, error.Message, StringComparison.Ordinal);
            var failure = Assert.Single(diagnostics.Where(diagnostic =>
                diagnostic.EventName == "LlmTransportFailed"
                && diagnostic.PhaseCode == LlmPhase.OpponentResponse));
            Assert.Null(failure.Exception);
            Assert.Equal("InvalidOperationException", failure.CorrelationHints["exception_type"]);
            Assert.Equal("performance", failure.CorrelationHints["datee_private_phase"]);
            Assert.DoesNotContain(PrivateRejectedPerformance, FlattenDiagnostics(new[] { failure }), StringComparison.Ordinal);
        }

        private static void AssertSafePromptTraceHints(OperationalDiagnosticEvent diagnostic)
        {
            foreach (var hint in diagnostic.CorrelationHints
                .Where(pair => PromptTraceDiagnosticContract.IsMetadataKey(pair.Key)))
            {
                Assert.True(
                    PromptTraceDiagnosticContract.IsSafe(hint.Key, hint.Value),
                    $"Unsafe prompt trace hint {hint.Key}={hint.Value}");
            }
        }

        private static PinderLlmAdapter CreateAdapter(
            ILlmTransport transport,
            Action<OperationalDiagnosticEvent> onDiagnostic,
            int retries)
        {
            return new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    PromptCatalog = BuiltInCatalog(),
                    MaxContractViolationRetries = retries,
                    ContractViolationBackoffMs = 1,
                    OnDiagnostic = onDiagnostic,
                });
        }

        private static DateeContext MakeContext()
        {
            var diagnosis = TestHelpers.MakePsychiatricDiagnosis()
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            diagnosis[TherapistDiagnosisContract.DerivedFeelingKey] = PrivateDiagnosis;

            return new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: new[]
                {
                    ("Player", "older visible player line"),
                    ("Datee", "older visible datee line"),
                },
                dateeLastMessage: "older visible datee line",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "visible delivered line",
                interestBefore: 8,
                interestAfter: 12,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 7,
                interestBeforeState: InterestState.Lukewarm,
                interestAfterState: InterestState.Interested,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    diagnosis));
        }

        private static string ValidDirectionJson(
            string? primaryEmotion = null,
            string? underlyingFeeling = null)
        {
            return new JObject
            {
                ["schema_version"] = CharacterEmotionalDirectionContract.SchemaVersion,
                ["primary_emotion"] = primaryEmotion ?? "relief",
                ["secondary_emotion"] = CharacterEmotionalDirection.NoneSecondaryEmotion,
                ["regulatory_state"] = "controlled",
                ["activation"] = 4,
                ["trajectory"] = "escalating",
                ["core_threat_or_desire"] = underlyingFeeling ?? "fear of being dismissed",
                ["interpretation"] = "reads the message as specific warmth that is probably meant for them",
                ["impulse"] = "leans in with a careful question",
                ["restraint"] = "keeps the reply tentative but available",
                ["response_posture"] = "Writing from " + (primaryEmotion ?? "relief") + ", turns warmer while still checking sincerity",
            }.ToString(Formatting.None);
        }

        private static PromptCatalog BuiltInCatalog()
        {
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            catalog.ValidateRuntimeCatalog();
            return catalog;
        }

        private static string FindPromptsRoot()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "data", "prompts");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }

        private static void AssertNonNegativeIntHint(
            OperationalDiagnosticEvent diagnostic,
            string key)
        {
            Assert.True(diagnostic.CorrelationHints.ContainsKey(key), "missing hint " + key);
            Assert.True(
                int.TryParse(
                    diagnostic.CorrelationHints[key],
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int value),
                "hint " + key + " was not an invariant integer");
            Assert.True(value >= 0, "hint " + key + " was negative");
        }

        private static string FlattenDiagnostics(IEnumerable<OperationalDiagnosticEvent> diagnostics)
        {
            return string.Join(
                "|",
                diagnostics.Select(diagnostic =>
                    string.Join(
                        ",",
                        diagnostic.Source,
                        diagnostic.EventName,
                        diagnostic.Message,
                        diagnostic.OperationKind,
                        diagnostic.PhaseCode,
                        diagnostic.Outcome.ToString(),
                        diagnostic.FailureClassification.ToString(),
                        diagnostic.Exception?.ToString() ?? string.Empty,
                        string.Join(";", diagnostic.CorrelationHints.Select(pair => pair.Key + "=" + pair.Value)))));
        }

        private static string FlattenHistory(IReadOnlyList<ConversationMessage> history)
        {
            return string.Join("|", history.Select(message => message.Role + ":" + message.Content));
        }

        private sealed class StructuredUsageTransport : ILlmTransport, IStructuredLlmTransport, ITokenUsageProvider
        {
            private readonly Queue<StructuredLlmResponse> _structuredResponses;
            private readonly Queue<string> _plainResponses;
            private int _inputTokens;
            private int _outputTokens;
            private int _cacheReadInputTokens;
            private int _cacheCreationInputTokens;
            private int _callCount;

            public StructuredUsageTransport(
                IEnumerable<StructuredLlmResponse> structuredResponses,
                IEnumerable<string> plainResponses)
            {
                _structuredResponses = new Queue<StructuredLlmResponse>(structuredResponses);
                _plainResponses = new Queue<string>(plainResponses);
            }

            public Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (request.SchemaName == DateePerformanceStructuredContract.SchemaName)
                {
                    AddUsage(23, 11, 5, 4);
                    return Task.FromResult(DateePromptTestBuilder.StructuredResponse(
                        request,
                        _plainResponses.Dequeue(),
                        "unit-performance-model"));
                }
                AddUsage(13, 7, 3, 2);
                return Task.FromResult(_structuredResponses.Dequeue());
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                AddUsage(23, 11, 5, 4);
                return Task.FromResult(_plainResponses.Dequeue());
            }

            public SessionTokenUsage GetSessionUsage()
            {
                return new SessionTokenUsage
                {
                    InputTokens = _inputTokens,
                    OutputTokens = _outputTokens,
                    CacheReadInputTokens = _cacheReadInputTokens,
                    CacheCreationInputTokens = _cacheCreationInputTokens,
                    CallCount = _callCount,
                };
            }

            private void AddUsage(int input, int output, int cacheRead, int cacheCreation)
            {
                _inputTokens += input;
                _outputTokens += output;
                _cacheReadInputTokens += cacheRead;
                _cacheCreationInputTokens += cacheCreation;
                _callCount++;
            }
        }

        private sealed class FailingPerformanceTransport : ILlmTransport, IStructuredLlmTransport, ITokenUsageProvider
        {
            private readonly StructuredLlmResponse _directorResponse;
            private readonly Exception _performanceException;
            private int _inputTokens;
            private int _outputTokens;
            private int _callCount;

            public FailingPerformanceTransport(string directorJson, Exception performanceException)
            {
                _directorResponse = new StructuredLlmResponse(
                    directorJson,
                    provider: "unit-provider",
                    model: "unit-director-model",
                    usedNativeStructuredOutput: true);
                _performanceException = performanceException;
            }

            public Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                _callCount++;
                if (request.SchemaName == DateePerformanceStructuredContract.SchemaName)
                    throw _performanceException;
                _inputTokens += 13;
                _outputTokens += 7;
                return Task.FromResult(_directorResponse);
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                _callCount++;
                throw _performanceException;
            }

            public SessionTokenUsage GetSessionUsage()
            {
                return new SessionTokenUsage
                {
                    InputTokens = _inputTokens,
                    OutputTokens = _outputTokens,
                    CallCount = _callCount,
                };
            }
        }
    }
}
