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
using Pinder.Core.Text;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public sealed class Issue1342_1343_EmotionalDirectorPerformanceTests
    {
        static Issue1342_1343_EmotionalDirectorPerformanceTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        public async Task GetDateeResponseAsync_RunsDirectorBeforePerformanceAndInjectsActionableDirection()
        {
            var transport = new RecordingTransport(ValidDirectionJson(), "That lands softer than I expected.");
            var adapter = CreateAdapter(transport);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeContext(
                    interestBefore: 4,
                    interestAfter: 6,
                    beforeState: InterestState.Bored,
                    afterState: InterestState.Lukewarm,
                    deliveredMessage: "visible delivered line"),
                Array.Empty<ConversationMessage>());

            Assert.Equal(new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse }, transport.Phases.ToArray());
            string performancePrompt = transport.UserMessages[1];
            Assert.Contains("DATEE EMOTIONAL PERFORMANCE DIRECTION", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("Primary emotion: relieved but cautious", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("Intensity: moderate and steadily rising", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("Underlying feeling: fear of being dismissed", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("Interpretation: reads the message as specific warmth that is probably meant for them", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("Impulse: leans in with a careful question", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("Restraint: keeps the reply tentative but available", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("Response posture: turns warmer while still checking sincerity", performancePrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("\"primary_emotion\"", performancePrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("Private emotional director source packet", performancePrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("Psychiatric diagnosis", performancePrompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TherapistDiagnosisContract.DerivedFeelingKey, performancePrompt, StringComparison.Ordinal);
            Assert.Equal("That lands softer than I expected.", result.Response.MessageText.Trim());
        }

        [Fact]
        public async Task StructuredDirectorThenPlainPerformance_UsesStructuredTransportOnlyForDirector()
        {
            var transport = new RecordingStructuredTransport(
                new StructuredLlmResponse(ValidDirectionJson(primaryEmotion: "quietly delighted"), provider: "test", model: "structured", usedNativeStructuredOutput: true),
                "I did not expect that to make me smile.");
            var adapter = CreateAdapter(transport);

            await adapter.GetDateeResponseAsync(MakeContext(), Array.Empty<ConversationMessage>());

            Assert.Equal(1, transport.StructuredCalls);
            Assert.Equal(1, transport.PlainCalls);
            Assert.Equal(LlmPhase.EmotionalDirector, transport.LastStructuredRequest!.Phase);
            Assert.Equal(LlmPhase.OpponentResponse, transport.Phases.Single());
            Assert.Contains("Primary emotion: quietly delighted", transport.UserMessages.Single(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task DirectorRetryCompletesBeforeAnyPerformanceAndExhaustionPreventsPerformance()
        {
            var acceptedTransport = new RecordingTransport("not json", ValidDirectionJson(), "Visible response after retry.");
            var acceptedAdapter = CreateAdapter(acceptedTransport, retries: 1);

            await acceptedAdapter.GetDateeResponseAsync(MakeContext(), Array.Empty<ConversationMessage>());

            Assert.Equal(
                new[] { LlmPhase.EmotionalDirector, LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse },
                acceptedTransport.Phases.ToArray());

            var exhaustedTransport = new RecordingTransport("not json", "still not json");
            var exhaustedAdapter = CreateAdapter(exhaustedTransport, retries: 1);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => exhaustedAdapter.GetDateeResponseAsync(MakeContext(), Array.Empty<ConversationMessage>()));

            Assert.Equal(LlmPhase.EmotionalDirector, ex.Phase);
            Assert.DoesNotContain(LlmPhase.OpponentResponse, exhaustedTransport.Phases);
        }

        [Fact]
        public async Task DirectorCancellationPropagatesBeforePerformance()
        {
            var transport = new CancelOnPhaseTransport(LlmPhase.EmotionalDirector);
            var adapter = CreateAdapter(transport);

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => adapter.GetDateeResponseAsync(MakeContext(), Array.Empty<ConversationMessage>()));

            Assert.Equal(new[] { LlmPhase.EmotionalDirector }, transport.Phases.ToArray());
        }

        [Fact]
        public async Task PerformanceRetryReusesSingleDirectorResultAndReturnsOnlyVisibleHistory()
        {
            var transport = new RecordingTransport(
                ValidDirectionJson(impulse: "wants to answer with a precise invitation"),
                "",
                "A visible accepted reply.");
            var adapter = CreateAdapter(transport, retries: 1);
            DateeContext context = MakeContext(deliveredMessage: "visible delivered line");

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(context, Array.Empty<ConversationMessage>());

            Assert.Equal(
                new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse, LlmPhase.OpponentResponse },
                transport.Phases.ToArray());
            Assert.Equal(1, transport.Phases.Count(phase => phase == LlmPhase.EmotionalDirector));
            Assert.Equal(
                2,
                transport.UserMessages.Count(message =>
                    message.Contains("Impulse: wants to answer with a precise invitation", StringComparison.Ordinal)));
            Assert.Equal(2, result.NewHistoryEntries.Count);
            Assert.Equal(ConversationMessage.UserRole, result.NewHistoryEntries[0].Role);
            Assert.Equal("visible delivered line", result.NewHistoryEntries[0].Content);
            Assert.Equal(ConversationMessage.AssistantRole, result.NewHistoryEntries[1].Role);
            Assert.Equal("A visible accepted reply.", result.NewHistoryEntries[1].Content);
        }

        [Theory]
        [InlineData("DATEE EMOTIONAL PERFORMANCE DIRECTION")]
        [InlineData("Primary emotion:")]
        [InlineData("Intensity:")]
        [InlineData("Underlying feeling:")]
        [InlineData("Interpretation:")]
        [InlineData("Impulse:")]
        [InlineData("Restraint:")]
        [InlineData("Response posture:")]
        [InlineData("primary emotion:")]
        [InlineData("**Primary emotion:**")]
        [InlineData("- Primary emotion:")]
        [InlineData("### **datee emotional performance direction**")]
        [InlineData("`Primary emotion:`")]
        [InlineData("~~Intensity:~~")]
        [InlineData("\"Interpretation:\"")]
        [InlineData("(Impulse:)")]
        [InlineData("\u201cRestraint:\u201d")]
        [InlineData("1. Primary emotion:")]
        [InlineData("\u2605 Primary emotion:")]
        public async Task PrivateDirectionMarkerInPerformanceResponseRetriesAndPersistsOnlyAcceptedResponse(
            string privateMarker)
        {
            string leakedResponse = "I should not expose this.\n" + privateMarker + " private value";
            var violations = new List<LlmContractViolation>();
            var transport = new RecordingTransport(
                ValidDirectionJson(),
                leakedResponse,
                "Only this visible response may be delivered.");
            var adapter = CreateAdapter(transport, retries: 1, onViolation: violations.Add);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeContext(),
                Array.Empty<ConversationMessage>());

            Assert.Equal(1, transport.Phases.Count(phase => phase == LlmPhase.EmotionalDirector));
            Assert.Equal(2, transport.Phases.Count(phase => phase == LlmPhase.OpponentResponse));
            Assert.Single(violations);
            Assert.Equal("private_direction_leak", violations[0].Reason);
            Assert.Equal(2, result.NewHistoryEntries.Count);
            Assert.Equal("Only this visible response may be delivered.", result.NewHistoryEntries[1].Content);
            Assert.All(
                result.NewHistoryEntries,
                entry => Assert.DoesNotContain(privateMarker, entry.Content, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("intensity matters when you say it like that.")]
        [InlineData("my interpretation is that you meant well.")]
        [InlineData("DATEE EMOTIONAL PERFORMANCE DIRECTIONAL")]
        public async Task OrdinaryLookalikesRemainAccepted(string visibleResponse)
        {
            var transport = new RecordingTransport(ValidDirectionJson(), visibleResponse);
            var adapter = CreateAdapter(transport, retries: 0);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeContext(),
                Array.Empty<ConversationMessage>());

            Assert.Equal(1, transport.Phases.Count(phase => phase == LlmPhase.EmotionalDirector));
            Assert.Equal(1, transport.Phases.Count(phase => phase == LlmPhase.OpponentResponse));
            Assert.Equal(visibleResponse, result.Response.MessageText);
            Assert.Equal(visibleResponse, result.NewHistoryEntries[1].Content);
        }

        [Fact]
        public async Task PrivateDirectionLeakExhaustionReturnsNoStatefulResultAndSanitizesException()
        {
            const string leakedValue = "Primary emotion: SECRET-DIRECTION-VALUE";
            var transport = new RecordingTransport(ValidDirectionJson(), leakedValue, leakedValue);
            var adapter = CreateAdapter(transport, retries: 1);
            StatefulDateeResult? result = null;

            LlmContractException exception = await Assert.ThrowsAsync<LlmContractException>(
                async () => result = await adapter.GetDateeResponseAsync(
                    MakeContext(),
                    Array.Empty<ConversationMessage>()));

            Assert.Null(result);
            Assert.Equal("private_direction_leak", exception.Reason);
            Assert.DoesNotContain("SECRET-DIRECTION-VALUE", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET-DIRECTION-VALUE", exception.ToString(), StringComparison.Ordinal);
            Assert.Equal(1, transport.Phases.Count(phase => phase == LlmPhase.EmotionalDirector));
            Assert.Equal(2, transport.Phases.Count(phase => phase == LlmPhase.OpponentResponse));
        }

        [Fact]
        public async Task PerformanceCancellationAfterSuccessfulDirectorPropagatesWithoutResult()
        {
            var transport = new CancelOnPhaseTransport(LlmPhase.OpponentResponse);
            var adapter = CreateAdapter(transport);
            StatefulDateeResult? result = null;

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => result = await adapter.GetDateeResponseAsync(
                    MakeContext(),
                    Array.Empty<ConversationMessage>()));

            Assert.Null(result);
            Assert.Equal(
                new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse },
                transport.Phases.ToArray());
        }

        [Fact]
        public async Task MissingEmotionalTurnEventFailsClosedBeforeTransport()
        {
            var transport = new RecordingTransport("should never be used");
            var adapter = CreateAdapter(transport);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => adapter.GetDateeResponseAsync(MakeContextWithoutEmotionalTurnEvent(), Array.Empty<ConversationMessage>()));

            Assert.Contains("EmotionalTurnEvent", ex.Message, StringComparison.Ordinal);
            Assert.Empty(transport.Phases);
        }

        [Fact]
        public void PerformancePromptTrace_AttributesYamlWrapperAndSevenRuntimeDirectorFields()
        {
            PromptCatalog catalog = BuiltInCatalog();
            DateeContext context = MakeContext();
            var direction = ValidDirection(primaryEmotion: "warmly unsettled", responsePosture: "lets the reply open one careful door");

            PromptTraceResult trace = SessionDocumentBuilder.BuildDateePerformancePromptEx(context, direction, catalog);

            Assert.Contains(
                trace.Spans,
                span => span.SourceFile == "data/prompts/emotional-reactions.yaml"
                    && span.Key == "emotional-reaction-performance-direction");
            foreach (string key in new[]
            {
                "EmotionalDirector.PrimaryEmotion",
                "EmotionalDirector.Intensity",
                "EmotionalDirector.UnderlyingFeeling",
                "EmotionalDirector.Interpretation",
                "EmotionalDirector.Impulse",
                "EmotionalDirector.Restraint",
                "EmotionalDirector.ResponsePosture",
            })
            {
                Assert.Contains(
                    trace.Spans,
                    span => span.SourceFile == SessionDocumentBuilder.EmotionalDirectorRuntimeSource
                        && span.Key == key);
            }

            int performanceIndex = trace.Spans.First(span => span.Key == "emotional-reaction-performance-direction").Start;
            int finalIndex = trace.Spans.First(span => span.Key == "datee-response-instruction").Start;
            Assert.InRange(performanceIndex, 0, finalIndex - 1);
        }

        [Fact]
        public void RuntimeValidationRejectsMissingPerformanceDirectionPlaceholder()
        {
            string root = CopyPromptsToTemp(FindPromptsRoot());
            try
            {
                string path = Path.Combine(root, "emotional-reactions.yaml");
                File.WriteAllText(
                    path,
                    File.ReadAllText(path).Replace(
                        "{response_posture}",
                        "response posture",
                        StringComparison.Ordinal));

                var catalog = PromptCatalog.LoadFromDirectory(root);
                var error = Assert.Throws<InvalidOperationException>(() => catalog.ValidateRuntimeCatalog());

                Assert.Contains("emotional-reaction-performance-direction", error.Message, StringComparison.Ordinal);
                Assert.Contains("{response_posture}", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Theory]
        [InlineData(
            "DATEE EMOTIONAL PERFORMANCE DIRECTION",
            "DATEE PRIVATE PERFORMANCE DIRECTION",
            "DATEE EMOTIONAL PERFORMANCE DIRECTION")]
        [InlineData(
            "Primary emotion: {primary_emotion}",
            "Core emotion: {primary_emotion}",
            "Primary emotion: {primary_emotion}")]
        [InlineData(
            "Restraint: {restraint}",
            "{restraint}",
            "Restraint: {restraint}")]
        public void RuntimeValidationRejectsPerformanceProtectionContractDrift(
            string original,
            string replacement,
            string expectedStructuralLine)
        {
            string root = CopyPromptsToTemp(FindPromptsRoot());
            try
            {
                string path = Path.Combine(root, "emotional-reactions.yaml");
                File.WriteAllText(
                    path,
                    File.ReadAllText(path).Replace(original, replacement, StringComparison.Ordinal));

                var catalog = PromptCatalog.LoadFromDirectory(root);
                var error = Assert.Throws<InvalidOperationException>(() => catalog.ValidateRuntimeCatalog());

                Assert.Contains("emotional-reaction-performance-direction", error.Message, StringComparison.Ordinal);
                Assert.Contains(expectedStructuralLine, error.Message, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Theory]
        [InlineData(2, InterestState.Bored, RollOutcomeIntensity.Catastrophe, "sharply hurt", "pulls the reply back behind a clean boundary")]
        [InlineData(21, InterestState.AlmostThere, RollOutcomeIntensity.Catastrophe, "hurt because closeness was already plausible", "answers with warmth bruised by disbelief")]
        [InlineData(2, InterestState.Bored, RollOutcomeIntensity.Strong, "surprised into reluctant curiosity", "lets one guarded question through")]
        [InlineData(21, InterestState.AlmostThere, RollOutcomeIntensity.Strong, "tenderly reassured", "lets the reply become unmistakably warmer")]
        public void PerformancePromptFixtureMatrix_RendersContrastingDirectionsWithoutPrivateCompilerArtifact(
            int interest,
            InterestState state,
            RollOutcomeIntensity outcome,
            string primaryEmotion,
            string posture)
        {
            var context = MakeContext(
                interestBefore: interest,
                interestAfter: interest,
                beforeState: state,
                afterState: state,
                outcome: outcome);
            var direction = ValidDirection(primaryEmotion: primaryEmotion, responsePosture: posture);

            string prompt = SessionDocumentBuilder.BuildDateePerformancePromptEx(context, direction, BuiltInCatalog()).Text;

            Assert.Contains("Primary emotion: " + primaryEmotion, prompt, StringComparison.Ordinal);
            Assert.Contains("Response posture: " + posture, prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("Private emotional director input", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("Character-specific emotional translation", prompt, StringComparison.Ordinal);
        }

        private static PinderLlmAdapter CreateAdapter(
            ILlmTransport transport,
            int retries = 0,
            Action<LlmContractViolation>? onViolation = null)
        {
            return new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    PromptCatalog = BuiltInCatalog(),
                    MaxContractViolationRetries = retries,
                    ContractViolationBackoffMs = 1,
                    OnLlmContractViolation = onViolation,
                });
        }

        private static DateeContext MakeContext(
            string deliveredMessage = "visible delivered line",
            int interestBefore = 8,
            int interestAfter = 12,
            InterestState beforeState = InterestState.Lukewarm,
            InterestState afterState = InterestState.Interested,
            RollOutcomeIntensity outcome = RollOutcomeIntensity.Strong)
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
                currentInterest: interestAfter,
                playerDeliveredMessage: deliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 7,
                interestBeforeState: beforeState,
                interestAfterState: afterState,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    outcome,
                    TestHelpers.MakePsychiatricDiagnosis()));
        }

        private static DateeContext MakeContextWithoutEmotionalTurnEvent()
        {
            return new DateeContext(
                dateePrompt: "datee prompt",
                conversationHistory: Array.Empty<(string Sender, string Text)>(),
                dateeLastMessage: "",
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
                interestAfterState: InterestState.Interested);
        }

        private static EmotionalDirectorDirection ValidDirection(
            string? primaryEmotion = null,
            string? intensity = null,
            string? underlyingFeeling = null,
            string? interpretation = null,
            string? impulse = null,
            string? restraint = null,
            string? responsePosture = null)
        {
            return new EmotionalDirectorDirection(
                primaryEmotion ?? "relieved but cautious",
                intensity ?? "moderate and steadily rising",
                underlyingFeeling ?? "fear of being dismissed",
                interpretation ?? "reads the message as specific warmth that is probably meant for them",
                impulse ?? "leans in with a careful question",
                restraint ?? "keeps the reply tentative but available",
                responsePosture ?? "turns warmer while still checking sincerity");
        }

        private static string ValidDirectionJson(
            string? primaryEmotion = null,
            string? intensity = null,
            string? underlyingFeeling = null,
            string? interpretation = null,
            string? impulse = null,
            string? restraint = null,
            string? responsePosture = null)
        {
            var direction = ValidDirection(
                primaryEmotion,
                intensity,
                underlyingFeeling,
                interpretation,
                impulse,
                restraint,
                responsePosture);
            return new JObject
            {
                ["primary_emotion"] = direction.PrimaryEmotion,
                ["intensity"] = direction.Intensity,
                ["underlying_feeling"] = direction.UnderlyingFeeling,
                ["interpretation"] = direction.Interpretation,
                ["impulse"] = direction.Impulse,
                ["restraint"] = direction.Restraint,
                ["response_posture"] = direction.ResponsePosture,
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

        private static string CopyPromptsToTemp(string source)
        {
            string destination = Path.Combine(
                Path.GetTempPath(),
                "issue1342-prompt-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source, "*.yaml"))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            return destination;
        }

        private class RecordingTransport : ILlmTransport
        {
            private readonly Queue<string> _responses;

            public RecordingTransport(params string[] responses)
            {
                _responses = new Queue<string>(responses);
            }

            public int PlainCalls { get; private set; }
            public List<string?> Phases { get; } = new List<string?>();
            public List<string> UserMessages { get; } = new List<string>();

            public virtual Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                PlainCalls++;
                Phases.Add(phase);
                UserMessages.Add(userMessage);
                return Task.FromResult(_responses.Dequeue());
            }
        }

        private sealed class RecordingStructuredTransport : RecordingTransport, IStructuredLlmTransport
        {
            private readonly Queue<StructuredLlmResponse> _structuredResponses;

            public RecordingStructuredTransport(StructuredLlmResponse structuredResponse, params string[] plainResponses)
                : base(plainResponses)
            {
                _structuredResponses = new Queue<StructuredLlmResponse>(new[] { structuredResponse });
            }

            public int StructuredCalls { get; private set; }
            public StructuredLlmRequest? LastStructuredRequest { get; private set; }

            public Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                StructuredCalls++;
                LastStructuredRequest = request;
                return Task.FromResult(_structuredResponses.Dequeue());
            }
        }

        private sealed class CancelOnPhaseTransport : ILlmTransport
        {
            private readonly string _phaseToCancel;

            public CancelOnPhaseTransport(string phaseToCancel)
            {
                _phaseToCancel = phaseToCancel;
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
                Phases.Add(phase);
                if (string.Equals(phase, _phaseToCancel, StringComparison.Ordinal))
                {
                    throw new OperationCanceledException(ct);
                }

                return Task.FromResult(ValidDirectionJson());
            }
        }
    }
}
