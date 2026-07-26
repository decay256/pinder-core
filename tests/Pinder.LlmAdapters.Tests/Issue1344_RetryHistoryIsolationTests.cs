using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public sealed class Issue1344_RetryHistoryIsolationTests
    {
        static Issue1344_RetryHistoryIsolationTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        public async Task PerformanceSignalsAreParsedButOnlyVisibleDateeReplyEntersStatefulHistory()
        {
            const string visibleReply = "That actually does make me soften a little.";
            string rawResponse = visibleReply + "\n[SIGNALS]\nTELL: Charm (she lets the guard down)";
            var transport = new RecordingTransport(ValidDirectionJson(), rawResponse);
            var adapter = new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    PromptCatalog = BuiltInCatalog(),
                    MaxContractViolationRetries = 0,
                });

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeContext("visible delivered line"),
                Array.Empty<ConversationMessage>());

            Assert.Equal(new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse }, transport.Phases.ToArray());
            Assert.Equal(visibleReply, result.Response.MessageText);
            Assert.NotNull(result.Response.DetectedTell);
            Assert.Equal(2, result.NewHistoryEntries.Count);
            Assert.Equal(ConversationMessage.UserRole, result.NewHistoryEntries[0].Role);
            Assert.Equal("visible delivered line", result.NewHistoryEntries[0].Content);
            Assert.Equal(ConversationMessage.AssistantRole, result.NewHistoryEntries[1].Role);
            Assert.Equal(visibleReply, result.NewHistoryEntries[1].Content);
            Assert.DoesNotContain("[SIGNALS]", result.NewHistoryEntries[1].Content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TELL:", result.NewHistoryEntries[1].Content, StringComparison.OrdinalIgnoreCase);
        }

        private static DateeContext MakeContext(string deliveredMessage)
        {
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
                playerDeliveredMessage: deliveredMessage,
                interestBefore: 8,
                interestAfter: 12,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 4,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    TestHelpers.MakePsychiatricDiagnosis()));
        }

        private static string ValidDirectionJson()
        {
            return new JObject
            {
                ["schema_version"] = EmotionalDirectorContract.SchemaVersion,
                ["primary_emotion"] = "relieved but cautious",
                ["intensity"] = "moderate and steadily rising",
                ["underlying_feeling"] = "fear of being dismissed",
                ["interpretation"] = "reads the message as specific warmth that is probably meant for them",
                ["impulse"] = "leans in with a careful question",
                ["restraint"] = "keeps the reply tentative but available",
                ["response_posture"] = "turns warmer while still checking sincerity",
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

        private sealed class RecordingTransport : ILlmTransport
        {
            private readonly Queue<string> _responses;

            public RecordingTransport(params string[] responses)
            {
                _responses = new Queue<string>(responses);
            }

            public List<string?> Phases { get; } = new List<string?>();

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                Phases.Add(phase);
                return Task.FromResult(_responses.Dequeue());
            }
        }
    }
}
