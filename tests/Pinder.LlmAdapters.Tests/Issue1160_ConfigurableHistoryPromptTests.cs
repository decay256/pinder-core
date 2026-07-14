using System;
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
    public class Issue1160_ConfigurableHistoryPromptTests
    {
        private sealed class CapturingTransport : ILlmTransport
        {
            private readonly string _response;

            public string UserMessage { get; private set; } = "";

            public CapturingTransport(string response)
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
                UserMessage = userMessage;
                return Task.FromResult(_response);
            }
        }

        static Issue1160_ConfigurableHistoryPromptTests()
        {
            PromptCatalogInitializer.Initialize();
        }

        [Fact]
        public async Task SteeringPrompt_EmptyHistoryUsesCatalogTextExactly()
        {
            var transport = new CapturingTransport("what made that feel true?");
            var adapter = CreateAdapter(transport);

            await adapter.GetSteeringQuestionAsync(new SteeringContext(
                playerAvatarPrompt: "player",
                dateeName: "Sable",
                playerName: "Velvet",
                deliveredMessage: "hey",
                conversationHistory: Array.Empty<(string Sender, string Text)>()));

            Assert.Contains("CONVERSATION SO FAR:", transport.UserMessage);
            Assert.Contains("The conversation just started.", transport.UserMessage);
        }

        [Fact]
        public async Task HorninessQuestionPrompt_EmptyHistoryUsesCatalogTextExactly()
        {
            var transport = new CapturingTransport("too much?");
            var adapter = CreateAdapter(transport);

            await adapter.GetHorninessQuestionAsync(new HorninessQuestionContext(
                playerAvatarPrompt: "player",
                dateeName: "Sable",
                playerName: "Velvet",
                deliveredMessage: "hey",
                conversationHistory: Array.Empty<(string Sender, string Text)>()));

            Assert.Contains("CONVERSATION SO FAR:", transport.UserMessage);
            Assert.Contains("The conversation just started.", transport.UserMessage);
        }

        [Fact]
        public async Task SuccessImprovementPrompt_EmptyHistoryUsesCatalogTextExactly()
        {
            var transport = new CapturingTransport("better line");
            var adapter = CreateAdapter(transport, withDeliveryInstructions: true);

            await adapter.GetSuccessImprovementAsync(new SuccessImprovementContext(
                playerAvatarPrompt: "player",
                dateeName: "Sable",
                playerName: "Velvet",
                deliveredMessage: "hey",
                stat: StatType.Charm,
                tierKey: "strong",
                conversationHistory: Array.Empty<(string Sender, string Text)>()));

            Assert.Contains("CONVERSATION SO FAR:", transport.UserMessage);
            Assert.Contains("The conversation just started.", transport.UserMessage);
        }

        private static PinderLlmAdapter CreateAdapter(
            CapturingTransport transport,
            bool withDeliveryInstructions = false)
        {
            return new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = new GameDefinition(
                        name: "Pinder",
                        gameMasterPrompt: "gm",
                        playerAvatarRoleDescription: "player role",
                        dateeRoleDescription: "datee role",
                        steeringPrompt: "CONVERSATION SO FAR:\n{conversation_history}\nwrite one steering question about {delivered_message}",
                        horninessPrompt: "CONVERSATION SO FAR:\n{conversation_history}\nwrite one horny followup for {delivered_message}",
                        maxDialogueOptions: 3,
                        maxTurns: 30,
                        maxDeliveryWords: 80),
                    StatDeliveryInstructions = withDeliveryInstructions
                        ? StatDeliveryInstructions.LoadFrom(@"
delivery_instructions:
  charm:
    strong: ""rewrite {delivered_message}""
  horniness_overlay:
    fumble: ""mock horniness""
shadow_corruption:
  madness:
    fumble: ""mock madness""
success_improvement_prompt_template: |-
  CONVERSATION SO FAR:
  {conversation_history}
  Rewrite {delivered_message} for {stat} at {tier}/{tier_upper}.
  {instruction}
")
                        : null
                });
        }
    }
}
