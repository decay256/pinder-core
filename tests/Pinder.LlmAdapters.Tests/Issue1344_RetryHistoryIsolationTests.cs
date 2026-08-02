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

        [Fact]
        public async Task SessionPath_UsesTypedCanonicalHistoryAndCommitsBothCharacterPerspectives()
        {
            var transport = new RecordingTransport(ValidDirectionJson(), "A visible accepted reply.");
            var adapter = new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    PromptCatalog = BuiltInCatalog(),
                    MaxContractViolationRetries = 0,
                });
            var dateeHistory = new[]
            {
                ConversationMessage.User("canonical older player line"),
                ConversationMessage.Assistant("canonical older datee reply"),
            };

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeContext("visible delivered line"),
                dateeHistory,
                Array.Empty<ConversationMessage>(),
                dateeSession: null,
                avatarSession: null);

            Assert.True(adapter.SupportsConversationSessions);
            IReadOnlyList<ConversationMessage> sentHistory = Assert.Single(transport.PriorMessages);
            Assert.Equal(dateeHistory.Select(message => message.Role), sentHistory.Select(message => message.Role));
            Assert.Equal(dateeHistory.Select(message => message.Content), sentHistory.Select(message => message.Content));
            Assert.DoesNotContain("older visible player line", transport.ContextualUserMessages.Single(), StringComparison.Ordinal);
            Assert.DoesNotContain("older visible datee line", transport.ContextualUserMessages.Single(), StringComparison.Ordinal);
            Assert.NotNull(result.DateeSessionSnapshot);
            Assert.NotNull(result.AvatarSessionSnapshot);

            await using PiConversationSession datee = await PiConversationSession.RestoreOrImportAsync(
                result.DateeSessionSnapshot,
                Array.Empty<ConversationMessage>(),
                "datee");
            Assert.Equal(
                new[]
                {
                    "canonical older player line",
                    "canonical older datee reply",
                    "visible delivered line",
                    "A visible accepted reply.",
                },
                (await datee.BuildSemanticHistoryAsync()).Select(message => message.Content).ToArray());

            await using PiConversationSession avatar = await PiConversationSession.RestoreOrImportAsync(
                result.AvatarSessionSnapshot,
                Array.Empty<ConversationMessage>(),
                "avatar");
            var avatarMessages = await avatar.BuildSemanticHistoryAsync();
            Assert.Equal(
                new[] { ConversationMessage.AssistantRole, ConversationMessage.UserRole },
                avatarMessages.Select(message => message.Role).ToArray());
            Assert.Equal(
                new[] { "visible delivered line", "A visible accepted reply." },
                avatarMessages.Select(message => message.Content).ToArray());
        }

        [Fact]
        public void WrappedLegacyTransport_DoesNotAdvertiseSessionSupport()
        {
            ILlmTransport legacy = new LegacyTransport();
            var punctuation = new PunctuationNormalizingTransport(legacy);
            var thinking = new ThinkingStrippingLlmTransport(punctuation);
            var adapter = new PinderLlmAdapter(
                thinking,
                new PinderLlmAdapterOptions { GameDefinition = GameDefinition.PinderDefaults });

            Assert.False(punctuation.SupportsConversationMessages);
            Assert.False(punctuation.SupportsStructuredConversationMessages);
            Assert.False(thinking.SupportsConversationMessages);
            Assert.False(thinking.SupportsStructuredConversationMessages);
            Assert.False(adapter.SupportsConversationSessions);
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

        private sealed class RecordingTransport : IConversationLlmTransport
        {
            private readonly Queue<string> _responses;

            public RecordingTransport(params string[] responses)
            {
                _responses = new Queue<string>(responses);
            }

            public List<string?> Phases { get; } = new List<string?>();
            public List<IReadOnlyList<ConversationMessage>> PriorMessages { get; } = new();
            public List<string> ContextualUserMessages { get; } = new();
            public bool SupportsConversationMessages => true;

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

            public Task<string> SendConversationAsync(
                string systemPrompt,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Phases.Add(phase);
                PriorMessages.Add(priorMessages.ToArray());
                ContextualUserMessages.Add(userMessage);
                return Task.FromResult(_responses.Dequeue());
            }
        }

        private sealed class LegacyTransport : ILlmTransport
        {
            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
                => Task.FromResult(string.Empty);
        }
    }
}
