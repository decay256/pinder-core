using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public sealed class Issue1252_EmotionalReleaseQualificationTests
    {
        private static readonly StatType[] Stats =
        {
            StatType.Charm,
            StatType.Rizz,
            StatType.Honesty,
            StatType.Chaos,
            StatType.Wit,
            StatType.SelfAwareness,
        };

        private static readonly RollOutcomeIntensity[] Outcomes =
        {
            RollOutcomeIntensity.Clean,
            RollOutcomeIntensity.Strong,
            RollOutcomeIntensity.Critical,
            RollOutcomeIntensity.Exceptional,
            RollOutcomeIntensity.Nat20,
            RollOutcomeIntensity.Fumble,
            RollOutcomeIntensity.Misfire,
            RollOutcomeIntensity.TropeTrap,
            RollOutcomeIntensity.Catastrophe,
            RollOutcomeIntensity.Nat1,
        };

        private static readonly IReadOnlyDictionary<StatType, string[]> ExpectedMeaningCharacteristics =
            new Dictionary<StatType, string[]>
            {
                {
                    StatType.Charm,
                    new[]
                    {
                        "easy social timing",
                        "confident social timing",
                        "unusually well judged",
                        "perfectly calibrated",
                        "rare moment of effortless social magic",
                        "almost smooth but slightly mistimed",
                        "trying to be charming while missing your actual temperature",
                        "dating-app charm you have seen before",
                        "social performance turning visibly wrong",
                        "collapse of social timing so complete it exposes the performance",
                    }
                },
                {
                    StatType.Rizz,
                    new[]
                    {
                        "mild romantic confidence",
                        "attractive directness that reads mutual rather than needy",
                        "desire with unusually good timing",
                        "magnetic attention that makes desire feel specific to you",
                        "rare flirtation that makes attraction feel inevitable without force",
                        "almost flirty but a little exposed",
                        "desire arriving before enough trust",
                        "canned flirtation rather than real attraction",
                        "physical pressure badly misreading you",
                        "total collapse of erotic judgment",
                    }
                },
                {
                    StatType.Honesty,
                    new[]
                    {
                        "small truth offered without drama",
                        "clear truth with emotional spine",
                        "well-placed truth that respects both people",
                        "rare honest moment that is precise, kind, and unhidden",
                        "truth arriving with perfect courage and care",
                        "truth with a slightly awkward edge",
                        "candor that centers their relief more than your experience",
                        "performed vulnerability",
                        "raw disclosure dropped without consent or care",
                        "collapse of trust",
                    }
                },
                {
                    StatType.Chaos,
                    new[]
                    {
                        "small surprising turn that keeps things alive",
                        "bold spontaneity that understands your edge",
                        "brave swerve that opens a new emotional door",
                        "perfectly judged disruption",
                        "rare chaos that reveals courage instead of instability",
                        "a little too random, though not fatal",
                        "unpredictability that misses your emotional footing",
                        "forced quirkiness",
                        "disruption that breaks the shared frame",
                        "collapse into incoherence or self-indulgent volatility",
                    }
                },
                {
                    StatType.Wit,
                    new[]
                    {
                        "light joke or sharp observation that lands",
                        "genuinely funny and attentive",
                        "humor that notices something real without flattening it",
                        "brilliant timing",
                        "rare joke that makes you feel deeply seen",
                        "joke that almost lands but slightly clips the edge",
                        "humor that dodges the emotional point",
                        "familiar banter armor",
                        "joke that wounds, trivializes, or badly misreads the moment",
                        "collapse of comic judgment so bad it changes the room",
                    }
                },
                {
                    StatType.SelfAwareness,
                    new[]
                    {
                        "modest self-understanding that leaves space for you",
                        "reflective clarity with emotional responsibility",
                        "self-knowledge that actually changes the exchange",
                        "unusually mature self-recognition",
                        "rare self-awareness that removes pressure instead of adding it",
                        "self-reflection with a slight over-explained edge",
                        "analysis replacing contact",
                        "therapy-speak used as personality",
                        "self-analysis that becomes self-absorption",
                        "collapse into clinical self-display or defensive insight",
                    }
                },
            };

        static Issue1252_EmotionalReleaseQualificationTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        public void DeterministicMatrix_CoversSixStatsTenOutcomesAndCanonicalRelationshipTransitions()
        {
            PromptCatalog catalog = BuiltInCatalog();
            var compiler = new EmotionalReactionEventCompiler(catalog);
            var observedStats = new HashSet<StatType>();
            var observedOutcomes = new HashSet<RollOutcomeIntensity>();
            var observedTransitions = new HashSet<string>(StringComparer.Ordinal);
            var scenarioIds = new HashSet<string>(StringComparer.Ordinal);
            var meaningTextsByStat = Stats.ToDictionary(
                stat => stat,
                _ => new HashSet<string>(StringComparer.Ordinal));
            int scenarioIndex = 0;

            foreach (StatType stat in Stats)
            {
                foreach (RollOutcomeIntensity outcome in Outcomes)
                {
                    scenarioIndex++;
                    Boundary boundary = BoundaryFor(outcome);
                    string scenarioId = "ERQ-MX-" + StatKey(stat) + "-" + RollOutcomeIntensityContract.ToKey(outcome);
                    string fixtureId = "ERQMX" + scenarioIndex.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
                    DateeContext context = MakeContext(
                        fixtureId,
                        stat,
                        outcome,
                        boundary.InterestBefore,
                        boundary.InterestAfter,
                        boundary.BeforeState,
                        boundary.AfterState,
                        MakeCompleteDiagnosis(fixtureId));

                    PromptTraceResult trace = compiler.Compile(context);
                    string outcomeKey = RollOutcomeIntensityContract.ToKey(outcome);
                    string eventKey = EmotionalReactionPromptCatalog.GetEventMeaningKey(stat, outcomeKey);
                    string transition = EmotionalReactionPromptCatalog.GetRelationshipTransitionKey(
                        context.InterestBeforeState,
                        context.InterestAfterState);
                    PromptEntry catalogEntry = catalog.Get(eventKey);
                    AnnotatedSpan eventSpan = Assert.Single(trace.Spans.Where(span => span.Key == eventKey));
                    string eventMeaningText = trace.Text.Substring(eventSpan.Start, eventSpan.Length);
                    string expectedCharacteristic =
                        ExpectedMeaningCharacteristics[stat][Array.IndexOf(Outcomes, outcome)];

                    Assert.True(scenarioIds.Add(scenarioId));
                    observedStats.Add(stat);
                    observedOutcomes.Add(outcome);
                    observedTransitions.Add(transition);
                    Assert.Equal("data/prompts/emotional-reactions.yaml", eventSpan.SourceFile);
                    Assert.Equal(catalogEntry.SystemPrompt, eventMeaningText);
                    Assert.Contains(expectedCharacteristic, eventMeaningText, StringComparison.OrdinalIgnoreCase);
                    Assert.True(
                        meaningTextsByStat[stat].Add(eventMeaningText),
                        "Each outcome must have a distinct ordinary-language meaning for " + stat + ".");
                    Assert.Contains(trace.Spans, span => span.Key == "PlayerDeliveredMessage");
                    Assert.Contains("delivered player line for " + fixtureId, trace.Text, StringComparison.Ordinal);
                    Assert.Contains("derived feeling " + fixtureId, trace.Text, StringComparison.Ordinal);
                    Assert.Contains(StatReactionFragment(stat, fixtureId), trace.Text, StringComparison.Ordinal);
                    if (RollOutcomeIntensityContract.IsSuccess(outcome))
                    {
                        Assert.Contains("safe connection " + fixtureId, trace.Text, StringComparison.Ordinal);
                        Assert.DoesNotContain("hurt protection " + fixtureId, trace.Text, StringComparison.Ordinal);
                    }
                    else
                    {
                        Assert.Contains("hurt protection " + fixtureId, trace.Text, StringComparison.Ordinal);
                        Assert.Contains("repair requirement " + fixtureId, trace.Text, StringComparison.Ordinal);
                    }

                    AssertNoPrivateCompilerShorthand(trace.Text);
                }
            }

            Assert.Equal(60, scenarioIds.Count);
            Assert.Equal(Stats.OrderBy(value => value).ToArray(), observedStats.OrderBy(value => value).ToArray());
            Assert.Equal(Outcomes.OrderBy(value => value).ToArray(), observedOutcomes.OrderBy(value => value).ToArray());
            Assert.Contains("preserved", observedTransitions);
            Assert.Contains("strengthened", observedTransitions);
            Assert.Contains("damaged", observedTransitions);
            Assert.Contains("transformed", observedTransitions);
            Assert.All(Stats, stat => Assert.Equal(10, meaningTextsByStat[stat].Count));
        }

        [Fact]
        public void RelationshipBoundaries_ProveCanonical15And16AndHonorExplicitBespokeStates()
        {
            var compiler = new EmotionalReactionEventCompiler(BuiltInCatalog());

            PromptTraceResult canonical15 = compiler.Compile(
                MakeContext("ERQ-BND-15", StatType.Honesty, RollOutcomeIntensity.Clean, 15, 15));
            Assert.Contains(canonical15.Spans, span => span.Key == "emotional-reaction-interest-interested");
            Assert.DoesNotContain(canonical15.Spans, span => span.Key == "emotional-reaction-interest-very-into-it");
            AssertNoPrivateCompilerShorthand(canonical15.Text);

            PromptTraceResult canonical16 = compiler.Compile(
                MakeContext("ERQ-BND-16", StatType.Honesty, RollOutcomeIntensity.Clean, 16, 16));
            Assert.Contains(canonical16.Spans, span => span.Key == "emotional-reaction-interest-very-into-it");
            Assert.DoesNotContain(canonical16.Spans, span => span.Key == "emotional-reaction-interest-interested");
            AssertNoPrivateCompilerShorthand(canonical16.Text);

            PromptTraceResult bespoke15AsVeryIntoIt = compiler.Compile(
                MakeContext(
                    "ERQ-BND-BESPOKE-15",
                    StatType.Honesty,
                    RollOutcomeIntensity.Clean,
                    15,
                    15,
                    InterestState.VeryIntoIt,
                    InterestState.VeryIntoIt));
            Assert.Contains(bespoke15AsVeryIntoIt.Spans, span => span.Key == "emotional-reaction-interest-very-into-it");
            Assert.DoesNotContain(
                bespoke15AsVeryIntoIt.Spans,
                span => span.Key == "emotional-reaction-interest-interested");
            AssertNoPrivateCompilerShorthand(bespoke15AsVeryIntoIt.Text);
        }

        [Fact]
        public async Task CompleteAndLegacyCharacters_CompletePassesAndLegacyFailsClosedBeforeProviderCalls()
        {
            var completeTransport = new QualificationTransport(
                ValidDirectionJson(),
                VisibleQualifiedReply);
            var completeAdapter = CreateAdapter(completeTransport);

            StatefulDateeResult result = await completeAdapter.GetDateeResponseAsync(
                MakeContext(
                    "ERQ-CHAR-COMPLETE",
                    StatType.Rizz,
                    RollOutcomeIntensity.Strong,
                    15,
                    16,
                    diagnosis: MakeCompleteDiagnosis("ERQ-CHAR-COMPLETE")),
                Array.Empty<ConversationMessage>());

            Assert.Equal(new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse }, completeTransport.Phases.ToArray());
            Assert.Equal(VisibleQualifiedMessageOnly, result.Response.MessageText);
            Assert.NotNull(result.Response.DetectedTell);
            Assert.Equal(StatType.Honesty, result.Response.DetectedTell!.Stat);
            Assert.NotNull(result.Response.WeaknessWindow);
            Assert.Equal(StatType.SelfAwareness, result.Response.WeaknessWindow!.DefendingStat);
            Assert.Equal(2, result.Response.WeaknessWindow.DcReduction);
            Assert.DoesNotContain("psychiatric", result.Response.MessageText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("derived_feeling", result.Response.MessageText, StringComparison.Ordinal);
            Assert.DoesNotContain("DATEE EMOTIONAL PERFORMANCE DIRECTION", result.Response.MessageText, StringComparison.Ordinal);

            var legacyDiagnosis = MakeCompleteDiagnosis("ERQ-CHAR-LEGACY")
                .Where(pair => pair.Key != TherapistDiagnosisContract.SafeConnectionKey)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var legacyTransport = new QualificationTransport(
                ValidDirectionJson(),
                VisibleQualifiedReply);
            var legacyAdapter = CreateAdapter(legacyTransport);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => legacyAdapter.GetDateeResponseAsync(
                    MakeContext(
                        "ERQ-CHAR-LEGACY",
                        StatType.Rizz,
                        RollOutcomeIntensity.Strong,
                        15,
                        16,
                        diagnosis: legacyDiagnosis),
                    Array.Empty<ConversationMessage>()));

            Assert.Contains("invalid therapist diagnosis", ex.Message, StringComparison.Ordinal);
            Assert.Empty(legacyTransport.Phases);
        }

        [Fact]
        public async Task ProviderNeutralStructuredTransport_RoutesDirectionAndVisibleSignals()
        {
            const string scenarioId = "ERQ-XPROV-STRUCTURED";
            var transport = new QualificationTransport(
                ValidDirectionJson(primaryEmotion: "pride"),
                VisibleQualifiedReply);
            var adapter = CreateAdapter(transport);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeContext(
                    scenarioId,
                    StatType.Chaos,
                    RollOutcomeIntensity.Catastrophe,
                    16,
                    15,
                    InterestState.VeryIntoIt,
                    InterestState.Interested),
                Array.Empty<ConversationMessage>());

            Assert.Equal(2, transport.StructuredCalls);
            Assert.Equal(0, transport.PlainCalls);
            Assert.Equal(LlmPhase.OpponentResponse, transport.LastStructuredRequest!.Phase);

            Assert.Equal(new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse }, transport.Phases.ToArray());
            Assert.Contains("[ENGINE — DATEE RESPONSE PLAN]", transport.PerformanceUserMessage, StringComparison.Ordinal);
            Assert.Contains("\"primary_emotion\":\"pride\"", transport.PerformanceUserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("Private emotional director source packet", transport.PerformanceUserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("Character-specific emotional translation", transport.PerformanceUserMessage, StringComparison.Ordinal);
            Assert.Contains("QUALIFICATION COMPLETE CHARACTER VOICE " + scenarioId, transport.PerformanceSystemPrompt, StringComparison.Ordinal);
            Assert.Equal(VisibleQualifiedMessageOnly, result.NewHistoryEntries[1].Content);
            Assert.NotNull(result.Response.DetectedTell);
            Assert.NotNull(result.Response.WeaknessWindow);
            Assert.DoesNotContain("[SIGNALS]", result.Response.MessageText, StringComparison.Ordinal);
            Assert.DoesNotContain("DATEE EMOTIONAL PERFORMANCE DIRECTION", result.Response.MessageText, StringComparison.Ordinal);
        }

        [Fact]
        public async Task PerformanceInputWiring_DistinctVoiceAndDirectionDriveDistinctScriptedReplies()
        {
            const string restrainedVoice =
                "QUALIFICATION VOICE RESTRAINED: lowercase, dry, and unwilling to over-explain.";
            const string expressiveVoice =
                "QUALIFICATION VOICE EXPRESSIVE: candid, buoyant, and emotionally explicit.";
            const string restrainedEmotion = "amusement";
            const string expressiveEmotion = "joy";
            const string restrainedReply = "maybe. that was better than I expected.";
            const string expressiveReply = "Okay, that genuinely delighted me. Tell me more.";

            var restrainedTransport = new InputConditionedQualificationTransport(
                ValidDirectionJson(primaryEmotion: restrainedEmotion),
                restrainedVoice,
                restrainedEmotion,
                restrainedReply);
            var expressiveTransport = new InputConditionedQualificationTransport(
                ValidDirectionJson(primaryEmotion: expressiveEmotion),
                expressiveVoice,
                expressiveEmotion,
                expressiveReply);

            StatefulDateeResult restrained = await CreateAdapter(restrainedTransport).GetDateeResponseAsync(
                MakeContext(
                    "ERQ-INPUT-RESTRAINED",
                    StatType.Wit,
                    RollOutcomeIntensity.Strong,
                    15,
                    16,
                    dateePrompt: restrainedVoice),
                Array.Empty<ConversationMessage>());
            StatefulDateeResult expressive = await CreateAdapter(expressiveTransport).GetDateeResponseAsync(
                MakeContext(
                    "ERQ-INPUT-EXPRESSIVE",
                    StatType.Wit,
                    RollOutcomeIntensity.Strong,
                    15,
                    16,
                    dateePrompt: expressiveVoice),
                Array.Empty<ConversationMessage>());

            Assert.Contains(restrainedVoice, restrainedTransport.PerformanceSystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain(expressiveVoice, restrainedTransport.PerformanceSystemPrompt, StringComparison.Ordinal);
            Assert.Contains("\"primary_emotion\":\"" + restrainedEmotion + "\"", restrainedTransport.PerformanceUserMessage, StringComparison.Ordinal);
            Assert.Contains(expressiveVoice, expressiveTransport.PerformanceSystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain(restrainedVoice, expressiveTransport.PerformanceSystemPrompt, StringComparison.Ordinal);
            Assert.Contains("\"primary_emotion\":\"" + expressiveEmotion + "\"", expressiveTransport.PerformanceUserMessage, StringComparison.Ordinal);
            Assert.Equal(restrainedReply, restrained.Response.MessageText);
            Assert.Equal(expressiveReply, expressive.Response.MessageText);
            Assert.NotEqual(restrained.Response.MessageText, expressive.Response.MessageText);
        }

        [Fact]
        public async Task RetryQualification_DoesNotDuplicateHistoryOrPersistPrivateArtifacts()
        {
            const string privateSentinel = "PRIVATE-ERQ-RETRY-SENTINEL";
            var violations = new List<LlmContractViolation>();
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var transport = new QualificationTransport(
                "not json",
                ValidDirectionJson(impulse: "leans in while still checking the edge"),
                "This should not ship.\nPrimary emotion: " + privateSentinel,
                VisibleQualifiedReply);
            var adapter = CreateAdapter(
                transport,
                retries: 1,
                onViolation: violations.Add,
                diagnostics: diagnostics.Add);

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                MakeContext("ERQ-RETRY", StatType.SelfAwareness, RollOutcomeIntensity.Nat1, 21, 16),
                Array.Empty<ConversationMessage>());

            Assert.Equal(
                new[]
                {
                    LlmPhase.EmotionalDirector,
                    LlmPhase.EmotionalDirector,
                    LlmPhase.OpponentResponse,
                    LlmPhase.OpponentResponse,
                },
                transport.Phases.ToArray());
            Assert.Equal(2, result.NewHistoryEntries.Count);
            Assert.Equal(ConversationMessage.UserRole, result.NewHistoryEntries[0].Role);
            Assert.Equal("delivered player line for ERQ-RETRY", result.NewHistoryEntries[0].Content);
            Assert.Equal(ConversationMessage.AssistantRole, result.NewHistoryEntries[1].Role);
            Assert.Equal(VisibleQualifiedMessageOnly, result.NewHistoryEntries[1].Content);
            Assert.NotNull(result.Response.DetectedTell);
            Assert.NotNull(result.Response.WeaknessWindow);
            Assert.Contains(violations, violation => violation.Reason == "no_json_object");
            Assert.Contains(violations, violation => violation.Reason == "private_direction_leak");
            Assert.All(violations, violation =>
            {
                Assert.DoesNotContain(privateSentinel, violation.Reason, StringComparison.Ordinal);
                Assert.DoesNotContain(privateSentinel, violation.Phase, StringComparison.Ordinal);
            });
            Assert.All(diagnostics, diagnostic =>
            {
                Assert.DoesNotContain(privateSentinel, diagnostic.Message, StringComparison.Ordinal);
                foreach (var value in diagnostic.CorrelationHints.Values)
                    Assert.DoesNotContain(privateSentinel, value, StringComparison.Ordinal);
            });
        }

        private static PinderLlmAdapter CreateAdapter(
            ILlmTransport transport,
            int retries = 0,
            Action<LlmContractViolation>? onViolation = null,
            Action<OperationalDiagnosticEvent>? diagnostics = null)
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
                    OnDiagnostic = diagnostics,
                });
        }

        private static DateeContext MakeContext(
            string scenarioId,
            StatType stat,
            RollOutcomeIntensity outcome,
            int interestBefore,
            int interestAfter,
            InterestState? beforeState = null,
            InterestState? afterState = null,
            IReadOnlyDictionary<string, string>? diagnosis = null,
            string? dateePrompt = null)
        {
            return new DateeContext(
                dateePrompt: dateePrompt ??
                    "QUALIFICATION COMPLETE CHARACTER VOICE " + scenarioId + ": terse, warm, exact.",
                conversationHistory: new[]
                {
                    ("Player", "visible setup line for " + scenarioId),
                    ("Datee", "visible datee setup line for " + scenarioId),
                },
                dateeLastMessage: "visible datee setup line for " + scenarioId,
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: "delivered player line for " + scenarioId,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 7,
                interestBeforeState: beforeState,
                interestAfterState: afterState,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    stat,
                    outcome,
                    diagnosis ?? MakeCompleteDiagnosis(scenarioId)));
        }

        private static Dictionary<string, string> MakeCompleteDiagnosis(string scenarioId)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { TherapistDiagnosisContract.DerivedFeelingKey, "derived feeling " + scenarioId },
                { TherapistDiagnosisContract.DefenseReactionKey, "defense reaction " + scenarioId },
                { TherapistDiagnosisContract.SafeConnectionKey, "safe connection " + scenarioId },
                { TherapistDiagnosisContract.HurtProtectionKey, "hurt protection " + scenarioId },
                { TherapistDiagnosisContract.RepairRequirementKey, "repair requirement " + scenarioId },
                { TherapistDiagnosisContract.CharmReactionKey, "charm reaction " + scenarioId },
                { TherapistDiagnosisContract.RizzReactionKey, "rizz reaction " + scenarioId },
                { TherapistDiagnosisContract.HonestyReactionKey, "honesty reaction " + scenarioId },
                { TherapistDiagnosisContract.ChaosReactionKey, "chaos reaction " + scenarioId },
                { TherapistDiagnosisContract.WitReactionKey, "wit reaction " + scenarioId },
                { TherapistDiagnosisContract.SelfAwarenessReactionKey, "self awareness reaction " + scenarioId },
            };
        }

        private static string StatReactionFragment(StatType stat, string scenarioId)
        {
            switch (stat)
            {
                case StatType.Charm:
                    return "charm reaction " + scenarioId;
                case StatType.Rizz:
                    return "rizz reaction " + scenarioId;
                case StatType.Honesty:
                    return "honesty reaction " + scenarioId;
                case StatType.Chaos:
                    return "chaos reaction " + scenarioId;
                case StatType.Wit:
                    return "wit reaction " + scenarioId;
                case StatType.SelfAwareness:
                    return "self awareness reaction " + scenarioId;
                default:
                    throw new InvalidOperationException("Unknown stat.");
            }
        }

        private static Boundary BoundaryFor(RollOutcomeIntensity outcome)
        {
            switch (outcome)
            {
                case RollOutcomeIntensity.Clean:
                    return new Boundary(15, 15, null, null);
                case RollOutcomeIntensity.Strong:
                    return new Boundary(15, 16, null, null);
                case RollOutcomeIntensity.Critical:
                    return new Boundary(8, 12, null, null);
                case RollOutcomeIntensity.Exceptional:
                    return new Boundary(20, 25, null, null);
                case RollOutcomeIntensity.Nat20:
                    return new Boundary(24, 25, null, null);
                case RollOutcomeIntensity.Fumble:
                    return new Boundary(16, 15, null, null);
                case RollOutcomeIntensity.Misfire:
                    return new Boundary(12, 8, null, null);
                case RollOutcomeIntensity.TropeTrap:
                    return new Boundary(2, 2, null, null);
                case RollOutcomeIntensity.Catastrophe:
                    return new Boundary(22, 18, null, null);
                case RollOutcomeIntensity.Nat1:
                    return new Boundary(1, 0, null, null);
                default:
                    throw new InvalidOperationException("Unknown outcome.");
            }
        }

        private static void AssertNoPrivateCompilerShorthand(string text)
        {
            string[] forbidden =
            {
                "InterestBefore",
                "InterestAfter",
                "StatType",
                "RollOutcomeIntensity",
                "nat1",
                "nat20",
                "trope_trap",
                "catastrophe",
                "advantage",
                "DC",
            };

            foreach (string value in forbidden)
                Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotMatch(@"\b\d+\s*(?:/|-|to)\s*\d+\b", text);
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
                string candidate = System.IO.Path.Combine(dir, "data", "prompts");
                if (System.IO.Directory.Exists(candidate))
                    return candidate;
                dir = System.IO.Path.GetDirectoryName(dir);
            }

            throw new System.IO.DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }

        private static string ValidDirectionJson(
            string? primaryEmotion = null,
            string? impulse = null)
        {
            return new JObject
            {
                ["schema_version"] = CharacterEmotionalDirectionContract.SchemaVersion,
                ["primary_emotion"] = primaryEmotion ?? "relief",
                ["secondary_emotion"] = CharacterEmotionalDirection.NoneSecondaryEmotion,
                ["regulatory_state"] = "controlled",
                ["activation"] = 4,
                ["trajectory"] = "escalating",
                ["core_threat_or_desire"] = "fear of being dismissed",
                ["interpretation"] = "reads the message as specific warmth that is probably meant for them",
                ["impulse"] = impulse ?? "leans in with a careful question",
                ["restraint"] = "keeps the reply tentative but available",
                ["response_posture"] = "Writing from " + (primaryEmotion ?? "relief") + ", turns warmer while still checking sincerity",
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string StatKey(StatType stat)
        {
            return stat == StatType.SelfAwareness
                ? "self-awareness"
                : stat.ToString().ToLowerInvariant();
        }

        private const string VisibleQualifiedMessageOnly =
            "that actually lands softer than I expected, and I am still checking the edges.";

        private const string VisibleQualifiedReply =
            "{\"schema_version\":\"datee_performance.v1\",\"message\":\"" + VisibleQualifiedMessageOnly +
            "\",\"signals\":{\"tell\":{\"stat\":\"HONESTY\",\"description\":\"asks directly whether the warmth is real\"}," +
            "\"weakness\":{\"defending_stat\":\"SELF_AWARENESS\",\"dc_reduction\":2,\"description\":\"lets the guard drop for a second\"}}}";

        private readonly struct Boundary
        {
            public Boundary(
                int interestBefore,
                int interestAfter,
                InterestState? beforeState,
                InterestState? afterState)
            {
                InterestBefore = interestBefore;
                InterestAfter = interestAfter;
                BeforeState = beforeState;
                AfterState = afterState;
            }

            public int InterestBefore { get; }

            public int InterestAfter { get; }

            public InterestState? BeforeState { get; }

            public InterestState? AfterState { get; }
        }

        private class QualificationTransport : ILlmTransport, IStructuredLlmTransport
        {
            protected readonly Queue<string> Responses;

            public QualificationTransport(params string[] responses)
            {
                Responses = new Queue<string>(responses);
            }

            public int PlainCalls { get; private set; }

            public int StructuredCalls { get; protected set; }

            public List<string?> Phases { get; } = new List<string?>();

            public StructuredLlmRequest? LastStructuredRequest { get; protected set; }

            public string PerformanceSystemPrompt { get; private set; } = string.Empty;

            public string PerformanceUserMessage { get; private set; } = string.Empty;

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
                if (string.Equals(phase, LlmPhase.OpponentResponse, StringComparison.Ordinal))
                {
                    PerformanceSystemPrompt = systemPrompt;
                    PerformanceUserMessage = userMessage;
                }

                return Task.FromResult(Responses.Dequeue());
            }

            public virtual Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                StructuredCalls++;
                Phases.Add(request.Phase);
                LastStructuredRequest = request;
                if (string.Equals(request.Phase, LlmPhase.OpponentResponse, StringComparison.Ordinal))
                {
                    PerformanceSystemPrompt = request.SystemPrompt;
                    PerformanceUserMessage = request.UserMessage;
                }

                return Task.FromResult(DateePromptTestBuilder.StructuredResponse(
                    request,
                    Responses.Dequeue(),
                    "structured-mock"));
            }
        }

        private sealed class StructuredQualificationTransport : QualificationTransport, IStructuredLlmTransport
        {
            public StructuredQualificationTransport(params string[] responses)
                : base(responses)
            {
            }

            public override Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                StructuredCalls++;
                Phases.Add(request.Phase);
                LastStructuredRequest = request;
                return Task.FromResult(DateePromptTestBuilder.StructuredResponse(
                    request,
                    Responses.Dequeue(),
                    "structured-mock"));
            }
        }

        private sealed class InputConditionedQualificationTransport : ILlmTransport, IStructuredLlmTransport
        {
            private readonly string _directorResponse;
            private readonly string _requiredVoice;
            private readonly string _requiredEmotion;
            private readonly string _visibleReply;

            public InputConditionedQualificationTransport(
                string directorResponse,
                string requiredVoice,
                string requiredEmotion,
                string visibleReply)
            {
                _directorResponse = directorResponse;
                _requiredVoice = requiredVoice;
                _requiredEmotion = requiredEmotion;
                _visibleReply = visibleReply;
            }

            public string PerformanceSystemPrompt { get; private set; } = string.Empty;

            public string PerformanceUserMessage { get; private set; } = string.Empty;

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(phase, LlmPhase.EmotionalDirector, StringComparison.Ordinal))
                    return Task.FromResult(_directorResponse);

                Assert.Equal(LlmPhase.OpponentResponse, phase);
                PerformanceSystemPrompt = systemPrompt;
                PerformanceUserMessage = userMessage;
                Assert.Contains(_requiredVoice, systemPrompt, StringComparison.Ordinal);
                Assert.Contains("\"primary_emotion\":\"" + _requiredEmotion + "\"", userMessage, StringComparison.Ordinal);
                return Task.FromResult(_visibleReply);
            }

            public async Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                string response = await SendAsync(
                    request.SystemPrompt,
                    request.UserMessage,
                    request.Temperature,
                    request.MaxTokens,
                    request.Phase,
                    ct).ConfigureAwait(false);
                return DateePromptTestBuilder.StructuredResponse(request, response);
            }
        }
    }
}
