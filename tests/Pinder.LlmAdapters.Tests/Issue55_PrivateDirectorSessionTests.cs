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
    public sealed class Issue55_PrivateDirectorSessionTests
    {
        static Issue55_PrivateDirectorSessionTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        public async Task PiPrivateBranch_InheritsContextAndCannotMutateCanonicalLeaf()
        {
            await using PiConversationSession canonical = await PiConversationSession.RestoreOrImportAsync(
                null,
                CanonicalHistory(),
                "datee");
            LlmConversationSessionSnapshot before = await canonical.SnapshotAsync();

            await using (PiConversationBranch branch = await canonical.ForkAsync("datee-private-analysis"))
            {
                Assert.Equal(
                    CanonicalHistory().Select(message => message.Content),
                    (await branch.BuildSemanticHistoryAsync()).Select(message => message.Content));
                await branch.AppendAcceptedExchangeAsync(
                    "private emotional source packet",
                    ValidDirectionJson());
                Assert.Contains(
                    "private emotional source packet",
                    (await branch.BuildSemanticHistoryAsync()).Select(message => message.Content));
            }

            LlmConversationSessionSnapshot after = await canonical.SnapshotAsync();
            Assert.Equal(before.Payload, after.Payload);
            Assert.Equal(
                CanonicalHistory().Select(message => message.Content),
                (await canonical.BuildSemanticHistoryAsync()).Select(message => message.Content));
        }

        [Fact]
        public async Task SessionDirector_UsesCharacterSystemAndTypedHistoryWithoutTranscriptDuplication()
        {
            var transport = new RecordingStructuredConversationTransport(
                new[] { ValidDirectionJson() },
                new[] { "A visible accepted reply." });
            var adapter = Adapter(transport, retries: 0);
            ConversationMessage[] canonicalHistory = CanonicalHistory();

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                Context(),
                canonicalHistory,
                Array.Empty<ConversationMessage>(),
                dateeSession: null,
                avatarSession: null);

            Assert.Empty(transport.LegacyStructuredRequests);
            StructuredConversationCall director = Assert.Single(transport.StructuredConversationCalls);
            Assert.Equal(canonicalHistory.Select(message => message.Role), director.PriorMessages.Select(message => message.Role));
            Assert.Equal(canonicalHistory.Select(message => message.Content), director.PriorMessages.Select(message => message.Content));
            Assert.Contains("DATEE CHARACTER SYSTEM MARKER", director.Request.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains("Produce one private emotional direction object", director.Request.SystemPrompt, StringComparison.Ordinal);
            Assert.True(
                director.Request.SystemPrompt.IndexOf("DATEE CHARACTER SYSTEM MARKER", StringComparison.Ordinal)
                < director.Request.SystemPrompt.IndexOf("Produce one private emotional direction object", StringComparison.Ordinal));
            Assert.Contains("current delivered player line", director.Request.UserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("legacy duplicate player line", director.Request.UserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("legacy duplicate datee reply", director.Request.UserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("canonical prior player line", director.Request.UserMessage, StringComparison.Ordinal);

            ConversationCall performance = Assert.Single(transport.ConversationCalls);
            Assert.Equal(LlmPhase.OpponentResponse, performance.Phase);
            Assert.Equal(canonicalHistory.Select(message => message.Content), performance.PriorMessages.Select(message => message.Content));

            await using PiConversationSession restored = await PiConversationSession.RestoreOrImportAsync(
                result.DateeSessionSnapshot,
                Array.Empty<ConversationMessage>(),
                "datee");
            Assert.Equal(
                new[]
                {
                    "canonical prior player line",
                    "canonical prior datee reply",
                    "current delivered player line",
                    "A visible accepted reply.",
                },
                (await restored.BuildSemanticHistoryAsync()).Select(message => message.Content).ToArray());
        }

        [Fact]
        public async Task DirectorContractRetry_ReusesUnchangedForkAndCommitsNoRejectedPrivateOutput()
        {
            var transport = new RecordingStructuredConversationTransport(
                new[] { "{\"schema_version\":\"wrong\"}", ValidDirectionJson() },
                new[] { "A visible reply after repair." });
            var adapter = Adapter(transport, retries: 1);
            ConversationMessage[] canonicalHistory = CanonicalHistory();

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                Context(),
                canonicalHistory,
                Array.Empty<ConversationMessage>(),
                dateeSession: null,
                avatarSession: null);

            Assert.Equal(2, transport.StructuredConversationCalls.Count);
            Assert.All(
                transport.StructuredConversationCalls,
                call => Assert.Equal(
                    canonicalHistory.Select(message => message.Content),
                    call.PriorMessages.Select(message => message.Content)));
            Assert.Equal(
                transport.StructuredConversationCalls[0].Request.UserMessage,
                transport.StructuredConversationCalls[1].Request.UserMessage);
            Assert.DoesNotContain(
                "wrong",
                string.Join("|", transport.StructuredConversationCalls[1].PriorMessages.Select(message => message.Content)),
                StringComparison.Ordinal);
            Assert.Contains(
                "previous emotional direction did not satisfy",
                transport.StructuredConversationCalls[1].Request.SystemPrompt,
                StringComparison.OrdinalIgnoreCase);

            await using PiConversationSession restored = await PiConversationSession.RestoreOrImportAsync(
                result.DateeSessionSnapshot,
                Array.Empty<ConversationMessage>(),
                "datee");
            string canonicalText = string.Join(
                "|",
                (await restored.BuildSemanticHistoryAsync()).Select(message => message.Content));
            Assert.DoesNotContain("schema_version", canonicalText, StringComparison.Ordinal);
            Assert.DoesNotContain("wrong", canonicalText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task DirectorCancellation_DoesNotRunPerformanceOrMutateInputSnapshot()
        {
            LlmConversationSessionSnapshot originalSnapshot;
            await using (PiConversationSession original = await PiConversationSession.RestoreOrImportAsync(
                null,
                CanonicalHistory(),
                "datee"))
            {
                originalSnapshot = await original.SnapshotAsync();
            }

            var transport = new RecordingStructuredConversationTransport(
                Array.Empty<string>(),
                Array.Empty<string>())
            {
                CancelDirector = true,
            };
            var adapter = Adapter(transport, retries: 0);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.GetDateeResponseAsync(
                Context(),
                CanonicalHistory(),
                Array.Empty<ConversationMessage>(),
                originalSnapshot,
                avatarSession: null));

            Assert.Single(transport.StructuredConversationCalls);
            Assert.Empty(transport.ConversationCalls);
            await using PiConversationSession restored = await PiConversationSession.RestoreOrImportAsync(
                originalSnapshot,
                Array.Empty<ConversationMessage>(),
                "datee");
            Assert.Equal(
                CanonicalHistory().Select(message => message.Content),
                (await restored.BuildSemanticHistoryAsync()).Select(message => message.Content));
        }

        private static PinderLlmAdapter Adapter(ILlmTransport transport, int retries)
            => new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    PromptCatalog = BuiltInCatalog(),
                    MaxContractViolationRetries = retries,
                    ContractViolationBackoffMs = 0,
                });

        private static ConversationMessage[] CanonicalHistory()
            => new[]
            {
                ConversationMessage.User("canonical prior player line"),
                ConversationMessage.Assistant("canonical prior datee reply"),
            };

        private static DateeContext Context()
            => new DateeContext(
                dateePrompt: "DATEE CHARACTER SYSTEM MARKER: full biography and psychological state.",
                conversationHistory: new[]
                {
                    ("Player", "legacy duplicate player line"),
                    ("Datee", "legacy duplicate datee reply"),
                },
                dateeLastMessage: "legacy duplicate datee reply",
                activeTraps: Array.Empty<string>(),
                currentInterest: 12,
                playerDeliveredMessage: "current delivered player line",
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

        private static string ValidDirectionJson()
            => new JObject
            {
                ["schema_version"] = CharacterEmotionalDirectionContract.SchemaVersion,
                ["primary_emotion"] = "relief",
                ["intensity"] = "moderate and steadily rising",
                ["underlying_feeling"] = "fear of being dismissed",
                ["interpretation"] = "reads the message as specific warmth that is probably meant for them",
                ["impulse"] = "leans in with a careful question",
                ["restraint"] = "keeps the reply tentative but available",
                ["response_posture"] = "Writing from relief, turns warmer while still checking sincerity",
            }.ToString(Formatting.None);

        private static PromptCatalog BuiltInCatalog()
        {
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            catalog.ValidateRuntimeCatalog();
            return catalog;
        }

        private static string FindPromptsRoot()
        {
            string? directory = AppDomain.CurrentDomain.BaseDirectory;
            while (directory != null)
            {
                string candidate = Path.Combine(directory, "data", "prompts");
                if (Directory.Exists(candidate)) return candidate;
                directory = Path.GetDirectoryName(directory);
            }
            throw new DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }

        private sealed class RecordingStructuredConversationTransport :
            IConversationLlmTransport,
            IStructuredConversationLlmTransport
        {
            private readonly Queue<string> _structuredResponses;
            private readonly Queue<string> _plainResponses;

            public RecordingStructuredConversationTransport(
                IEnumerable<string> structuredResponses,
                IEnumerable<string> plainResponses)
            {
                _structuredResponses = new Queue<string>(structuredResponses);
                _plainResponses = new Queue<string>(plainResponses);
            }

            public bool SupportsConversationMessages => true;
            public bool SupportsStructuredConversationMessages => true;
            public bool CancelDirector { get; set; }
            public List<StructuredLlmRequest> LegacyStructuredRequests { get; } = new();
            public List<StructuredConversationCall> StructuredConversationCalls { get; } = new();
            public List<ConversationCall> ConversationCalls { get; } = new();

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
                => Task.FromResult(_plainResponses.Dequeue());

            public Task<string> SendConversationAsync(
                string systemPrompt,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken cancellationToken = default)
            {
                ConversationCalls.Add(new ConversationCall(
                    systemPrompt,
                    priorMessages.ToArray(),
                    userMessage,
                    phase));
                return Task.FromResult(_plainResponses.Dequeue());
            }

            public Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                LegacyStructuredRequests.Add(request);
                return Task.FromResult(new StructuredLlmResponse(_structuredResponses.Dequeue()));
            }

            public Task<StructuredLlmResponse> SendStructuredConversationAsync(
                StructuredLlmRequest request,
                IReadOnlyList<ConversationMessage> priorMessages,
                CancellationToken cancellationToken = default)
            {
                StructuredConversationCalls.Add(new StructuredConversationCall(
                    request,
                    priorMessages.ToArray()));
                if (CancelDirector)
                    throw new OperationCanceledException(cancellationToken);
                return Task.FromResult(new StructuredLlmResponse(_structuredResponses.Dequeue()));
            }
        }

        private sealed class StructuredConversationCall
        {
            public StructuredConversationCall(
                StructuredLlmRequest request,
                IReadOnlyList<ConversationMessage> priorMessages)
            {
                Request = request;
                PriorMessages = priorMessages;
            }

            public StructuredLlmRequest Request { get; }
            public IReadOnlyList<ConversationMessage> PriorMessages { get; }
        }

        private sealed class ConversationCall
        {
            public ConversationCall(
                string systemPrompt,
                IReadOnlyList<ConversationMessage> priorMessages,
                string userMessage,
                string? phase)
            {
                SystemPrompt = systemPrompt;
                PriorMessages = priorMessages;
                UserMessage = userMessage;
                Phase = phase;
            }

            public string SystemPrompt { get; }
            public IReadOnlyList<ConversationMessage> PriorMessages { get; }
            public string UserMessage { get; }
            public string? Phase { get; }
        }
    }
}
