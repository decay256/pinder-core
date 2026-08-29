using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public class Issue1314_ResilientStructuredLlmWrapperTests
    {
        public Issue1314_ResilientStructuredLlmWrapperTests()
        {
            PromptCatalogInitializer.Initialize();
        }
        private sealed class FailureSimulatingTransport : ILlmTransport, IStructuredConversationLlmTransport
        {
            public int Calls { get; private set; }
            public List<string> UserMessages { get; } = new List<string>();
            public List<string?> Phases { get; } = new List<string?>();

            private readonly int _failCount;
            private readonly string _malformedResponse;
            private readonly string _successResponse;
            private readonly bool _throwDirectly;
            private Queue<string>? _successResponses;

            public FailureSimulatingTransport(int failCount, string malformedResponse, string successResponse, bool throwDirectly = false)
            {
                _failCount = failCount;
                _malformedResponse = malformedResponse;
                _successResponse = successResponse;
                _throwDirectly = throwDirectly;
            }

            public bool SupportsStructuredConversationMessages => true;
            public FailureSimulatingTransport(
                int failCount,
                string malformedResponse,
                string firstSuccessResponse,
                string secondSuccessResponse)
                : this(failCount, malformedResponse, secondSuccessResponse)
            {
                _successResponses = new Queue<string>(new[] { firstSuccessResponse, secondSuccessResponse });
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
                => Task.FromResult(NextResponse(userMessage, phase, ct));

            public Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
                => Task.FromResult(new StructuredLlmResponse(
                    NextResponse(request.UserMessage, request.Phase, ct),
                    provider: "test",
                    model: "test-model"));

            public Task<StructuredLlmResponse> SendStructuredConversationAsync(
                StructuredLlmRequest request,
                IReadOnlyList<ConversationMessage> priorMessages,
                CancellationToken cancellationToken = default)
                => SendStructuredAsync(request, cancellationToken);

            private string NextResponse(string userMessage, string? phase, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(phase, LlmPhase.EmotionalDirector, StringComparison.Ordinal))
                {
                    return ValidDirectorJson;
                }

                Calls++;
                UserMessages.Add(userMessage);
                Phases.Add(phase);

                if (Calls <= _failCount)
                {
                    if (_throwDirectly)
                    {
                        throw new LlmContractException(
                            phase: phase ?? "test",
                            reason: "simulated_failure",
                            message: "Simulated contract violation exception from transport",
                            provider: null,
                            model: null,
                            parserName: "MockTransport",
                            expectedOptionCount: null,
                            parsedOptionCount: null,
                            optionCount: null,
                            signalCount: null,
                            sessionId: null,
                            turnId: 1
                        );
                    }
                    return _malformedResponse;
                }
                return                     _successResponses != null && _successResponses.Count > 0
                        ? _successResponses.Dequeue()
                        : _successResponse;
            }
        }

        [Fact]
        public void PinderLlmAdapterOptions_DefaultsToThreeContractViolationRetries()
        {
            var options = new PinderLlmAdapterOptions();

            Assert.Equal(3, options.MaxContractViolationRetries);
            Assert.Equal(100, options.ContractViolationBackoffMs);
        }

        [Fact]
        public void ContractViolationBackoff_UsesExactExponentialSchedule()
        {
            Assert.Equal(0, PinderLlmAdapter.GetContractViolationBackoffDelayMs(0, 1));
            Assert.Equal(100, PinderLlmAdapter.GetContractViolationBackoffDelayMs(100, 1));
            Assert.Equal(200, PinderLlmAdapter.GetContractViolationBackoffDelayMs(100, 2));
            Assert.Equal(400, PinderLlmAdapter.GetContractViolationBackoffDelayMs(100, 3));
            Assert.Equal(int.MaxValue, PinderLlmAdapter.GetContractViolationBackoffDelayMs(int.MaxValue, 2));
        }

        [Fact]
        public async Task GetDialogueOptionsAsync_ThreeTransientViolations_RecoversOnFinalRetry()
        {
            // Arrange
            string malformed = "This is malformed output that doesn't parse";
            string success = @"OPTION 1
[STAT: Charm]
""This is a valid dialogue line A""
OPTION 2
[STAT: Honesty]
""This is a valid dialogue line B""";

            var transport = new FailureSimulatingTransport(3, malformed, success);
            int violationCount = 0;
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                MaxContractViolationRetries = 3,
                ContractViolationBackoffMs = 1, // fast for tests
                OnLlmContractViolation = v => violationCount++
            };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DialogueContext(
                playerAvatarPrompt: "",
                dateePrompt: "",
                conversationHistory: new (string Sender, string Text)[0],
                dateeLastMessage: "",
                activeTraps: new string[0],
                currentInterest: 50,
                playerName: "Pursuer",
                dateeName: "TestChar",
                availableStats: new[] { StatType.Charm, StatType.Honesty }
            );

            // Act
            var result = await adapter.GetDialogueOptionsAsync(context);

            // Assert
            Assert.Equal(4, transport.Calls);
            Assert.Equal(3, violationCount);
            Assert.NotNull(result);
            Assert.Equal(2, result.Length);
            Assert.Equal(StatType.Charm, result[0].Stat);
            Assert.Equal(StatType.Honesty, result[1].Stat);
        }

        [Fact]
        public async Task GetDialogueOptionsAsync_PersistentViolation_BubblesUpAfterMaxAttempts()
        {
            // Arrange
            string malformed = "This is malformed output that doesn't parse";
            string success = @"OPTION 1
[STAT: Charm]
""This is a valid dialogue line A""";

            var transport = new FailureSimulatingTransport(4, malformed, success);
            int violationCount = 0;
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                MaxContractViolationRetries = 3,
                ContractViolationBackoffMs = 1,
                OnLlmContractViolation = v => violationCount++
            };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DialogueContext(
                playerAvatarPrompt: "",
                dateePrompt: "",
                conversationHistory: new (string Sender, string Text)[0],
                dateeLastMessage: "",
                activeTraps: new string[0],
                currentInterest: 50,
                playerName: "Pursuer",
                dateeName: "TestChar",
                availableStats: new[] { StatType.Charm, StatType.Honesty }
            );

            // Act & Assert
            var ex = await Assert.ThrowsAsync<LlmContractException>(() => adapter.GetDialogueOptionsAsync(context));
            Assert.Equal(4, transport.Calls);
            Assert.Equal(4, violationCount);
            Assert.Equal("dialogue_options", ex.Phase);
        }

        [Fact]
        public async Task GetDateeResponseAsync_MalformedSignals_RecoversOnFinalRetry()
        {
            // Arrange
            string malformed = DateeJson("Hello there!", tellStat: "NOT_A_STAT");
            string success = DateeJson("Hello there!", tellStat: "CHARM");

            var transport = new FailureSimulatingTransport(3, malformed, success);
            int violationCount = 0;
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                MaxContractViolationRetries = 3,
                ContractViolationBackoffMs = 1,
                OnLlmContractViolation = v => violationCount++
            };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DateeContext(
                dateePrompt: "",
                conversationHistory: new (string Sender, string Text)[0],
                dateeLastMessage: "",
                activeTraps: new string[0],
                currentInterest: 50,
                playerDeliveredMessage: "delivered line",
                interestBefore: 50,
                interestAfter: 50,
                responseDelayMinutes: 0.0,
                playerName: "Pursuer",
                dateeName: "TestChar",
                emotionalTurnEvent: MakeEvent()
            );

            // Act
            var result = await adapter.GetDateeResponseAsync(context);

            // Assert
            Assert.Equal(4, transport.Calls);
            Assert.Equal(3, violationCount);
            Assert.NotNull(result);
            Assert.Equal("Hello there!", result.MessageText.Trim());
        }

        [Fact]
        public async Task GetDateeResponseAsync_EmptyOutput_RecoversOnRetry()
        {
            // Arrange
            string success = DateeJson("Hello there!", tellStat: "CHARM");

            var transport = new FailureSimulatingTransport(1, "   ", success);
            int violationCount = 0;
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                MaxContractViolationRetries = 3,
                ContractViolationBackoffMs = 1,
                OnLlmContractViolation = v =>
                {
                    Assert.Equal("empty_output", v.Reason);
                    violationCount++;
                }
            };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DateeContext(
                dateePrompt: "",
                conversationHistory: new (string Sender, string Text)[0],
                dateeLastMessage: "",
                activeTraps: new string[0],
                currentInterest: 50,
                playerDeliveredMessage: "delivered line",
                interestBefore: 50,
                interestAfter: 50,
                responseDelayMinutes: 0.0,
                playerName: "Pursuer",
                dateeName: "TestChar",
                emotionalTurnEvent: MakeEvent()
            );

            // Act
            var result = await adapter.GetDateeResponseAsync(context);

            // Assert
            Assert.Equal(2, transport.Calls);
            Assert.Equal(1, violationCount);
            Assert.Equal("Hello there!", result.MessageText.Trim());
        }

        [Fact]
        public async Task GetDateeResponseAsync_PersistentViolation_BubblesUpAfterMaxAttempts()
        {
            // Arrange
            string malformed = DateeJson("Hello there!", tellStat: "NOT_A_STAT");
            string success = DateeJson("Hello there!", tellStat: "CHARM");

            var transport = new FailureSimulatingTransport(4, malformed, success);
            int violationCount = 0;
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                MaxContractViolationRetries = 3,
                ContractViolationBackoffMs = 1,
                OnLlmContractViolation = v => violationCount++
            };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DateeContext(
                dateePrompt: "",
                conversationHistory: new (string Sender, string Text)[0],
                dateeLastMessage: "",
                activeTraps: new string[0],
                currentInterest: 50,
                playerDeliveredMessage: "delivered line",
                interestBefore: 50,
                interestAfter: 50,
                responseDelayMinutes: 0.0,
                playerName: "Pursuer",
                dateeName: "TestChar",
                emotionalTurnEvent: MakeEvent()
            );

            // Act & Assert
            var ex = await Assert.ThrowsAsync<LlmContractException>(() => adapter.GetDateeResponseAsync(context));
            Assert.Equal(4, transport.Calls);
            Assert.Equal(4, violationCount);
            Assert.Equal("datee_response", ex.Phase);
        }

        [Fact]
        public async Task GetDialogueOptionsAsync_ThrowsLlmContractExceptionDirectly_RecoversOnFinalRetry()
        {
            // Arrange
            string success = @"OPTION 1
[STAT: Charm]
""This is a valid dialogue line A""
OPTION 2
[STAT: Honesty]
""This is a valid dialogue line B""";

            var transport = new FailureSimulatingTransport(3, "", success, throwDirectly: true);
            int violationCount = 0;
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                MaxContractViolationRetries = 3,
                ContractViolationBackoffMs = 1,
                OnLlmContractViolation = v => violationCount++
            };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DialogueContext(
                playerAvatarPrompt: "",
                dateePrompt: "",
                conversationHistory: new (string Sender, string Text)[0],
                dateeLastMessage: "",
                activeTraps: new string[0],
                currentInterest: 50,
                playerName: "Pursuer",
                dateeName: "TestChar",
                availableStats: new[] { StatType.Charm, StatType.Honesty }
            );

            // Act
            var result = await adapter.GetDialogueOptionsAsync(context);

            // Assert
            Assert.Equal(4, transport.Calls);
            Assert.Equal(3, violationCount);
            Assert.NotNull(result);
            Assert.Equal(2, result.Length);
        }

        [Fact]
        public async Task GetDateeResponseAsync_StatefulRetries_DoNotMutateSuppliedHistoryAndReusePrompt()
        {
            // Arrange
            string malformed = DateeJson("Hello there!", tellStat: "NOT_A_STAT");
            string success = DateeJson("Hello there!", tellStat: "CHARM");

            var transport = new FailureSimulatingTransport(3, malformed, success);
            int violationCount = 0;
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                MaxContractViolationRetries = 3,
                ContractViolationBackoffMs = 1,
                OnLlmContractViolation = v => violationCount++
            };
            var adapter = new PinderLlmAdapter(transport, options);

            var history = new List<ConversationMessage>
            {
                ConversationMessage.User("old user line"),
                ConversationMessage.Assistant("old assistant line"),
            };

            var context = new DateeContext(
                dateePrompt: "",
                conversationHistory: new (string Sender, string Text)[0],
                dateeLastMessage: "",
                activeTraps: new string[0],
                currentInterest: 50,
                playerDeliveredMessage: "new delivered line",
                interestBefore: 50,
                interestAfter: 50,
                responseDelayMinutes: 0.0,
                playerName: "Pursuer",
                dateeName: "TestChar",
                emotionalTurnEvent: MakeEvent()
            );

            // Act
            var result = await adapter.GetDateeResponseAsync(context, history);

            // Assert
            Assert.Equal(4, transport.Calls);
            Assert.Equal(3, violationCount);
            Assert.Equal(2, history.Count);
            Assert.Equal(2, result.NewHistoryEntries.Count);
            Assert.Equal("new delivered line", result.NewHistoryEntries[0].Content);
            Assert.DoesNotContain("[PREVIOUS CONVERSATION CONTEXT]", transport.UserMessages[0]);
            Assert.DoesNotContain("old user line", transport.UserMessages[0]);
            Assert.DoesNotContain("old assistant line", transport.UserMessages[0]);
            Assert.Equal(transport.UserMessages[0], transport.UserMessages[1]);
            Assert.Equal(transport.UserMessages[0], transport.UserMessages[2]);
            Assert.Equal(transport.UserMessages[0], transport.UserMessages[3]);
        }

        [Fact]
        public async Task GetDateeResponseAsync_ConsecutiveTurns_DoNotNestPriorPromptDocuments()
        {
            string response = DateeJson("Datee reply", tellStat: "CHARM");
            string repair = DateeJson("Different datee reply", tellStat: "CHARM");
            var transport = new FailureSimulatingTransport(0, response, response, repair);
            var adapter = new PinderLlmAdapter(transport, new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
            });
            var statefulHistory = new List<ConversationMessage>();

            var firstContext = CreateDateeContext(
                Array.Empty<(string Sender, string Text)>(),
                "first delivered line",
                currentTurn: 1);
            var firstResult = await adapter.GetDateeResponseAsync(firstContext, statefulHistory);
            statefulHistory.AddRange(firstResult.NewHistoryEntries);

            var secondContext = CreateDateeContext(
                new[]
                {
                    ("Pursuer", "first delivered line"),
                    ("TestChar", "Datee reply"),
                },
                "second delivered line",
                currentTurn: 2);
            await adapter.GetDateeResponseAsync(secondContext, statefulHistory);

            string secondPrompt = transport.UserMessages[1];
            Assert.Equal(1, CountOccurrences(secondPrompt, "[CURRENT_TURN]"));
            Assert.Equal(2, CountOccurrences(secondPrompt, "<ENGINE_STATE>"));
            Assert.Equal(2, CountOccurrences(secondPrompt, "</ENGINE_STATE>"));
            Assert.DoesNotContain("[PREVIOUS CONVERSATION CONTEXT]", secondPrompt);
            Assert.Contains("first delivered line", secondPrompt);
            Assert.Contains("Datee reply", secondPrompt);
            Assert.Contains("second delivered line", secondPrompt);
            Assert.DoesNotContain(transport.UserMessages[0], secondPrompt);
        }

        private static DateeContext CreateDateeContext(
            IReadOnlyList<(string Sender, string Text)> conversationHistory,
            string deliveredMessage,
            int currentTurn)
        {
            return new DateeContext(
                dateePrompt: "",
                conversationHistory: conversationHistory,
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 50,
                playerDeliveredMessage: deliveredMessage,
                interestBefore: 50,
                interestAfter: 50,
                responseDelayMinutes: 0,
                playerName: "Pursuer",
                dateeName: "TestChar",
                currentTurn: currentTurn,
                emotionalTurnEvent: MakeEvent());
        }

        private static DateeEmotionalTurnEvent MakeEvent()
        {
            return new DateeEmotionalTurnEvent(
                StatType.Honesty,
                RollOutcomeIntensity.Strong,
                TestHelpers.MakePsychiatricDiagnosis());
        }

        private static string DateeJson(string message, string? tellStat = null)
        {
            string tell = tellStat == null
                ? "null"
                : "{\"stat\":\"" + tellStat + "\",\"description\":\"She liked your charm\"}";
            return "{\"schema_version\":\"datee_performance.v1\","
                + "\"message\":" + System.Text.Json.JsonSerializer.Serialize(message) + ","
                + "\"signals\":{\"tell\":" + tell + ",\"weakness\":null}}";
        }

        private const string ValidDirectorJson =
            "{\"schema_version\":\"emotional_director.v2\",\"primary_emotion\":\"relief\",\"secondary_emotion\":\"none\",\"regulatory_state\":\"controlled\",\"activation\":4,\"trajectory\":\"escalating\",\"core_threat_or_desire\":\"fear of being dismissed\",\"interpretation\":\"reads the message as specific warmth that is probably meant for them\",\"impulse\":\"leans in with a careful question\",\"restraint\":\"keeps the reply tentative but available\",\"response_posture\":\"turns warmer while still checking sincerity\"}";

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }
    }
}
