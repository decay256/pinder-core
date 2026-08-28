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
    public sealed class Issue1342_1343_EmotionalDirectorPerformanceTests
    {
        private static readonly Guid DateeId = Guid.Parse("8fb6db12-c8f7-4f76-bb02-146ec39ef215");

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
                    interestAfter: 4,
                    beforeState: InterestState.Bored,
                    afterState: InterestState.Bored,
                    deliveredMessage: "visible delivered line",
                    cognitiveSubtextFact: CognitivePressure(),
                    reactionTarget: CuratedBufferTarget()),
                Array.Empty<ConversationMessage>());

            Assert.Equal(new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse }, transport.Phases.ToArray());
            string performancePrompt = transport.UserMessages[1];
            Assert.Contains("[ENGINE — DATEE RESPONSE PLAN]", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("\"schema_version\":\"datee_response_plan.v1\"", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("\"primary_emotion\":\"relief\"", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("\"regulatory_state\":\"controlled\"", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("\"activation\":4", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("\"trajectory\":\"escalating\"", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("\"movement\":\"hold\"", performancePrompt, StringComparison.Ordinal);
            Assert.Contains("\"disclosure\":\"voluntary\"", performancePrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("DATEE EMOTIONAL PERFORMANCE DIRECTION", performancePrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("ABANDONMENT + DEFLECTION", performancePrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("buffered disclosure", performancePrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("Private emotional director source packet", performancePrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("Psychiatric diagnosis", performancePrompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TherapistDiagnosisContract.DerivedFeelingKey, performancePrompt, StringComparison.Ordinal);
            Assert.Equal("That lands softer than I expected.", result.Response.MessageText.Trim());
            Assert.NotNull(result.Response.EmotionalReactionDebug);
            Assert.Equal("relief", result.Response.EmotionalReactionDebug!.Direction!.PrimaryEmotion);
            Assert.Equal("none", result.Response.EmotionalReactionDebug.Direction.SecondaryEmotion);
            Assert.Equal("controlled", result.Response.EmotionalReactionDebug.Direction.RegulatoryState);
            Assert.Equal(4, result.Response.EmotionalReactionDebug.Direction.Activation);
            Assert.Equal("escalating", result.Response.EmotionalReactionDebug.Direction.Trajectory);
            Assert.Equal("fear of being dismissed", result.Response.EmotionalReactionDebug.Direction.CoreThreatOrDesire);
            Assert.Equal("reads the message as specific warmth that is probably meant for them", result.Response.EmotionalReactionDebug.Direction.Interpretation);
            Assert.Equal("leans in with a careful question", result.Response.EmotionalReactionDebug.Direction.Impulse);
            Assert.Equal("keeps the reply tentative but available", result.Response.EmotionalReactionDebug.Direction.Restraint);
            Assert.Equal("turns warmer while still checking sincerity", result.Response.EmotionalReactionDebug.Direction.ResponsePosture);
            Assert.Equal("ABANDONMENT + DEFLECTION", result.Response.EmotionalReactionDebug.CognitiveSubtext);
            Assert.Equal("admit the move was difficult", result.Response.EmotionalReactionDebug.TransitionTarget);
            Assert.Equal("buffered disclosure", result.Response.EmotionalReactionDebug.TransitionStyle);
            Assert.NotNull(result.Response.EmotionalReactionDebug.CompiledPromptInstruction);
            Assert.NotNull(result.Response.EmotionalReactionDebug.ResponsePlan);
            Assert.Equal(DateeResponseMovement.Hold, result.Response.EmotionalReactionDebug.ResponsePlan!.Movement);
            Assert.Equal(DateeResponseDisclosure.Voluntary, result.Response.EmotionalReactionDebug.ResponsePlan.Disclosure);
            Assert.StartsWith(
                "<ENGINE_STATE>",
                result.Response.EmotionalReactionDebug.CompiledPromptInstruction,
                StringComparison.Ordinal);
            Assert.Contains(
                "\"primary_emotion\":\"relief\"",
                result.Response.EmotionalReactionDebug.CompiledPromptInstruction,
                StringComparison.Ordinal);
            Assert.Contains(
                "\"movement\":\"hold\"",
                result.Response.EmotionalReactionDebug.CompiledPromptInstruction,
                StringComparison.Ordinal);
            Assert.Contains(
                "\"text\":\"visible delivered line\"",
                result.Response.EmotionalReactionDebug.CompiledPromptInstruction,
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task StructuredDirectorThenStructuredPerformance_UsesProviderNeutralContractsForBothCalls()
        {
            var transport = new RecordingTransport(
                ValidDirectionJson(primaryEmotion: "joy"),
                "I did not expect that to make me smile.");
            var adapter = CreateAdapter(transport);

            await adapter.GetDateeResponseAsync(MakeContext(), Array.Empty<ConversationMessage>());

            Assert.Equal(2, transport.StructuredCalls);
            Assert.Equal(0, transport.PlainCalls);
            Assert.Equal(
                new[] { "emotional_director", DateePerformanceStructuredContract.SchemaName },
                transport.StructuredRequests.Select(request => request.SchemaName).ToArray());
            Assert.Equal(
                new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse },
                transport.Phases.ToArray());
            Assert.Contains("\"primary_emotion\":\"joy\"", transport.UserMessages[1], StringComparison.Ordinal);
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
                    message.Contains("\"primary_emotion\":\"relief\"", StringComparison.Ordinal)));
            Assert.Equal(2, result.NewHistoryEntries.Count);
            Assert.Equal(ConversationMessage.UserRole, result.NewHistoryEntries[0].Role);
            Assert.Equal("visible delivered line", result.NewHistoryEntries[0].Content);
            Assert.Equal(ConversationMessage.AssistantRole, result.NewHistoryEntries[1].Role);
            Assert.Equal("A visible accepted reply.", result.NewHistoryEntries[1].Content);
        }

        [Theory]
        [InlineData("DATEE EMOTIONAL PERFORMANCE DIRECTION")]
        [InlineData("Primary emotion:")]
        [InlineData("Activation:")]
        [InlineData("Core threat/desire:")]
        [InlineData("Interpretation:")]
        [InlineData("Impulse:")]
        [InlineData("Restraint:")]
        [InlineData("Response posture:")]
        [InlineData("primary emotion:")]
        [InlineData("**Primary emotion:**")]
        [InlineData("- Primary emotion:")]
        [InlineData("### **datee emotional performance direction**")]
        [InlineData("`Primary emotion:`")]
        [InlineData("~~Activation:~~")]
        [InlineData("\"Interpretation:\"")]
        [InlineData("(Impulse:)")]
        [InlineData("\u201cRestraint:\u201d")]
        [InlineData("1. Primary emotion:")]
        [InlineData("\u2605 Primary emotion:")]
        [InlineData("\u26A0\uFE0F Primary emotion:")]
        [InlineData("\u200BPrimary emotion:")]
        [InlineData("\u2060Primary emotion:")]
        [InlineData("\uFEFFDATEE EMOTIONAL PERFORMANCE DIRECTION")]
        [InlineData("\u0301Primary emotion:")]
        [InlineData("1\uFE0F\u20E3 Primary emotion:")]
        [InlineData("\u2460 Primary emotion:")]
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
        [InlineData("1Primary emotion: this is ordinary digit-leading text.")]
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
        public void PerformancePromptTrace_AttributesCatalogPlanWrapperAndCanonicalPlanArtifact()
        {
            PromptCatalog catalog = BuiltInCatalog();
            DateeContext context = MakeContext();
            var direction = ValidDirection(primaryEmotion: "fear", regulatoryState: "controlled", responsePosture: "lets the reply open one careful door");
            DateeResponsePlan plan = CompilePlan(context, direction);

            PromptTraceResult trace = SessionDocumentBuilder.BuildDateePerformancePromptEx(context, plan, catalog);

            Assert.Contains(
                trace.Spans,
                span => span.SourceFile == "data/prompts/templates.yaml"
                    && span.Key == "datee-response-plan-performance");
            Assert.Contains(
                trace.Spans,
                span => span.SourceFile == "runtime:datee-response-plan"
                    && span.Key == DateeResponsePlan.CurrentSchemaVersion);

            int performanceIndex = trace.Spans.First(span => span.Key == "datee-response-plan-performance").Start;
            int finalIndex = trace.Spans.First(span => span.Key == "datee-response-instruction").Start;
            Assert.InRange(performanceIndex, 0, finalIndex - 1);

            string debugInstruction = SessionDocumentBuilder.ExtractAnnotatedInstruction(
                trace,
                "datee-response-plan-performance");
            Assert.StartsWith("<ENGINE_STATE>", debugInstruction, StringComparison.Ordinal);
            Assert.Contains("\"primary_emotion\":\"fear\"", debugInstruction, StringComparison.Ordinal);
            Assert.Contains("\"regulatory_state\":\"controlled\"", debugInstruction, StringComparison.Ordinal);
            Assert.DoesNotContain("Response posture:", debugInstruction, StringComparison.Ordinal);
            Assert.Contains("\"text\":\"visible delivered line\"", debugInstruction, StringComparison.Ordinal);
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
                        "{trajectory}",
                        "trajectory",
                        StringComparison.Ordinal));

                var catalog = PromptCatalog.LoadFromDirectory(root);
                var error = Assert.Throws<InvalidOperationException>(() => catalog.ValidateRuntimeCatalog());

                Assert.Contains("emotional-reaction-performance-direction", error.Message, StringComparison.Ordinal);
                Assert.Contains("{trajectory}", error.Message, StringComparison.Ordinal);
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
        [InlineData(2, InterestState.Bored, RollOutcomeIntensity.Catastrophe, "hurt", "pulls the reply back behind a clean boundary")]
        [InlineData(21, InterestState.AlmostThere, RollOutcomeIntensity.Catastrophe, "hurt", "answers with warmth bruised by disbelief")]
        [InlineData(2, InterestState.Bored, RollOutcomeIntensity.Strong, "curiosity", "lets one guarded question through")]
        [InlineData(21, InterestState.AlmostThere, RollOutcomeIntensity.Strong, "relief", "lets the reply become unmistakably warmer")]
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

            Assert.Contains("\"primary_emotion\":\"" + primaryEmotion + "\"", prompt, StringComparison.Ordinal);
            Assert.Contains("\"schema_version\":\"datee_response_plan.v1\"", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("Response posture: " + posture, prompt, StringComparison.Ordinal);
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
            RollOutcomeIntensity outcome = RollOutcomeIntensity.Strong,
            OwnedPromptFactV1? cognitiveSubtextFact = null,
            DateeReactionTarget? reactionTarget = null)
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
                    TestHelpers.MakePsychiatricDiagnosis()),
                dateeReactionTarget: reactionTarget,
                cognitiveSubtextFact: cognitiveSubtextFact,
                recipientCharacterId: reactionTarget != null || cognitiveSubtextFact != null ? DateeId : (Guid?)null);
        }

        private static DateeReactionTarget CuratedBufferTarget()
        {
            return DateeReactionTarget.FromLegacyResolvedTarget(
                new ResolvedRevelationTarget
                {
                    Registry = EmotionStemSelectionRules.StakeRegistry,
                    Index = 3,
                    Field = "STAKE_LINE",
                    Manner = "CURATED_BUFFER",
                    StemText = "admit the move was difficult",
                    TransitionStyle = "buffered disclosure",
                },
                DateeId,
                DateeId,
                ConversationParticipantRole.Datee,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceIds.PsychologicalStake(DateeId, 3));
        }

        private static OwnedPromptFactV1 CognitivePressure()
        {
            return new OwnedPromptFactV1(
                DateeId,
                ConversationParticipantRole.Datee,
                PromptFactVisibility.PrivateToSubject,
                PromptFactSourceKind.CognitiveSubtext,
                PromptFactSourceIds.CognitiveSubtext(DateeId, 7),
                "ABANDONMENT + DEFLECTION");
        }

        private static DateeResponsePlan CompilePlan(DateeContext context, CharacterEmotionalDirection direction)
        {
            DateeResponsePlanCompilationResult result = new DateeResponsePlanCompiler().Compile(
                DateeResponsePlanInput.From(context, direction));
            Assert.Equal(DateeResponsePlanCompilationOutcome.Accepted, result.Outcome);
            return Assert.IsType<DateeResponsePlan>(result.Plan);
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

        private static CharacterEmotionalDirection ValidDirection(
            string? primaryEmotion = null,
            string? secondaryEmotion = null,
            string? regulatoryState = null,
            int activation = 4,
            string? trajectory = null,
            string? coreThreatOrDesire = null,
            string? interpretation = null,
            string? impulse = null,
            string? restraint = null,
            string? responsePosture = null)
        {
            return new CharacterEmotionalDirection(
                primaryEmotion ?? "relief",
                secondaryEmotion ?? CharacterEmotionalDirection.NoneSecondaryEmotion,
                regulatoryState ?? "controlled",
                activation,
                trajectory ?? "escalating",
                coreThreatOrDesire ?? "fear of being dismissed",
                interpretation ?? "reads the message as specific warmth that is probably meant for them",
                impulse ?? "leans in with a careful question",
                restraint ?? "keeps the reply tentative but available",
                responsePosture ?? "turns warmer while still checking sincerity");
        }

        private static string ValidDirectionJson(
            string? primaryEmotion = null,
            string? secondaryEmotion = null,
            string? regulatoryState = null,
            int activation = 4,
            string? trajectory = null,
            string? coreThreatOrDesire = null,
            string? interpretation = null,
            string? impulse = null,
            string? restraint = null,
            string? responsePosture = null)
        {
            var direction = ValidDirection(
                primaryEmotion,
                secondaryEmotion,
                regulatoryState,
                activation,
                trajectory,
                coreThreatOrDesire,
                interpretation,
                impulse,
                restraint,
                responsePosture);
            return new JObject
            {
                ["schema_version"] = CharacterEmotionalDirectionContract.SchemaVersion,
                ["primary_emotion"] = direction.PrimaryEmotion,
                ["secondary_emotion"] = direction.SecondaryEmotion,
                ["regulatory_state"] = direction.RegulatoryState,
                ["activation"] = direction.Activation,
                ["trajectory"] = direction.Trajectory,
                ["core_threat_or_desire"] = direction.CoreThreatOrDesire,
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

        private sealed class RecordingTransport : ILlmTransport, IStructuredLlmTransport, IStructuredConversationLlmTransport
        {
            private readonly Queue<string> _responses;

            public RecordingTransport(params string[] responses)
            {
                _responses = new Queue<string>(responses);
            }

            public int PlainCalls { get; private set; }
            public int StructuredCalls { get; private set; }
            public bool SupportsStructuredConversationMessages => true;
            public List<string?> Phases { get; } = new List<string?>();
            public List<string> UserMessages { get; } = new List<string>();
            public List<StructuredLlmRequest> StructuredRequests { get; } = new List<StructuredLlmRequest>();

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                PlainCalls++;
                Phases.Add(phase);
                UserMessages.Add(userMessage);
                return Task.FromResult(_responses.Dequeue());
            }

            public Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                return SendStructuredCore(request, ct);
            }

            public Task<StructuredLlmResponse> SendStructuredConversationAsync(
                StructuredLlmRequest request,
                IReadOnlyList<ConversationMessage> priorMessages,
                CancellationToken cancellationToken = default)
            {
                return SendStructuredCore(request, cancellationToken);
            }

            private Task<StructuredLlmResponse> SendStructuredCore(
                StructuredLlmRequest request,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                StructuredCalls++;
                Phases.Add(request.Phase);
                UserMessages.Add(request.UserMessage);
                StructuredRequests.Add(request);
                string response = _responses.Dequeue();
                if (request.SchemaName == DateePerformanceStructuredContract.SchemaName)
                {
                    response = PerformanceJson(response);
                }

                return Task.FromResult(new StructuredLlmResponse(
                    response,
                    provider: "test",
                    model: "structured",
                    usedNativeStructuredOutput: true));
            }
        }

        private sealed class CancelOnPhaseTransport : ILlmTransport, IStructuredLlmTransport, IStructuredConversationLlmTransport
        {
            private readonly string _phaseToCancel;

            public CancelOnPhaseTransport(string phaseToCancel)
            {
                _phaseToCancel = phaseToCancel;
            }

            public List<string?> Phases { get; } = new List<string?>();
            public bool SupportsStructuredConversationMessages => true;

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
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

            public Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                return SendStructuredCore(request, ct);
            }

            public Task<StructuredLlmResponse> SendStructuredConversationAsync(
                StructuredLlmRequest request,
                IReadOnlyList<ConversationMessage> priorMessages,
                CancellationToken cancellationToken = default)
            {
                return SendStructuredCore(request, cancellationToken);
            }

            private Task<StructuredLlmResponse> SendStructuredCore(
                StructuredLlmRequest request,
                CancellationToken ct)
            {
                Phases.Add(request.Phase);
                if (string.Equals(request.Phase, _phaseToCancel, StringComparison.Ordinal))
                {
                    throw new OperationCanceledException(ct);
                }

                string response = request.SchemaName == DateePerformanceStructuredContract.SchemaName
                    ? PerformanceJson("visible response")
                    : ValidDirectionJson();
                return Task.FromResult(new StructuredLlmResponse(response, provider: "test", model: "structured"));
            }
        }

        private static string PerformanceJson(string message)
        {
            return new JObject
            {
                ["schema_version"] = DateePerformanceStructuredContract.SchemaVersion,
                ["message"] = message,
                ["signals"] = new JObject
                {
                    ["tell"] = JValue.CreateNull(),
                    ["weakness"] = JValue.CreateNull(),
                },
            }.ToString(Formatting.None);
        }
    }
}
