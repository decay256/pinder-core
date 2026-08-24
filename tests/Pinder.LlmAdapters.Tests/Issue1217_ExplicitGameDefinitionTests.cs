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
    public class Issue1217_ExplicitGameDefinitionTests
    {
        // 1. Production fail-loud.

        [Fact]
        public async Task Production_GetDialogueOptions_ThrowsInvalidOperationException_WhenGameDefinitionIsNull()
        {
            var transport = new FixedResponseTransport("OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none]\n\"Hi\"");
            var options = new PinderLlmAdapterOptions { GameDefinition = null };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DialogueContext(
                playerAvatarPrompt: "player spec",
                dateePrompt: "datee spec",
                conversationHistory: new List<(string, string)> { ("O", "Hi") },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "P",
                dateeName: "O",
                availableStats: new[] { StatType.Charm }
            );

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetDialogueOptionsAsync(context));
            Assert.Contains("GameDefinition", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Production_GetDateeResponse_ThrowsInvalidOperationException_WhenGameDefinitionIsNull()
        {
            var transport = new FixedResponseTransport("some datee response");
            var options = new PinderLlmAdapterOptions { GameDefinition = null };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DateeContext(
                dateePrompt: "datee spec",
                conversationHistory: new List<(string, string)> { ("O", "Hi") },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 0.5,
                playerName: "P",
                dateeName: "O",
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    TestHelpers.MakePsychiatricDiagnosis())
            );

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetDateeResponseAsync(context));
            Assert.Contains("GameDefinition", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // 2. Test/dev fallback parity.

        [Fact]
        public void PinderDefaults_GameMasterPrompt_DoesNotContainStaleRizzHorniness_AndContainsDespair()
        {
            var gmPrompt = GameDefinition.PinderDefaults.GameMasterPrompt;

            Assert.DoesNotContain("Rizz/Horniness", gmPrompt);
            Assert.Contains("Rizz/Despair", gmPrompt);
            Assert.Contains("Despair", gmPrompt);
        }

        // 3. Production path result contracts.

        [Fact]
        public async Task Production_GetDialogueOptions_ReturnsParsedOptions_WhenGameDefinitionIsProvided()
        {
            var transport = new FixedResponseTransport("OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none]\n\"Hello\"");
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DialogueContext(
                playerAvatarPrompt: "player spec",
                dateePrompt: "datee spec",
                conversationHistory: new List<(string, string)> { ("O", "Hi") },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "P",
                dateeName: "O",
                availableStats: new[] { StatType.Charm }
            );

            var result = await adapter.GetDialogueOptionsAsync(context);

            var option = Assert.Single(result);
            Assert.Equal(StatType.Charm, option.Stat);
            Assert.Equal("Hello", option.IntendedText);
            Assert.Null(option.CallbackTurnNumber);
            Assert.Null(option.ComboName);
            Assert.Equal(new[] { LlmPhase.DialogueOptions }, transport.Phases);
            Assert.All(transport.Calls, call =>
            {
                Assert.False(string.IsNullOrWhiteSpace(call.SystemPrompt));
                Assert.False(string.IsNullOrWhiteSpace(call.UserMessage));
            });
        }

        [Fact]
        public async Task Production_GetDateeResponse_ReturnsVisibleResponseAndRunsDirectorBeforePerformance_WhenGameDefinitionIsProvided()
        {
            const string visibleReply = "That lands softer than I expected.";
            var transport = new FixedResponseTransport(visibleReply);
            var options = new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults };
            var adapter = new PinderLlmAdapter(transport, options);

            var context = new DateeContext(
                dateePrompt: "datee spec",
                conversationHistory: new List<(string, string)> { ("O", "Hi") },
                dateeLastMessage: "Hi",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "Hello",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 0.5,
                playerName: "P",
                dateeName: "O",
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    TestHelpers.MakePsychiatricDiagnosis())
            );

            var result = await adapter.GetDateeResponseAsync(context);

            Assert.Equal(visibleReply, result.MessageText);
            Assert.Equal(
                new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse },
                transport.Phases);
            Assert.All(transport.Calls, call =>
            {
                Assert.False(string.IsNullOrWhiteSpace(call.SystemPrompt));
                Assert.False(string.IsNullOrWhiteSpace(call.UserMessage));
            });
        }

        private sealed class FixedResponseTransport : ILlmTransport
        {
            private readonly string _response;
            private readonly List<TransportCall> _calls = new List<TransportCall>();

            public FixedResponseTransport(string response) => _response = response;

            public IReadOnlyList<TransportCall> Calls => _calls;

            public string?[] Phases
            {
                get
                {
                    var phases = new string?[_calls.Count];
                    for (int i = 0; i < _calls.Count; i++)
                    {
                        phases[i] = _calls[i].Phase;
                    }

                    return phases;
                }
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
            {
                _calls.Add(new TransportCall(systemPrompt, userMessage, phase));

                if (string.Equals(phase, LlmPhase.EmotionalDirector, StringComparison.Ordinal))
                {
                    return Task.FromResult(ValidDirectorJson);
                }

                return Task.FromResult(_response);
            }
        }

        private sealed class TransportCall
        {
            public TransportCall(string systemPrompt, string userMessage, string? phase)
            {
                SystemPrompt = systemPrompt;
                UserMessage = userMessage;
                Phase = phase;
            }

            public string SystemPrompt { get; }

            public string UserMessage { get; }

            public string? Phase { get; }
        }

        private const string ValidDirectorJson =
            "{\"schema_version\":\"emotional_director.v1\",\"primary_emotion\":\"relief\",\"intensity\":\"moderate and steadily rising\",\"underlying_feeling\":\"fear of being dismissed\",\"interpretation\":\"reads the message as specific warmth that is probably meant for them\",\"impulse\":\"leans in with a careful question\",\"restraint\":\"keeps the reply tentative but available\",\"response_posture\":\"Writing from relief, turns warmer while still checking sincerity\"}";
    }
}
