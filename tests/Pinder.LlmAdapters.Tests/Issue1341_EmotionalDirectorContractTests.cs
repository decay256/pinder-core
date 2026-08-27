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
            Assert.Equal("emotional_director.v2", transport.LastStructuredRequest.SchemaVersion);
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
                    "secondary_emotion",
                    "regulatory_state",
                    "activation",
                    "trajectory",
                    "core_threat_or_desire",
                    "interpretation",
                    "impulse",
                    "restraint",
                    "response_posture",
                },
                schema["required"]!.Values<string>());
            Assert.Equal(
                "emotional_director.v2",
                schema["properties"]!["schema_version"]!.Value<string>("const"));
            Assert.Equal(
                "The contract schema version string. Must be exactly 'emotional_director.v2'.",
                schema["properties"]!["schema_version"]!.Value<string>("description"));
            Assert.Equal(
                "The single dominant concrete felt emotion chosen from the configured vocabulary.",
                schema["properties"]!["primary_emotion"]!.Value<string>("description"));
            Assert.Equal(
                "A distinct configured concrete emotion, or literal 'none'.",
                schema["properties"]!["secondary_emotion"]!.Value<string>("description"));
            Assert.Equal(
                "The character's regulatory state.",
                schema["properties"]!["regulatory_state"]!.Value<string>("description"));
            Assert.Equal(
                "Emotional activation from 1 through 5.",
                schema["properties"]!["activation"]!.Value<string>("description"));
            Assert.Equal(
                "The movement of the emotional beat.",
                schema["properties"]!["trajectory"]!.Value<string>("description"));
            Assert.Equal(
                "Concise vulnerable threat or desire driving the reaction.",
                schema["properties"]!["core_threat_or_desire"]!.Value<string>("description"));
            Assert.Equal(
                "How the latest visible message lands for this character.",
                schema["properties"]!["interpretation"]!.Value<string>("description"));
            Assert.Equal(
                "Immediate behavioral urge, never drafted dialogue.",
                schema["properties"]!["impulse"]!.Value<string>("description"));
            Assert.Equal(
                "What prevents full expression.",
                schema["properties"]!["restraint"]!.Value<string>("description"));
            Assert.Equal(
                "Actionable performance direction, never drafted dialogue.",
                schema["properties"]!["response_posture"]!.Value<string>("description"));
            Assert.All(
                schema["properties"]!
                    .Children<JProperty>()
                    .Where(property => property.Name != "schema_version"),
                property => Assert.Null(property.Value["maxLength"]));
            Assert.Equal(
                JoinTraceValues(compiled, span => span.Key),
                transport.LastStructuredRequest.Metadata["compiled_input_keys"]);
            Assert.Equal(
                JoinTraceValues(compiled, span => span.SourceFile),
                transport.LastStructuredRequest.Metadata["compiled_input_sources"]);
            Assert.Contains("visible delivered line", transport.LastStructuredRequest.UserMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("characters or fewer", transport.LastStructuredRequest.SystemPrompt, StringComparison.Ordinal);
            Assert.Equal("relief", direction.PrimaryEmotion);
            Assert.Equal(4, direction.Activation);
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
            Assert.Equal(4, direction.Activation);
            Assert.Equal("relief", direction.PrimaryEmotion);
        }

        [Fact]
        public void DirectorInput_ExplainsBothCharactersHfiAndTor()
        {
            var compiler = new EmotionalPromptCompiler(BuiltInCatalog());

            CompiledEmotionalDirectorPrompt prompt = compiler.CompileDirector(
                MakeContext(playerHfi: 4, playerTor: 13, dateeHfi: 12, dateeTor: 3));

            Assert.Contains("Datee has HFI 12", prompt.CompiledReactionInput.Text, StringComparison.Ordinal);
            Assert.Contains("Datee has TOR 3", prompt.CompiledReactionInput.Text, StringComparison.Ordinal);
            Assert.Contains("Player has HFI 4", prompt.CompiledReactionInput.Text, StringComparison.Ordinal);
            Assert.Contains("Player has TOR 13", prompt.CompiledReactionInput.Text, StringComparison.Ordinal);
            Assert.Contains("closeness is exerting a strong pull", prompt.CompiledReactionInput.Text, StringComparison.Ordinal);
            Assert.Contains("rejection feels dangerous", prompt.CompiledReactionInput.Text, StringComparison.Ordinal);
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
            Assert.Equal(catalogEntry.Temperature ?? 0.35, transport.LastTemperature);
            Assert.Equal(catalogEntry.MaxTokens, transport.LastMaxTokens);
            Assert.Contains("visible delivered line", transport.LastUserMessage, StringComparison.Ordinal);
            Assert.Equal("keeps the reply tentative but available", direction.Restraint);
        }

        [Theory]
        [InlineData("", "empty_output")]
        [InlineData("not json", "no_json_object")]
        [InlineData("[1,2]", "root_nonobject")]
        [InlineData("{", "malformed_json")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""schema_version"":""emotional_director.v2"",""primary_emotion"":""relief"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "malformed_json")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""primary_emotion"":""relief"",""primary_emotion"":""hurt"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "malformed_json")]
        [InlineData(@"{""primary_emotion"":""relief"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "invalid_schema_version")]
        [InlineData(@"{""schema_version"":42,""primary_emotion"":""relief"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "invalid_schema_version")]
        [InlineData(@"{""schema_version"":""emotional_director.v1"",""primary_emotion"":""relief"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "invalid_schema_version")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""primary_emotion"":""relief""}", "missing_field")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""primary_emotion"":""relief"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity"",""debug"":""unsafe""}", "unexpected_field")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""primary_emotion"":"" "",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "blank_field")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""primary_emotion"":""relief"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""x"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "field_too_short")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""primary_emotion"":""relief"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""fear of being dismissed"",""interpretation"":""analysis: she feels safer"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "meta_language")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""primary_emotion"":""relief"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""fear of being dismissed"",""interpretation"":""StatType.Honesty rolled 18 against DC 15"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "raw_mechanics")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""primary_emotion"":""relief"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""<3"",""interpretation"":""reads it as specific warmth"",""impulse"":""leans in with a careful question"",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "symbolic_only")]
        [InlineData(@"{""schema_version"":""emotional_director.v2"",""primary_emotion"":""relief"",""secondary_emotion"":""none"",""regulatory_state"":""controlled"",""activation"":4,""trajectory"":""escalating"",""core_threat_or_desire"":""fear of being dismissed"",""interpretation"":""reads it as specific warmth"",""impulse"":""That actually means a lot, but I need to know you mean it."",""restraint"":""keeps the reply tentative but available"",""response_posture"":""turns warmer while still checking sincerity""}", "drafted_chat_reply")]
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
            Assert.Equal("CharacterEmotionalDirectionContract", ex.ParserName);
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
                    ValidJson(coreThreatOrDesire: "x"),
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
                    ValidJson(schemaVersion: "emotional_director.v1"),
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
            Assert.Equal("relief", direction.PrimaryEmotion);
            Assert.All(diagnostics, diagnostic =>
            {
                Assert.DoesNotContain("visible delivered line", diagnostic.Message, StringComparison.Ordinal);
                foreach (var hint in diagnostic.CorrelationHints)
                    Assert.DoesNotContain("visible delivered line", hint.Value, StringComparison.Ordinal);
            });
        }

        [Fact]
        public async Task Retry_MissingFieldAddsCatalogRepairWithoutEchoingRejectedPrivateOutput()
        {
            const string privateRejectedText = "PRIVATE-MISSING-DIRECTOR-DO-NOT-ECHO";
            string invalid =
                "{\"schema_version\":\"emotional_director.v1\","
                + "\"primary_emotion\":\"" + privateRejectedText + "\"}";
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

            var direction = await adapter.GenerateEmotionalDirectionAsync(MakeContext());

            Assert.Equal("relief", direction.PrimaryEmotion);
            Assert.Equal(2, transport.StructuredCalls);
            Assert.DoesNotContain(
                "The previous emotional direction did not satisfy the response contract.",
                transport.StructuredRequests[0].SystemPrompt,
                StringComparison.Ordinal);
            Assert.Contains(
                "The previous emotional direction did not satisfy the response contract.",
                transport.StructuredRequests[1].SystemPrompt,
                StringComparison.Ordinal);
            Assert.Contains(
                "response_posture",
                transport.StructuredRequests[1].SystemPrompt,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                privateRejectedText,
                transport.StructuredRequests[1].SystemPrompt,
                StringComparison.Ordinal);
            Assert.Equal(
                transport.StructuredRequests[0].JsonSchema,
                transport.StructuredRequests[1].JsonSchema);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LongFieldsParseWithoutRetry(
            bool structured)
        {
            string longInterpretation = "reads the message as sincere warmth " + new string('x', 500);
            string json = ValidJson(interpretation: longInterpretation);
            var context = MakeContext();

            if (structured)
            {
                var transport = new StructuredQueueTransport(
                    new StructuredLlmResponse(
                        json,
                        provider: "test",
                        model: "structured",
                        usedNativeStructuredOutput: true));
                var adapter = CreateAdapter(transport, retries: 1);

                var direction = await adapter.GenerateEmotionalDirectionAsync(context);

                Assert.Equal(longInterpretation, direction.Interpretation);
                Assert.Equal(1, transport.StructuredCalls);
                return;
            }

            var plainTransport = new PlainQueueTransport(json);
            var plainAdapter = CreateAdapter(plainTransport, retries: 1);

            var plainDirection = await plainAdapter.GenerateEmotionalDirectionAsync(context);

            Assert.Equal(longInterpretation, plainDirection.Interpretation);
            Assert.Equal(1, plainTransport.PlainCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Retry_DraftedChatReplyAddsActionableCatalogRepairWithoutEchoingRejectedOutput(
            bool structured)
        {
            const string privateRejectedText =
                "That actually means a lot, but I need to know you mean it.";
            string invalid = ValidJson(impulse: privateRejectedText);
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

                Assert.Equal("relief", direction.PrimaryEmotion);
                Assert.Equal(2, transport.StructuredCalls);
                Assert.Contains(
                    "write impulse as a behavioral urge",
                    transport.StructuredRequests[1].SystemPrompt,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "Do not use first-person speech",
                    transport.StructuredRequests[1].SystemPrompt,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    privateRejectedText,
                    transport.StructuredRequests[1].SystemPrompt,
                    StringComparison.Ordinal);
                Assert.Equal(
                    transport.StructuredRequests[0].JsonSchema,
                    transport.StructuredRequests[1].JsonSchema);
                return;
            }

            var plainTransport = new PlainQueueTransport(invalid, ValidJson());
            var plainAdapter = CreateAdapter(plainTransport, retries: 1);

            var plainDirection = await plainAdapter.GenerateEmotionalDirectionAsync(context);

            Assert.Equal("relief", plainDirection.PrimaryEmotion);
            Assert.Equal(2, plainTransport.PlainCalls);
            Assert.Contains(
                "write impulse as a behavioral urge",
                plainTransport.SystemPrompts[1],
                StringComparison.Ordinal);
            Assert.Contains(
                "Do not use first-person speech",
                plainTransport.SystemPrompts[1],
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                privateRejectedText,
                plainTransport.SystemPrompts[1],
                StringComparison.Ordinal);
        }

        [Fact]
        public void GenericContractRepairPreservesYamlSpanInCompiledSystemPrompt()
        {
            PromptCatalog catalog = BuiltInCatalog();
            var compiler = new EmotionalPromptCompiler(catalog);
            CompiledEmotionalDirectorPrompt initial = compiler.CompileDirector(MakeContext());

            var repaired = compiler.CompileDirectorRetrySystemPrompt(
                initial.SystemPrompt,
                "missing_field");

            var repairSpan = Assert.Single(repaired.Spans.Where(
                span => span.Key == EmotionalPromptCompiler.DirectorContractRepairPromptKey));
            Assert.Equal("data/prompts/emotional-reactions.yaml", repairSpan.SourceFile);
            Assert.Equal(
                catalog.Get(EmotionalPromptCompiler.DirectorContractRepairPromptKey).SystemPrompt!.Trim(),
                repaired.Text.Substring(repairSpan.Start, repairSpan.End - repairSpan.Start));
            Assert.Contains(
                repaired.Spans,
                span => span.Key == EmotionalPromptCompiler.DirectorPromptKey);
        }

        [Fact]
        public void DraftedChatReplyRepairPreservesYamlSpanInCompiledSystemPrompt()
        {
            PromptCatalog catalog = BuiltInCatalog();
            var compiler = new EmotionalPromptCompiler(catalog);
            CompiledEmotionalDirectorPrompt initial = compiler.CompileDirector(MakeContext());

            var repaired = compiler.CompileDirectorRetrySystemPrompt(
                initial.SystemPrompt,
                "drafted_chat_reply");

            var repairSpan = Assert.Single(repaired.Spans.Where(
                span => span.Key == EmotionalPromptCompiler.DirectorDraftedChatReplyRepairPromptKey));
            Assert.Equal("data/prompts/emotional-reactions.yaml", repairSpan.SourceFile);
            Assert.Equal(
                catalog.Get(EmotionalPromptCompiler.DirectorDraftedChatReplyRepairPromptKey).SystemPrompt!.Trim(),
                repaired.Text.Substring(repairSpan.Start, repairSpan.End - repairSpan.Start));
            Assert.Contains(
                repaired.Spans,
                span => span.Key == EmotionalPromptCompiler.DirectorPromptKey);
        }

        [Fact]
        public void ResponsePostureOmitsPrimaryEmotionRepairPreservesYamlSpanInCompiledSystemPrompt()
        {
            PromptCatalog catalog = BuiltInCatalog();
            var compiler = new EmotionalPromptCompiler(catalog);
            CompiledEmotionalDirectorPrompt initial = compiler.CompileDirector(MakeContext());

            var repaired = compiler.CompileDirectorRetrySystemPrompt(
                initial.SystemPrompt,
                "response_posture_omits_primary_emotion");

            const string expectedKey = "emotional-reaction-director-repair-response-posture-omits-primary-emotion";
            var repairSpan = Assert.Single(repaired.Spans.Where(
                span => span.Key == expectedKey));
            Assert.Equal("data/prompts/emotional-reactions.yaml", repairSpan.SourceFile);
            Assert.Equal(
                catalog.Get(expectedKey).SystemPrompt!.Trim(),
                repaired.Text.Substring(repairSpan.Start, repairSpan.End - repairSpan.Start));
            Assert.Contains(
                "Writing from <exact primary_emotion value>, ...",
                repaired.Text,
                StringComparison.Ordinal);
            Assert.Contains(
                repaired.Spans,
                span => span.Key == EmotionalPromptCompiler.DirectorPromptKey);
        }

        [Fact]
        public void UnsupportedPrimaryEmotionRepairPreservesYamlSpanAndSubstitutesVocabulary()
        {
            PromptCatalog catalog = BuiltInCatalog();
            var compiler = new EmotionalPromptCompiler(catalog);
            CompiledEmotionalDirectorPrompt initial = compiler.CompileDirector(MakeContext());

            var repaired = compiler.CompileDirectorRetrySystemPrompt(
                initial.SystemPrompt,
                "unsupported_primary_emotion");

            const string expectedKey = "emotional-reaction-director-repair-unsupported-primary-emotion";
            Assert.DoesNotContain("{emotion_vocabulary}", repaired.Text, StringComparison.Ordinal);
            Assert.Contains(repaired.Spans, span => span.Key == expectedKey);
            Assert.Contains(repaired.Spans, span => span.Key == CharacterEmotionCatalog.PromptKey);
            Assert.Contains(
                repaired.Spans,
                span => span.Key == EmotionalPromptCompiler.DirectorPromptKey);

            var vocabulary = string.Join(", ", CharacterEmotionCatalog.Load(catalog));
            Assert.Contains(vocabulary, repaired.Text, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Retry_UnsupportedPrimaryEmotionAddsActionableCatalogRepairWithVocabularyWithoutEchoingRejectedOutput(
            bool structured)
        {
            string invalid = ValidJson(
                primaryEmotion: "contempt",
                responsePosture: "Writing from contempt, they sharpen every observation.");
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

                Assert.Equal("relief", direction.PrimaryEmotion);
                Assert.Equal(2, transport.StructuredCalls);
                Assert.DoesNotContain(
                    "The previous emotional direction did not satisfy the response contract.",
                    transport.StructuredRequests[1].SystemPrompt,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "primary_emotion",
                    transport.StructuredRequests[1].SystemPrompt,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "relief",
                    transport.StructuredRequests[1].SystemPrompt,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "contempt",
                    transport.StructuredRequests[1].SystemPrompt,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "{emotion_vocabulary}",
                    transport.StructuredRequests[1].SystemPrompt,
                    StringComparison.Ordinal);
                Assert.Equal(
                    transport.StructuredRequests[0].JsonSchema,
                    transport.StructuredRequests[1].JsonSchema);
                return;
            }

            var plainTransport = new PlainQueueTransport(invalid, ValidJson());
            var plainAdapter = CreateAdapter(plainTransport, retries: 1);

            var plainDirection = await plainAdapter.GenerateEmotionalDirectionAsync(context);

            Assert.Equal("relief", plainDirection.PrimaryEmotion);
            Assert.Equal(2, plainTransport.PlainCalls);
            Assert.DoesNotContain(
                "The previous emotional direction did not satisfy the response contract.",
                plainTransport.SystemPrompts[1],
                StringComparison.Ordinal);
            Assert.Contains(
                "primary_emotion",
                plainTransport.SystemPrompts[1],
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "contempt",
                plainTransport.SystemPrompts[1],
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "{emotion_vocabulary}",
                plainTransport.SystemPrompts[1],
                StringComparison.Ordinal);
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
            Assert.Contains("Primary emotion: relief", transport.LastUserMessage, StringComparison.Ordinal);
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

        private static DateeContext MakeContext(
            int? playerHfi = null,
            int? playerTor = null,
            int? dateeHfi = null,
            int? dateeTor = null)
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
                    TestHelpers.MakePsychiatricDiagnosis()),
                playerHungerForIntimacy: playerHfi,
                playerTerrorOfRejection: playerTor,
                dateeHungerForIntimacy: dateeHfi,
                dateeTerrorOfRejection: dateeTor);
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
            string? schemaVersion = "emotional_director.v2",
            string? responsePosture = null,
            string? secondaryEmotion = null,
            string? regulatoryState = null,
            int activation = 4,
            string? trajectory = null,
            string? coreThreatOrDesire = null)
        {
            return new JObject
            {
                ["schema_version"] = schemaVersion,
                ["primary_emotion"] = primaryEmotion ?? "relief",
                ["secondary_emotion"] = secondaryEmotion ?? CharacterEmotionalDirection.NoneSecondaryEmotion,
                ["regulatory_state"] = regulatoryState ?? "controlled",
                ["activation"] = activation,
                ["trajectory"] = trajectory ?? "escalating",
                ["core_threat_or_desire"] = coreThreatOrDesire ?? "fear of being dismissed",
                ["interpretation"] = interpretation ?? "reads the message as specific warmth that is probably meant for them",
                ["impulse"] = impulse ?? "leans in with a careful question",
                ["restraint"] = "keeps the reply tentative but available",
                ["response_posture"] = responsePosture ?? "turns warmer while still checking sincerity",
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
            public int? LastMaxTokens { get; private set; }

            public virtual Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
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
                int? maxTokens = null,
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
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                throw new OperationCanceledException(ct);
            }
        }
    }
}
