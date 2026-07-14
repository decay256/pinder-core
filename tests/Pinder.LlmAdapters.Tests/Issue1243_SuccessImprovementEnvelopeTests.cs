using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public class Issue1243_SuccessImprovementEnvelopeTests
    {
        private sealed class CapturingTransport : ILlmTransport
        {
            public string UserMessage { get; private set; } = "";
            public string SystemPrompt { get; private set; } = "";
            private readonly string _response;

            public CapturingTransport(string response)
            {
                _response = response;
            }

            public Task<string> SendAsync(string systemPrompt, string userMessage, double temperature = 0.9, int maxTokens = 1024, string? phase = null, CancellationToken ct = default)
            {
                SystemPrompt = systemPrompt;
                UserMessage = userMessage;
                return Task.FromResult(_response);
            }
        }

        private static PinderLlmAdapter CreateAdapter(string response, out CapturingTransport transport, out List<OverlayDegradedEvent> degradedEvents)
        {
            transport = new CapturingTransport(response);
            var capturedEvents = new List<OverlayDegradedEvent>();

            var yamlContent = @"
delivery_instructions:
  charm:
    strong: ""rewrite the intended message so it lands harder: {delivered_message}""
  horniness_overlay:
    fumble: ""mock horniness""
shadow_corruption:
  madness:
    fumble: ""mock madness""
success_improvement_prompt_template: |-
  CONFIGURED SUCCESS {tier_upper} {stat}
  TIER KEY: {tier}
  HISTORY:
  {conversation_history}
  DELIVERED: {delivered_message}
  INSTRUCTION:
  {instruction}
";
            var instructions = StatDeliveryInstructions.LoadFrom(yamlContent);

            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                StatDeliveryInstructions = instructions,
                OnOverlayDegraded = evt => capturedEvents.Add(evt)
            };

            degradedEvents = capturedEvents;
            return new PinderLlmAdapter(transport, options);
        }

        private static SuccessImprovementContext CreateContext()
        {
            var history = new List<(string Sender, string Text)>
            {
                ("Datee", "Tell me about yourself.")
            };
            
            return new SuccessImprovementContext(
                playerAvatarPrompt: "You are the Player.",
                dateeName: "Datee",
                playerName: "Player",
                deliveredMessage: "I like to code.",
                stat: StatType.Charm,
                tierKey: "strong",
                conversationHistory: history
            );
        }

        [Fact]
        public async Task B1_EnvelopeContent_IncludesPurposeTierStatAndFormatReqs()
        {
            var adapter = CreateAdapter("Some response.", out var transport, out _);
            var context = CreateContext();

            await adapter.GetSuccessImprovementAsync(context);

            var promptText = transport.UserMessage + transport.SystemPrompt;

            Assert.Contains("STRONG", promptText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Charm", promptText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CONFIGURED SUCCESS STRONG Charm", promptText);
            Assert.Contains("Datee: Tell me about yourself.", promptText);
            Assert.Contains("rewrite the intended message so it lands harder: I like to code.", promptText);
        }

        [Fact]
        public async Task B1b_EnvelopeContent_ComesFromConfiguredTemplate()
        {
            var adapter = CreateAdapter("Some response.", out var transport, out _);
            var context = CreateContext();

            await adapter.GetSuccessImprovementAsync(context);

            Assert.Contains("CONFIGURED SUCCESS STRONG Charm", transport.UserMessage);
            Assert.DoesNotContain("<ENGINE_STATE>", transport.UserMessage);
            Assert.DoesNotContain("CALL PURPOSE: SUCCESS_IMPROVEMENT", transport.UserMessage);
            Assert.DoesNotContain("CONVERSATION SO FAR:", transport.UserMessage);
        }

        [Fact]
        public async Task B2_InvalidEngineStateRejection_ReturnsOriginalAndFiresDegraded()
        {
            var adapter = CreateAdapter("<ENGINE_STATE> INVALID_ENGINE_STATE </ENGINE_STATE>", out _, out var degradedEvents);
            var context = CreateContext();

            var result = await adapter.GetSuccessImprovementAsync(context);

            Assert.Equal("I like to code.", result);
            Assert.Single(degradedEvents);
            var evt = degradedEvents[0];
            Assert.Equal("success_improvement", evt.OverlayType);
            Assert.Equal(OverlayOutcome.Degraded, evt.Outcome);
        }

        [Fact]
        public async Task B3_MetaControlRejection_ReturnsOriginalAndFiresDegraded()
        {
            var adapter = CreateAdapter("I need to analyze the conversation. Now I need to generate OPTIONS.", out _, out var degradedEvents);
            var context = CreateContext();

            var result = await adapter.GetSuccessImprovementAsync(context);

            Assert.Equal("I like to code.", result);
            Assert.Single(degradedEvents);
            var evt = degradedEvents[0];
            Assert.Equal("success_improvement", evt.OverlayType);
            Assert.Equal(OverlayOutcome.Degraded, evt.Outcome);
        }

        [Fact]
        public async Task B4_ValidShortRewrite_AppliesNormally_NoDegradedEvent()
        {
            var validRewrite = "\"I'm secretly a hacker.\"";
            var adapter = CreateAdapter(validRewrite, out _, out var degradedEvents);
            var context = CreateContext();

            var result = await adapter.GetSuccessImprovementAsync(context);

            Assert.Equal("I'm secretly a hacker.", result);
            Assert.Empty(degradedEvents);
        }
    }
}
