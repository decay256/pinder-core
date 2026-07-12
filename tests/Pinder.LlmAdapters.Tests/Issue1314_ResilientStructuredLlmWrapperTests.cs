using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public class Issue1314_ResilientStructuredLlmWrapperTests
    {
        private sealed class FailureSimulatingTransport : ILlmTransport
        {
            public int Calls { get; private set; }
            public List<string> UserMessages { get; } = new List<string>();
            public List<string?> Phases { get; } = new List<string?>();

            private readonly int _failCount;
            private readonly string _malformedResponse;
            private readonly string _successResponse;
            private readonly bool _throwDirectly;

            public FailureSimulatingTransport(int failCount, string malformedResponse, string successResponse, bool throwDirectly = false)
            {
                _failCount = failCount;
                _malformedResponse = malformedResponse;
                _successResponse = successResponse;
                _throwDirectly = throwDirectly;
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
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
                    return Task.FromResult(_malformedResponse);
                }
                return Task.FromResult(_successResponse);
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
            string malformed = "Hello there!\n[SIGNALS]\nTELL: Charm";
            string success = @"Hello there!
[SIGNALS]
TELL: Charm (She liked your charm)";

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
                playerDeliveredMessage: "",
                interestBefore: 50,
                interestAfter: 50,
                responseDelayMinutes: 0.0,
                playerName: "Pursuer",
                dateeName: "TestChar"
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
            string success = @"Hello there!
[SIGNALS]
TELL: Charm (She liked your charm)";

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
                playerDeliveredMessage: "",
                interestBefore: 50,
                interestAfter: 50,
                responseDelayMinutes: 0.0,
                playerName: "Pursuer",
                dateeName: "TestChar"
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
            string malformed = "Hello there!\n[SIGNALS]\nTELL: Charm";
            string success = @"Hello there!
[SIGNALS]
TELL: Charm (She liked your charm)";

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
                playerDeliveredMessage: "",
                interestBefore: 50,
                interestAfter: 50,
                responseDelayMinutes: 0.0,
                playerName: "Pursuer",
                dateeName: "TestChar"
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
            string malformed = "Hello there!\n[SIGNALS]\nTELL: Charm";
            string success = @"Hello there!
[SIGNALS]
TELL: Charm (She liked your charm)";

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
                dateeName: "TestChar"
            );

            // Act
            var result = await adapter.GetDateeResponseAsync(context, history);

            // Assert
            Assert.Equal(4, transport.Calls);
            Assert.Equal(3, violationCount);
            Assert.Equal(2, history.Count);
            Assert.Equal(2, result.NewHistoryEntries.Count);
            Assert.Contains("[PREVIOUS CONVERSATION CONTEXT]", transport.UserMessages[0]);
            Assert.Contains("old user line", transport.UserMessages[0]);
            Assert.Contains("old assistant line", transport.UserMessages[0]);
            Assert.Equal(transport.UserMessages[0], transport.UserMessages[1]);
            Assert.Equal(transport.UserMessages[0], transport.UserMessages[2]);
            Assert.Equal(transport.UserMessages[0], transport.UserMessages[3]);
        }
    }
}
