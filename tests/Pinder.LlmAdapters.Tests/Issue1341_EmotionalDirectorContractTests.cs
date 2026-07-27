using System;
using System.Collections.Generic;
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
    public sealed class Issue1341_EmotionalDirectorContractTests
    {
        static Issue1341_EmotionalDirectorContractTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        public async Task StructuredTransport_SendsDirectorSchemaAndAcceptsValidatedContract()
        {
            var transport = new StructuredQueueTransport(
                new StructuredLlmResponse(ValidJson(), provider: "test", model: "structured", usedNativeStructuredOutput: true));
            var adapter = CreateAdapter(transport);
            var context = MakeContext();
            var compiled = new EmotionalReactionEventCompiler(BuiltInCatalog()).Compile(context);

            var direction = await adapter.GenerateEmotionalDirectionAsync(context);

            Assert.Equal(1, transport.StructuredCalls);
            Assert.Equal("emotional_director", transport.LastStructuredRequest!.SchemaName);
            Assert.Equal("emotional_director.v1", transport.LastStructuredRequest.SchemaVersion);
            Assert.Equal(LlmPhase.EmotionalDirector, transport.LastStructuredRequest.Phase);
            Assert.Equal("emotional_director", transport.LastStructuredRequest.Metadata["phase"]);
            Assert.Equal("data/prompts/emotional-reactions.yaml", transport.LastStructuredRequest.Metadata["system_prompt_source"]);
            Assert.Equal("data/prompts/emotional-reactions.yaml", transport.LastStructuredRequest.Metadata["user_template_source"]);
            var schema = JObject.Parse(transport.LastStructuredRequest.JsonSchema);
            Assert.False(schema.Value<bool>("additionalProperties"));
            Assert.Equal(
                new[]
                {
                    "schema_version",
                    "primary_emotion",
                    "intensity",
                    "underlying_feeling",
                    "interpretation",
                    "impulse",
                    "restraint",
                    "response_posture",
                },
                schema["required"]!.Values<string>());
            Assert.Equal(
                "emotional_director.v1",
                schema["properties"]!["schema_version"]!.Value<string>("const"));
            Assert.All(
                schema["properties"]!
                    .Children<JProperty>()
                    .Where(property => property.Name != "schema_version"),
                property => Assert.Equal(180, property.Value.Value<int>("maxLength")));
            Assert.Equal(
                JoinTraceValues(compiled, span => span.Key),
                transport.LastStructuredRequest.Metadata["compiled_input_keys"]);
            Assert.Equal(
                JoinTraceValues(compiled, span => span.SourceFile),
                transport.LastStructuredRequest.Metadata["compiled_input_sources"]);
            Assert.Contains("visible delivered line", transport.LastStructuredRequest.UserMessage, StringComparison.Ordinal);
            Assert.Contains("each 180 characters or fewer", transport.LastStructuredRequest.SystemPrompt, StringComparison.Ordinal);
            Assert.Equal("relieved but cautious", direction.PrimaryEmotion);
            Assert.Equal("moderate and steadily rising", direction.Intensity);
            Assert.Equal("turns warmer while still checking sincerity", direction.ResponsePosture);
        }

        [Fact]
        public async Task StructuredTransport_NonNativeResponseUsesLocalJsonExtraction()
        {
            var transport = new StructuredQueueTransport(
                new StructuredLlmResponse(
                    "Planner prose\n```json\n" + ValidJson() + "\n```\nEnd prose",
                    provider: "test",
                    model: "structured-fallback",
                    usedNativeStructuredOutput: false));
            var adapter = CreateAdapter(transport);

            var direction = await adapter.GenerateEmotionalDirectionAsync(MakeContext());

            Assert.Equal(1, transport.StructuredCalls);
            Assert.Equal("moderate and steadily rising", direction.Intensity);
            Assert.Equal("relieved but cautious", direction.PrimaryEmotion);
        }

        [Fact]
        public async Task PlainTransport_ExtractsFirstJsonObjectAndUsesPromptEntrySampling()
        {
            var transport = new PlainQueueTransport(
                "Prose before " + ValidJson() + " prose after");
            var adapter = CreateAdapter(transport);
            var catalogEntry = BuiltInCatalog().Get("emotional-reaction-director");

            var direction = await adapter.GenerateEmotionalDirectionAsync(MakeContext());

            Assert.Equal(1, transport.PlainCalls);
            Assert.Equal(LlmPhase.EmotionalDirector, transport.LastPhase);
            Assert.Equal(catalogEntry.Temperature!.Value, transport.LastTemperature);
            Assert.Equal(catalogEntry.MaxTokens!.Value, transport.LastMaxTokens);
            Assert.Contains("visible delivered line", transport.LastUserMessage, StringComparison.Ordinal);
            Assert.Equal("keeps the reply tentative but available", direction.Restraint);
        }

        [Theory]
        [InlineData("", "empty_output")]
        [InlineData("not json", "no_json_object")]
        [InlineData("[1,2]", "root_nonobject")]
        [InlineData("{", "malformed_json")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""schema_version"":""emotional_director.v1"",""primary_emotion"":""relieved but cautious"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "malformed_json")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""primary_emotion"":""relieved but cautious"",""primary_emotion"":""quietly hurt"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "malformed_json")]
        [InlineData(@"{""primary_emotion"":""relieved but cautious"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "invalid_schema_version")]
        [InlineData(@"{""schema_version"":42,""primary_emotion"":""relieved but cautious"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "invalid_schema_version")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""primary_emotion"":""relieved but cautious"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "invalid_schema_version")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""primary_emotion"":""relieved but cautious""}", "missing_field")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""primary_emotion"":""relieved but cautious"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity"",""debug"":""unsafe""}", "unexpected_field")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""primary_emotion"":"" "",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "blank_field")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""primary_emotion"":""x"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "field_too_short")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""primary_emotion"":""relieved but cautious"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""analysis: she feels safer"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "meta_language")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""primary_emotion"":""relieved but cautious"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""StatType.Honesty rolled 18 against DC 15"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "raw_mechanics")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""primary_emotion"":""<3"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "symbolic_only")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""primary_emotion"":""relieved but cautious"",""intensity"":""moderate and steadily rising"",""underlying_feeling"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""That actually means a lot, but I need to know you mean it."",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "drafted_chat_reply")]
        public async Task Parser_RejectionMatrix_UsesStableSanitizedReasons(
            string response,
            string expectedReason)
        {
            var violations = new List<LlmContractViolation>();
            var transport = new PlainQueueTransport(response);
            var adapter = CreateAdapter(transport, violations.Add, retries: 0);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GenerateEmotionalDirectionAsync(MakeContext()));

            Assert.Equal(LlmPhase.EmotionalDirector, ex.Phase);
            Assert.Equal(expectedReason, ex.Reason);
            Assert.Equal("EmotionalDirectorContract", ex.ParserName);
            Assert.Single(violations);
            Assert.Equal(expectedReason, violations[0].Reason);
            Assert.DoesNotContain("visible delivered line", ex.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("No immediate retreat; the posture remains receptive.")]
        [InlineData("The strong feeling is softened by caution.")]
        [InlineData("Honesty lands as clean and emotionally credible.")]
        public async Task Validator_AcceptsOrdinaryActionableProse(string interpretation)
        {
            var transport = new PlainQueueTransport(ValidJson(interpretation: interpretation));
            var adapter = CreateAdapter(transport);

            var direction = await adapter.GenerateEmotionalDirectionAsync(MakeContext());

            Assert.Equal(interpretation, direction.Interpretation);
        }

        [Theory]
        [InlineData("strong")]
        [InlineData("honesty catastrophe")]
        [InlineData("StatType.Honesty")]
        [InlineData("Roll 18 + modifier 3 against DC 15")]
        [InlineData("Interest 12 -> 15")]
        [InlineData("The game mechanics mark this as a critical tier.")]
        [InlineData("Psychiatric diagnosis: fear of abandonment")]
        [InlineData("The dice made her cautious.")]
        [InlineData("She rolled poorly and now withdraws.")]
        public async Task Validator_RejectsExplicitMechanicalForms(string interpretation)
        {
            var transport = new PlainQueueTransport(ValidJson(interpretation: interpretation));
            var adapter = CreateAdapter(transport);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GenerateEmotionalDirectionAsync(MakeContext()));

            Assert.Equal("raw_mechanics", ex.Reason);
        }

        [Theory]
        [InlineData("That actually means a lot, but I need to know you mean it.")]
        [InlineData("The impulse is to say, \"That matters.\"")]
        public async Task Validator_RejectsClearDraftedChatMarkersAnywhere(string impulse)
        {
            var transport = new PlainQueueTransport(ValidJson(impulse: impulse));
            var adapter = CreateAdapter(transport);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GenerateEmotionalDirectionAsync(MakeContext()));

            Assert.Equal("drafted_chat_reply", ex.Reason);
        }

        [Fact]
        public async Task StructuredFallback_EnforcesTheSameMinimumFieldLengthAsNativeSchema()
        {
            var transport = new StructuredQueueTransport(
                new StructuredLlmResponse(
                    ValidJson(primaryEmotion: "x"),
                    usedNativeStructuredOutput: false));
            var adapter = CreateAdapter(transport);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GenerateEmotionalDirectionAsync(MakeContext()));

            Assert.Equal("field_too_short", ex.Reason);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task StructuredTransport_RejectsInvalidSchemaVersionWithSameReason(bool usedNativeStructuredOutput)
        {
            var validations = new List<StructuredLlmValidationResult>();
            var transport = new StructuredQueueTransport(
                new StructuredLlmResponse(
                    ValidJson(schemaVersion: "emotional_director.v2"),
                    provider: "test",
                    model: "structured",
                    usedNativeStructuredOutput: usedNativeStructuredOutput,
                    validationMode: "test_schema",
                    validationObserver: validations.Add));
            var adapter = CreateAdapter(transport);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GenerateEmotionalDirectionAsync(MakeContext()));

            Assert.Equal("invalid_schema_version", ex.Reason);
            var validation = Assert.Single(validations);
            Assert.Equal("rejected", validation.Outcome);
            Assert.Equal("invalid_schema_version", validation.RejectionReason);
        }

        [Fact]
        public async Task NativeStructuredOutput_FailsClosedBeforeParsingOversizedResponse()
        {
            var transport = new StructuredQueueTransport(
                new StructuredLlmResponse(
                    new string('x', GeneratedJsonObjectExtractionOptions.DefaultMaxInputChars + 1),
                    usedNativeStructuredOutput: true));
            var adapter = CreateAdapter(transport);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GenerateEmotionalDirectionAsync(MakeContext()));

            Assert.Equal("output_too_large", ex.Reason);
        }

        [Fact]
        public async Task PlainOutput_UsesTheSameOversizedResponseLimit()
        {
            var transport = new PlainQueueTransport(
                new string('x', GeneratedJsonObjectExtractionOptions.DefaultMaxInputChars + 1));
            var adapter = CreateAdapter(transport);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GenerateEmotionalDirectionAsync(MakeContext()));

            Assert.Equal("output_too_large", ex.Reason);
        }

        [Fact]
        public async Task Retry_AcceptsLaterValidContractWithoutPersistingPrivateText()
        {
            var violations = new List<LlmContractViolation>();
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var transport = new PlainQueueTransport("not json", ValidJson());
            var adapter = CreateAdapter(transport, violations.Add, retries: 1, diagnostics: diagnostics.Add);

            var direction = await adapter.GenerateEmotionalDirectionAsync(MakeContext());

            Assert.Equal(2, transport.PlainCalls);
            Assert.Single(violations);
            Assert.Equal("no_json_object", violations[0].Reason);
            Assert.Equal("relieved but cautious", direction.PrimaryEmotion);
            Assert.All(diagnostics, diagnostic =>
            {
                Assert.DoesNotContain("visible delivered line", diagnostic.Message, StringComparison.Ordinal);
                foreach (var hint in diagnostic.CorrelationHints)
                    Assert.DoesNotContain("visible delivered line", hint.Value, StringComparison.Ordinal);
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Retry_FieldTooLongAddsCatalogRepairWithoutEchoingRejectedPrivateOutput(
            bool structured)
        {
            const string privateRejectedText = "PRIVATE-OVERLONG-DIRECTOR-DO-NOT-ECHO-";
            string overlong = privateRejectedText + new string('x', 181);
            string invalid = ValidJson(interpretation: overlong);
            var context = MakeContext();

            if (structured)
            {
                var transport = new StructuredQueueTransport(
                    new StructuredLlmResponse(
                        invalid,
                        provider: "test",
                        model: "structured",
                        usedNativeStructuredOutput: true),
                    new StructuredLlmResponse(
                        ValidJson(),
                        provider: "test",
                        model: "structured",
                        usedNativeStructuredOutput: true));
                var adapter = CreateAdapter(transport, retries: 1);

                var direction = await adapter.GenerateEmotionalDirectionAsync(context);

                Assert.Equal("relieved but cautious", direction.PrimaryEmotion);
                Assert.Equal(2, transport.StructuredCalls);
                Assert.DoesNotContain(
                    "A previous emotional direction exceeded the permitted field length.",
                    transport.StructuredRequests[0].SystemPrompt,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "A previous emotional direction exceeded the permitted field length.",
                    transport.StructuredRequests[1].SystemPrompt,
                    StringComparison.Ordinal);
                Assert.Contains("180 characters or fewer", transport.StructuredRequests[1].SystemPrompt, StringComparison.Ordinal);
                Assert.DoesNotContain(privateRejectedText, transport.StructuredRequests[1].SystemPrompt, StringComparison.Ordinal);
                Assert.Equal(transport.StructuredRequests[0].JsonSchema, transport.StructuredRequests[1].JsonSchema);
                return;
            }

            var plainTransport = new PlainQueueTransport(invalid, ValidJson());
            var plainAdapter = CreateAdapter(plainTransport, retries: 1);

            var plainDirection = await plainAdapter.GenerateEmotionalDirectionAsync(context);

            Assert.Equal("relieved but cautious", plainDirection.PrimaryEmotion);
            Assert.Equal(2, plainTransport.PlainCalls);
            Assert.DoesNotContain(
                "A previous emotional direction exceeded the permitted field length.",
                plainTransport.SystemPrompts[0],
                StringComparison.Ordinal);
            Assert.Contains(
                "A previous emotional direction exceeded the permitted field length.",
                plainTransport.SystemPrompts[1],
                StringComparison.Ordinal);
            Assert.Contains("180 characters or fewer", plainTransport.SystemPrompts[1], StringComparison.Ordinal);
            Assert.DoesNotContain(privateRejectedText, plainTransport.SystemPrompts[1], StringComparison.Ordinal);
        }

        [Fact]
        public void FieldTooLongRepairPreservesYamlSpanInCompiledSystemPrompt()
        {
            PromptCatalog catalog = BuiltInCatalog();
            var compiler = new EmotionalPromptCompiler(catalog);
            CompiledEmotionalDirectorPrompt initial = compiler.CompileDirector(MakeContext());

            var repaired = compiler.CompileDirectorRetrySystemPrompt(
                initial.SystemPrompt,
                "field_too_long");

            var repairSpan = Assert.Single(repaired.Spans.Where(
                span => span.Key == EmotionalPromptCompiler.DirectorFieldTooLongRepairPromptKey));
            Assert.Equal("data/prompts/emotional-reactions.yaml", repairSpan.SourceFile);
            Assert.Equal(
                catalog.Get(EmotionalPromptCompiler.DirectorFieldTooLongRepairPromptKey).SystemPrompt!.Trim(),
                repaired.Text.Substring(repairSpan.Start, repairSpan.End - repairSpan.Start));
            Assert.Contains(
                repaired.Spans,
                span => span.Key == EmotionalPromptCompiler.DirectorPromptKey);
        }

        [Fact]
        public async Task Retry_ExhaustionThrowsFinalStableReason()
        {
            const string privateRejectedDirector = "PRIVATE-DIRECTOR-EXHAUSTION-DO-NOT-LOG-1345";
            var diagnostics = new List<OperationalDiagnosticEvent>();
            var transport = new PlainQueueTransport(privateRejectedDirector, privateRejectedDirector);
            var adapter = CreateAdapter(transport, retries: 1, diagnostics: diagnostics.Add);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GenerateEmotionalDirectionAsync(MakeContext()));

            Assert.Equal(2, transport.PlainCalls);
            Assert.Equal("no_json_object", ex.Reason);
            var terminal = Assert.Single(diagnostics.Where(
                diagnostic =>
                    diagnostic.PhaseCode == LlmPhase.EmotionalDirector
                    && diagnostic.Lifecycle == OperationalDiagnosticLifecycle.Terminal
                    && diagnostic.Outcome == OperationalDiagnosticOutcome.Failed));
            Assert.Equal("EmotionalDirectorContractExhausted", terminal.EventName);
            Assert.Equal(LlmPhase.EmotionalDirector, terminal.PhaseCode);
            Assert.Equal(OperationalDiagnosticLifecycle.Terminal, terminal.Lifecycle);
            Assert.Equal(OperationalDiagnosticOutcome.Failed, terminal.Outcome);
            Assert.Equal(OperationalDiagnosticFailureClassification.Permanent, terminal.FailureClassification);
            Assert.Null(terminal.Exception);
            Assert.Equal(nameof(LlmContractException), terminal.CorrelationHints["exception_type"]);
            Assert.Equal("contract_violation", terminal.CorrelationHints["failure_kind"]);
            Assert.Equal("no_json_object", terminal.CorrelationHints["reason"]);
            Assert.Equal("2", terminal.CorrelationHints["attempt_count"]);
            Assert.DoesNotContain(privateRejectedDirector, terminal.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("visible delivered line", terminal.Message, StringComparison.Ordinal);
            Assert.All(terminal.CorrelationHints.Values, value =>
            {
                Assert.DoesNotContain(privateRejectedDirector, value, StringComparison.Ordinal);
                Assert.DoesNotContain("visible delivered line", value, StringComparison.Ordinal);
            });
        }

        [Fact]
        public async Task Cancellation_PropagatesWithoutContractViolation()
        {
            var violations = new List<LlmContractViolation>();
            var transport = new CancellingTransport();
            var adapter = CreateAdapter(transport, violations.Add);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => adapter.GenerateEmotionalDirectionAsync(MakeContext(), cts.Token));

            Assert.Empty(violations);
        }

        [Fact]
        public async Task CurrentDateeResponsePath_RunsDirectorThenOpponentResponse()
        {
            var transport = new CountingTransport(ValidJson(), "A bounded DATEE reply.");
            var adapter = CreateAdapter(transport);

            var result = await adapter.GetDateeResponseAsync(
                MakeContext(),
                Array.Empty<ConversationMessage>());

            Assert.Equal(2, transport.PlainCalls);
            Assert.Equal(new[] { LlmPhase.EmotionalDirector, LlmPhase.OpponentResponse }, transport.Phases.ToArray());
            Assert.Contains("Primary emotion: relieved but cautious", transport.LastUserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("Private emotional director source packet", transport.LastUserMessage, StringComparison.Ordinal);
            Assert.Equal("A bounded DATEE reply.", result.Response.MessageText.Trim());
        }

        private static PinderLlmAdapter CreateAdapter(
            ILlmTransport transport,
            Action<LlmContractViolation>? onViolation = null,
            int retries = 0,
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

        private static DateeContext MakeContext()
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
                playerDeliveredMessage: "visible delivered line",
                interestBefore: 8,
                interestAfter: 12,
                responseDelayMinutes: 0,
                playerName: "Player",
                dateeName: "Datee",
                currentTurn: 7,
                interestBeforeState: InterestState.Lukewarm,
                interestAfterState: InterestState.Interested,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    StatType.Honesty,
                    RollOutcomeIntensity.Strong,
                    TestHelpers.MakePsychiatricDiagnosis()));
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
                var candidate = System.IO.Path.Combine(dir, "data", "prompts");
                if (System.IO.Directory.Exists(candidate)) return candidate;
                dir = System.IO.Path.GetDirectoryName(dir);
            }

            throw new System.IO.DirectoryNotFoundException("Could not locate bundled data/prompts.");
        }

        private static string JoinTraceValues(
            Pinder.Core.Text.PromptTraceResult trace,
            Func<Pinder.Core.Text.AnnotatedSpan, string?> selector)
        {
            return string.Join(
                ",",
                trace.Spans
                    .Select(selector)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal));
        }

        private static string ValidJson(
            string? interpretation = null,
            string? impulse = null,
            string? primaryEmotion = null,
            string? schemaVersion = "emotional_director.v1")
        {
            return new JObject
            {
                ["schema_version"] = schemaVersion,
                ["primary_emotion"] = primaryEmotion ?? "relieved but cautious",
                ["intensity"] = "moderate and steadily rising",
                ["underlying_feeling"] = "fear of being dismissed",
                ["interpretation"] = interpretation ?? "reads the message as specific warmth that is probably meant for them",
                ["impulse"] = impulse ?? "leans in with a careful question",
                ["restraint"] = "keeps the reply tentative but available",
                ["response_posture"] = "turns warmer while still checking sincerity",
            }.ToString(Formatting.None);
        }

        private class PlainQueueTransport : ILlmTransport
        {
            private readonly Queue<string> _responses;

            public PlainQueueTransport(params string[] responses)
            {
                _responses = new Queue<string>(responses);
            }

            public int PlainCalls { get; private set; }
            public string LastSystemPrompt { get; private set; } = string.Empty;
            public string LastUserMessage { get; private set; } = string.Empty;
            public List<string> SystemPrompts { get; } = new List<string>();
            public List<string> UserMessages { get; } = new List<string>();
            public string? LastPhase { get; private set; }
            public double LastTemperature { get; private set; }
            public int LastMaxTokens { get; private set; }

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
                LastSystemPrompt = systemPrompt;
                LastUserMessage = userMessage;
                SystemPrompts.Add(systemPrompt);
                UserMessages.Add(userMessage);
                LastPhase = phase;
                LastTemperature = temperature;
                LastMaxTokens = maxTokens;
                return Task.FromResult(_responses.Dequeue());
            }
        }

        private sealed class CountingTransport : PlainQueueTransport
        {
            public CountingTransport(params string[] responses)
                : base(responses)
            {
            }

            public List<string?> Phases { get; } = new List<string?>();

            public override Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                Phases.Add(phase);
                return base.SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase, ct);
            }
        }

        private sealed class StructuredQueueTransport : PlainQueueTransport, IStructuredLlmTransport
        {
            private readonly Queue<StructuredLlmResponse> _structuredResponses;

            public StructuredQueueTransport(params StructuredLlmResponse[] responses)
                : base()
            {
                _structuredResponses = new Queue<StructuredLlmResponse>(responses);
            }

            public int StructuredCalls { get; private set; }
            public StructuredLlmRequest? LastStructuredRequest { get; private set; }
            public List<StructuredLlmRequest> StructuredRequests { get; } =
                new List<StructuredLlmRequest>();

            public Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                StructuredCalls++;
                LastStructuredRequest = request;
                StructuredRequests.Add(request);
                return Task.FromResult(_structuredResponses.Dequeue());
            }
        }

        private sealed class CancellingTransport : ILlmTransport
        {
            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                throw new OperationCanceledException(ct);
            }
        }
    }
}
