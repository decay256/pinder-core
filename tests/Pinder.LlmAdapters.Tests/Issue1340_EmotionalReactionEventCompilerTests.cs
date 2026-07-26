using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public sealed class Issue1340_EmotionalReactionEventCompilerTests
    {
        private static readonly InterestState[] InterestStates =
        {
            InterestState.Unmatched,
            InterestState.Bored,
            InterestState.Lukewarm,
            InterestState.Interested,
            InterestState.VeryIntoIt,
            InterestState.AlmostThere,
            InterestState.DateSecured,
        };

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

        [Fact]
        public void Compile_CoversFullStatOutcomeMatrixWithExactEventTraceKeys()
        {
            var catalog = BuiltInCatalog();
            var compiler = new EmotionalReactionEventCompiler();

            foreach (StatType stat in Stats)
            {
                foreach (RollOutcomeIntensity outcome in Outcomes)
                {
                    var trace = compiler.Compile(
                        MakeContext(
                            stat: stat,
                            outcome: outcome,
                            deliveredMessage: "degraded delivered text"));
                    string outcomeKey = RollOutcomeIntensityContract.ToKey(outcome);
                    string eventKey = EmotionalReactionPromptCatalog.GetEventMeaningKey(stat, outcomeKey);

                    Assert.Contains(
                        catalog.Get(eventKey).SystemPrompt!,
                        trace.Text,
                        StringComparison.Ordinal);
                    Assert.Contains(
                        trace.Spans,
                        span => span.SourceFile == "data/prompts/emotional-reactions.yaml" && span.Key == eventKey);
                    Assert.DoesNotContain("{", trace.Text, StringComparison.Ordinal);
                    Assert.Contains("degraded delivered text", trace.Text, StringComparison.Ordinal);
                }
            }
        }

        [Fact]
        public void Compile_CoversFullRelationshipMatrixAndAllTransitionClasses()
        {
            var compiler = new EmotionalReactionEventCompiler();
            var seenTransitions = new HashSet<string>(StringComparer.Ordinal);

            foreach (InterestState before in InterestStates)
            {
                foreach (InterestState after in InterestStates)
                {
                    string transition = EmotionalReactionPromptCatalog.GetRelationshipTransitionKey(before, after);
                    seenTransitions.Add(transition);

                    var trace = compiler.Compile(MakeContext(beforeState: before, afterState: after));
                    string beforeKey = EmotionalReactionPromptCatalog.GetInterestStateMeaningKey(before);
                    string afterKey = EmotionalReactionPromptCatalog.GetInterestStateMeaningKey(after);
                    string transitionKey = EmotionalReactionPromptCatalog.GetRelationshipTransitionInstructionKey(transition);

                    Assert.Contains(trace.Spans, span => span.Key == beforeKey && span.SourceFile == "data/prompts/emotional-reactions.yaml");
                    Assert.Contains(trace.Spans, span => span.Key == afterKey && span.SourceFile == "data/prompts/emotional-reactions.yaml");
                    Assert.Contains(trace.Spans, span => span.Key == transitionKey && span.SourceFile == "data/prompts/emotional-reactions.yaml");
                    Assert.DoesNotContain("{prior_relationship}", trace.Text, StringComparison.Ordinal);
                    Assert.DoesNotContain("{resulting_relationship}", trace.Text, StringComparison.Ordinal);
                }
            }

            Assert.Equal(
                new[] { "damaged", "preserved", "strengthened", "transformed" },
                seenTransitions.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }

        [Theory]
        [InlineData(0, InterestState.Unmatched)]
        [InlineData(1, InterestState.Bored)]
        [InlineData(4, InterestState.Bored)]
        [InlineData(5, InterestState.Lukewarm)]
        [InlineData(9, InterestState.Lukewarm)]
        [InlineData(10, InterestState.Interested)]
        [InlineData(15, InterestState.Interested)]
        [InlineData(16, InterestState.VeryIntoIt)]
        [InlineData(20, InterestState.VeryIntoIt)]
        [InlineData(21, InterestState.AlmostThere)]
        [InlineData(24, InterestState.AlmostThere)]
        [InlineData(25, InterestState.DateSecured)]
        public void Compile_DefaultInterestBoundaries_UseCanonicalRelationshipMeaning(
            int interest,
            InterestState expected)
        {
            var trace = new EmotionalReactionEventCompiler()
                .Compile(MakeContext(interestBefore: interest, interestAfter: interest));

            Assert.Contains(
                trace.Spans,
                span => span.Key == EmotionalReactionPromptCatalog.GetInterestStateMeaningKey(expected));
        }

        [Fact]
        public void Compile_HighInvestmentDownwardMove_DoesNotInventNeverCaredProse()
        {
            var trace = new EmotionalReactionEventCompiler()
                .Compile(MakeContext(beforeState: InterestState.AlmostThere, afterState: InterestState.VeryIntoIt));

            Assert.DoesNotContain("never cared", trace.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(trace.Spans, span => span.Key == "emotional-reaction-transition-damaged");
        }

        [Fact]
        public void Compile_IncludesDeliveredRuntimeMessageAndExcludesPristineOptionSources()
        {
            var trace = new EmotionalReactionEventCompiler()
                .Compile(MakeContext(
                    dateePrompt: "pristine intended option that should never be read",
                    deliveredMessage: "degraded delivered message"));

            Assert.Contains("degraded delivered message", trace.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("pristine intended option", trace.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                trace.Spans,
                span => span.SourceFile == EmotionalReactionEventCompiler.RuntimeSource && span.Key == "PlayerDeliveredMessage");
        }

        [Fact]
        public void Compile_UsesCharacterFieldsWithoutDiagnosisLabels()
        {
            var diagnosis = TestHelpers.MakePsychiatricDiagnosis()
                .ToDictionary(pair => pair.Key, pair => "value for " + pair.Key, StringComparer.Ordinal);
            diagnosis[TherapistDiagnosisContract.DerivedFeelingKey] = "you feel secretly replaceable";
            diagnosis[TherapistDiagnosisContract.DefenseReactionKey] = "you check whether warmth has receipts";
            diagnosis[TherapistDiagnosisContract.HonestyReactionKey] = "you trust candor when it stays accountable";
            diagnosis[TherapistDiagnosisContract.SafeConnectionKey] = "steady closeness lets your shoulders drop";
            diagnosis[TherapistDiagnosisContract.HurtProtectionKey] = "you go cool before admitting hurt";
            diagnosis[TherapistDiagnosisContract.RepairRequirementKey] = "you need the rupture named plainly";

            var success = new EmotionalReactionEventCompiler()
                .Compile(MakeContext(stat: StatType.Honesty, outcome: RollOutcomeIntensity.Strong, diagnosis: diagnosis));
            var failure = new EmotionalReactionEventCompiler()
                .Compile(MakeContext(stat: StatType.Honesty, outcome: RollOutcomeIntensity.Catastrophe, diagnosis: diagnosis));

            Assert.Contains("you feel secretly replaceable", success.Text, StringComparison.Ordinal);
            Assert.Contains("steady closeness lets your shoulders drop", success.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("hurt_protection", success.Text, StringComparison.Ordinal);
            Assert.Contains("you go cool before admitting hurt", failure.Text, StringComparison.Ordinal);
            Assert.Contains("you need the rupture named plainly", failure.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("derived_feeling", failure.Text, StringComparison.Ordinal);
            Assert.Contains(failure.Spans, span => span.SourceFile == "character:psychiatric_diagnosis" && span.Key == TherapistDiagnosisContract.HonestyReactionKey);
        }

        [Fact]
        public void Compile_NestedTraceOffsetsResolveToExactRenderedSubstrings()
        {
            const string delivered = "DELIVERED OFFSET SENTINEL";
            const string derivedFeeling = "DERIVED FEELING OFFSET SENTINEL";
            var diagnosis = TestHelpers.MakePsychiatricDiagnosis()
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            diagnosis[TherapistDiagnosisContract.DerivedFeelingKey] = derivedFeeling;
            var context = MakeContext(
                stat: StatType.Honesty,
                outcome: RollOutcomeIntensity.Strong,
                diagnosis: diagnosis,
                deliveredMessage: delivered);
            var catalog = BuiltInCatalog();

            PromptTraceResult trace = new EmotionalReactionEventCompiler(catalog).Compile(context);
            string eventKey = EmotionalReactionPromptCatalog.GetEventMeaningKey(StatType.Honesty, "strong");
            string relationshipKey = EmotionalReactionPromptCatalog.GetInterestStateMeaningKey(
                context.InterestBeforeState);

            AssertExactSpanSubstring(
                trace,
                EmotionalReactionEventCompiler.RuntimeSource,
                "PlayerDeliveredMessage",
                delivered);
            AssertExactSpanSubstring(
                trace,
                EmotionalReactionEventCompiler.RuntimeSource,
                "ConversationHistory.Text",
                "visible older player text");
            AssertExactSpanSubstring(
                trace,
                "character:psychiatric_diagnosis",
                TherapistDiagnosisContract.DerivedFeelingKey,
                derivedFeeling);
            AssertExactSpanSubstring(
                trace,
                "data/prompts/emotional-reactions.yaml",
                eventKey,
                catalog.Get(eventKey).SystemPrompt!);
            AssertExactSpanSubstring(
                trace,
                "data/prompts/emotional-reactions.yaml",
                relationshipKey,
                catalog.Get(relationshipKey).SystemPrompt!);
            Assert.All(
                trace.Spans,
                span => Assert.InRange(span.End, span.Start, trace.Text.Length));
        }

        [Fact]
        public async Task CurrentPinderLlmAdapter_DoesNotTransportOrPersistPrivateEventOnlySentinel()
        {
            const string privateSentinel = "PRIVATE EVENT ONLY SENTINEL 1340";
            var diagnosis = TestHelpers.MakePsychiatricDiagnosis()
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            diagnosis[TherapistDiagnosisContract.DerivedFeelingKey] = privateSentinel;
            var context = MakeContext(
                diagnosis: diagnosis,
                deliveredMessage: "visible delivered line");
            var transport = new CapturingTransport("A bounded DATEE reply.");
            var adapter = new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    PromptCatalog = BuiltInCatalog(),
                    MaxContractViolationRetries = 0,
                });

            StatefulDateeResult result = await adapter.GetDateeResponseAsync(
                context,
                Array.Empty<ConversationMessage>());

            Assert.NotNull(context.EmotionalTurnEvent);
            Assert.Equal(
                privateSentinel,
                context.EmotionalTurnEvent!.TherapistDiagnosis![TherapistDiagnosisContract.DerivedFeelingKey]);
            Assert.DoesNotContain(privateSentinel, transport.LastSystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain(privateSentinel, transport.LastUserMessage, StringComparison.Ordinal);
            Assert.All(
                result.NewHistoryEntries,
                entry => Assert.DoesNotContain(privateSentinel, entry.Content, StringComparison.Ordinal));
            Assert.Equal("visible delivered line", result.NewHistoryEntries[0].Content);
        }

        [Fact]
        public void Compile_DoesNotRenderRawMechanicsOrOutcomeShorthand()
        {
            var trace = new EmotionalReactionEventCompiler()
                .Compile(MakeContext(stat: StatType.SelfAwareness, outcome: RollOutcomeIntensity.Nat1));

            string[] forbidden =
            {
                "InterestBefore",
                "InterestAfter",
                "FailureTier",
                "StatType",
                "RollOutcomeIntensity",
                "nat1",
                "trope_trap",
                "force",
                "advantage",
            };

            foreach (string value in forbidden)
                Assert.DoesNotContain(value, trace.Text, StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotMatch(@"\b\d+\s*(?:/|-|to)\s*\d+\b", trace.Text);
        }

        [Fact]
        public void Compile_FailsClosedWhenTypedEventOrDiagnosisIsMissingOrBlank()
        {
            var compiler = new EmotionalReactionEventCompiler();

            var noEvent = new DateeContext(
                dateePrompt: "datee",
                conversationHistory: Array.Empty<(string Sender, string Text)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "delivered",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 0);

            Assert.Throws<InvalidOperationException>(() => compiler.Compile(noEvent));

            var missingDiagnosis = new DateeContext(
                dateePrompt: "datee",
                conversationHistory: Array.Empty<(string Sender, string Text)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerDeliveredMessage: "delivered",
                interestBefore: 10,
                interestAfter: 10,
                responseDelayMinutes: 0,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Charm,
                    RollOutcomeIntensity.Clean,
                    null));
            Assert.Throws<InvalidOperationException>(() => compiler.Compile(missingDiagnosis));

            var blankDiagnosis = TestHelpers.MakePsychiatricDiagnosis()
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            blankDiagnosis[TherapistDiagnosisContract.DerivedFeelingKey] = " ";
            Assert.Throws<InvalidOperationException>(() => compiler.Compile(MakeContext(diagnosis: blankDiagnosis)));
        }

        [Fact]
        public void BuildDateePromptEx_DoesNotAppendCompiledPrivateArtifact()
        {
            var diagnosis = TestHelpers.MakePsychiatricDiagnosis()
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            diagnosis[TherapistDiagnosisContract.SafeConnectionKey] = "PRIVATE COMPILED SAFE CONNECTION";
            var context = MakeContext(diagnosis: diagnosis);

            PromptTraceResult compiled = new EmotionalReactionEventCompiler().Compile(context);
            PromptTraceResult visible = SessionDocumentBuilder.BuildDateePromptEx(context, BuiltInCatalog());

            Assert.Contains("PRIVATE COMPILED SAFE CONNECTION", compiled.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATE COMPILED SAFE CONNECTION", visible.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Character-specific emotional translation", visible.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeValidation_RejectsWrapperWithMissingExactPlaceholder()
        {
            string root = CopyPromptsToTemp(FindPromptsRoot());
            try
            {
                string path = Path.Combine(root, "emotional-reactions.yaml");
                File.WriteAllText(
                    path,
                    File.ReadAllText(path).Replace(
                        "{character_formulation}",
                        "character formulation",
                        StringComparison.Ordinal));

                var catalog = PromptCatalog.LoadFromDirectory(root);
                var error = Assert.Throws<InvalidOperationException>(() => catalog.ValidateRuntimeCatalog());

                Assert.Contains("emotional-reaction-compiled-wrapper", error.Message, StringComparison.Ordinal);
                Assert.Contains("{character_formulation}", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static DateeContext MakeContext(
            StatType stat = StatType.Charm,
            RollOutcomeIntensity outcome = RollOutcomeIntensity.Clean,
            IReadOnlyDictionary<string, string>? diagnosis = null,
            string dateePrompt = "datee prompt",
            string deliveredMessage = "delivered message",
            int interestBefore = 10,
            int interestAfter = 10,
            InterestState? beforeState = null,
            InterestState? afterState = null)
        {
            return new DateeContext(
                dateePrompt: dateePrompt,
                conversationHistory: new[]
                {
                    ("Player", "visible older player text"),
                    ("Datee", "visible older datee text"),
                },
                dateeLastMessage: "visible older datee text",
                activeTraps: Array.Empty<string>(),
                currentInterest: interestAfter,
                playerDeliveredMessage: deliveredMessage,
                interestBefore: interestBefore,
                interestAfter: interestAfter,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                interestBeforeState: beforeState,
                interestAfterState: afterState,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    stat,
                    outcome,
                    diagnosis ?? TestHelpers.MakePsychiatricDiagnosis()));
        }

        private static PromptCatalog BuiltInCatalog()
        {
            var catalog = PromptCatalog.LoadFromDirectory(FindPromptsRoot());
            catalog.ValidateRuntimeCatalog();
            return catalog;
        }

        private static void AssertExactSpanSubstring(
            PromptTraceResult trace,
            string sourceFile,
            string key,
            string expected)
        {
            var matching = trace.Spans
                .Where(span => span.SourceFile == sourceFile && span.Key == key)
                .ToArray();

            Assert.NotEmpty(matching);
            Assert.Contains(
                matching,
                span => trace.Text.Substring(span.Start, span.Length) == expected);
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
                "issue1340-prompt-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source, "*.yaml"))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            return destination;
        }

        private sealed class CapturingTransport : ILlmTransport
        {
            private readonly string _response;

            public CapturingTransport(string response)
            {
                _response = response;
            }

            public string LastSystemPrompt { get; private set; } = string.Empty;

            public string LastUserMessage { get; private set; } = string.Empty;

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                LastSystemPrompt = systemPrompt;
                LastUserMessage = userMessage;
                return Task.FromResult(_response);
            }
        }
    }
}
