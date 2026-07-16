using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.TestCommon;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public class Issue1159_1160_StructuredDialogueOptionsTests
    {
        static Issue1159_1160_StructuredDialogueOptionsTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        private sealed class StructuredQueueTransport : ILlmTransport, IStructuredLlmTransport
        {
            private readonly Queue<StructuredLlmResponse> _responses = new Queue<StructuredLlmResponse>();

            public int StructuredCalls { get; private set; }
            public StructuredLlmRequest? LastRequest { get; private set; }

            public StructuredQueueTransport(params StructuredLlmResponse[] responses)
            {
                foreach (var response in responses)
                {
                    _responses.Enqueue(response);
                }
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                throw new InvalidOperationException("Native structured tests must not use SendAsync.");
            }

            public Task<StructuredLlmResponse> SendStructuredAsync(
                StructuredLlmRequest request,
                CancellationToken ct = default)
            {
                StructuredCalls++;
                LastRequest = request;
                return Task.FromResult(_responses.Dequeue());
            }
        }

        private sealed class TextTransport : ILlmTransport
        {
            private readonly string _response;

            public int Calls { get; private set; }

            public TextTransport(string response)
            {
                _response = response;
            }

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                Calls++;
                return Task.FromResult(_response);
            }
        }

        [Fact]
        public async Task NativeStructuredTransport_ParsesVersionedNeutralSchema()
        {
            var transport = new StructuredQueueTransport(
                new StructuredLlmResponse(ValidJson(), provider: "test", model: "structured", usedNativeStructuredOutput: true));
            var adapter = CreateAdapter(transport);

            var options = await adapter.GetDialogueOptionsAsync(MakeContext());

            Assert.Equal(1, transport.StructuredCalls);
            Assert.Equal("dialogue_options", transport.LastRequest!.SchemaName);
            Assert.Equal("dialogue_options.v1", transport.LastRequest.SchemaVersion);
            Assert.Equal("dialogue_options", transport.LastRequest.Metadata["phase"]);
            Assert.Contains("\"schema_version\"", transport.LastRequest.JsonSchema);
            Assert.Equal(2, options.Length);
            Assert.Equal(StatType.Charm, options[0].Stat);
            Assert.Equal("that jacket is doing dangerous work", options[0].IntendedText);
            Assert.False(options[0].HasTellBonus);
            Assert.False(options[0].HasWeaknessWindow);
            Assert.Equal(StatType.Honesty, options[1].Stat);
        }

        [Fact]
        public async Task TextTransport_StrictLocalJsonFallback_ParsesBeforeLegacyFormat()
        {
            var transport = new TextTransport(ValidJson());
            var adapter = CreateAdapter(transport);

            var options = await adapter.GetDialogueOptionsAsync(MakeContext());

            Assert.Equal(1, transport.Calls);
            Assert.Equal(2, options.Length);
            Assert.Equal(StatType.Charm, options[0].Stat);
            Assert.Equal(StatType.Honesty, options[1].Stat);
        }

        [Fact]
        public async Task TextTransport_LegacyOptionFormat_RemainsCompatible()
        {
            string legacy = "OPTION_1\n[STAT: CHARM] [CALLBACK: none] [COMBO: none]\n\"hey, that jacket is doing dangerous work\"\n\nOPTION_2\n[STAT: HONESTY] [CALLBACK: none] [COMBO: none]\n\"I should admit your bio got me curious\"";
            var transport = new TextTransport(legacy);
            var adapter = CreateAdapter(transport);

            var options = await adapter.GetDialogueOptionsAsync(MakeContext());

            Assert.Equal(2, options.Length);
            Assert.Equal(StatType.Charm, options[0].Stat);
            Assert.Equal(StatType.Honesty, options[1].Stat);
        }

        [Fact]
        public async Task NativeStructuredTransport_CountViolation_RetriesAndRecovers()
        {
            var validations = new List<StructuredLlmValidationResult>();
            var transport = new StructuredQueueTransport(
                new StructuredLlmResponse(OneOptionJson(), provider: "test", model: "structured", usedNativeStructuredOutput: true,
                    validationMode: "test_schema", validationObserver: validations.Add),
                new StructuredLlmResponse(ValidJson(), provider: "test", model: "structured", usedNativeStructuredOutput: true,
                    validationMode: "test_schema", validationObserver: validations.Add));
            int violations = 0;
            var adapter = CreateAdapter(transport, violation => violations++);

            var options = await adapter.GetDialogueOptionsAsync(MakeContext());

            Assert.Equal(2, transport.StructuredCalls);
            Assert.Equal(1, violations);
            Assert.Equal(2, options.Length);
            Assert.Collection(
                validations,
                first =>
                {
                    Assert.Equal("test_schema", first.Mode);
                    Assert.Equal("rejected", first.Outcome);
                    Assert.Equal("option_count_mismatch", first.RejectionReason);
                },
                second =>
                {
                    Assert.Equal("test_schema", second.Mode);
                    Assert.Equal("accepted", second.Outcome);
                    Assert.Null(second.RejectionReason);
                });
        }

        [Fact]
        public async Task NativeStructuredTransport_ReasoningOnlyEmptyContent_IsRejected()
        {
            var transport = new StructuredQueueTransport(
                new StructuredLlmResponse("", provider: "openai-compatible", model: "reasoning-model", usedNativeStructuredOutput: true));
            var adapter = CreateAdapter(transport);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GetDialogueOptionsAsync(MakeContext()));

            Assert.Equal("dialogue_options", ex.Phase);
            Assert.Equal("empty_output", ex.Reason);
            Assert.Equal("StructuredDialogueOptionsParser", ex.ParserName);
            Assert.Equal("openai-compatible", ex.Provider);
        }

        [Fact]
        public async Task NativeStructuredTransport_RejectsExtraRootProperty()
        {
            var transport = new StructuredQueueTransport(
                new StructuredLlmResponse(ValidJsonWithExtraRootProperty(), provider: "test", model: "structured", usedNativeStructuredOutput: true));
            var adapter = CreateAdapter(transport);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GetDialogueOptionsAsync(MakeContext()));

            Assert.Equal("unexpected_property", ex.Reason);
            Assert.Contains("debug", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TextTransport_StrictLocalJsonFallback_RejectsExtraOptionProperty()
        {
            var transport = new TextTransport(ValidJsonWithExtraOptionProperty());
            var adapter = CreateAdapter(transport);

            var ex = await Assert.ThrowsAsync<LlmContractException>(
                () => adapter.GetDialogueOptionsAsync(MakeContext()));

            Assert.Equal("unexpected_property", ex.Reason);
            Assert.Contains("unsafe_extra", ex.Message, StringComparison.Ordinal);
        }

        private static PinderLlmAdapter CreateAdapter(
            ILlmTransport transport,
            Action<LlmContractViolation>? onViolation = null)
        {
            return new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = GameDefinition.PinderDefaults,
                    MaxContractViolationRetries = onViolation == null ? 0 : 1,
                    ContractViolationBackoffMs = 1,
                    OnLlmContractViolation = onViolation
                });
        }

        private static DialogueContext MakeContext()
        {
            return new DialogueContext(
                playerAvatarPrompt: "player prompt",
                dateePrompt: "datee profile",
                conversationHistory: Array.Empty<(string Sender, string Text)>(),
                dateeLastMessage: "",
                activeTraps: Array.Empty<string>(),
                currentInterest: 10,
                playerName: "Velvet",
                dateeName: "Sable",
                currentTurn: 1,
                availableStats: new[] { StatType.Charm, StatType.Honesty });
        }

        private static string ValidJson()
        {
            return @"{
  ""schema_version"": ""dialogue_options.v1"",
  ""options"": [
    { ""stat"": ""CHARM"", ""text"": ""that jacket is doing dangerous work"", ""callback"": null, ""combo"": null },
    { ""stat"": ""HONESTY"", ""text"": ""I should admit your bio got me curious"", ""callback"": ""none"", ""combo"": ""none"" }
  ]
}";
        }

        private static string OneOptionJson()
        {
            return @"{
  ""schema_version"": ""dialogue_options.v1"",
  ""options"": [
    { ""stat"": ""CHARM"", ""text"": ""that jacket is doing dangerous work"", ""callback"": null, ""combo"": null }
  ]
}";
        }

        private static string ValidJsonWithExtraRootProperty()
        {
            return @"{
  ""schema_version"": ""dialogue_options.v1"",
  ""options"": [
    { ""stat"": ""CHARM"", ""text"": ""that jacket is doing dangerous work"", ""callback"": null, ""combo"": null },
    { ""stat"": ""HONESTY"", ""text"": ""I should admit your bio got me curious"", ""callback"": ""none"", ""combo"": ""none"" }
  ],
  ""debug"": ""ignored""
}";
        }

        private static string ValidJsonWithExtraOptionProperty()
        {
            return @"{
  ""schema_version"": ""dialogue_options.v1"",
  ""options"": [
    { ""stat"": ""CHARM"", ""text"": ""that jacket is doing dangerous work"", ""callback"": null, ""combo"": null, ""unsafe_extra"": ""ignored"" },
    { ""stat"": ""HONESTY"", ""text"": ""I should admit your bio got me curious"", ""callback"": ""none"", ""combo"": ""none"" }
  ]
}";
        }
    }
}
