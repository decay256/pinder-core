using System;
using System.IO;
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
    public class ParameterDriftFixTests
    {
        private sealed class TemperatureTrackingTransport : ILlmTransport
        {
            private readonly string _response;

            public double LastTemperature { get; private set; } = -1.0;

            public TemperatureTrackingTransport(string response = "mocked-response")
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
                LastTemperature = temperature;
                return Task.FromResult(_response);
            }
        }

        [Fact]
        public async Task ApplyHorninessOverlayAsync_UsesDefaultDeliveryTemperature_WhenOptionsTemperatureIsNull()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = null
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyHorninessOverlayAsync("hello", "make it horny");

            // Assert
            Assert.Equal(LlmPhaseTemperatures.OverlayRewrite, transport.LastTemperature);
        }

        [Fact]
        public async Task ApplyHorninessOverlayAsync_UsesOptionsTemperature_WhenProvided()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = 0.5
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyHorninessOverlayAsync("hello", "make it horny");

            // Assert
            Assert.Equal(0.5, transport.LastTemperature);
        }

        [Fact]
        public async Task ApplyTrapOverlayAsync_UsesDefaultDeliveryTemperature_WhenOptionsTemperatureIsNull()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = null
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyTrapOverlayAsync("hello", "trap them", "clown-trap");

            // Assert
            Assert.Equal(LlmPhaseTemperatures.OverlayRewrite, transport.LastTemperature);
        }

        [Fact]
        public async Task ApplyTrapOverlayAsync_UsesOptionsTemperature_WhenProvided()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = 0.5
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyTrapOverlayAsync("hello", "trap them", "clown-trap");

            // Assert
            Assert.Equal(0.5, transport.LastTemperature);
        }

        [Fact]
        public async Task ApplyShadowCorruptionAsync_UsesDefaultDeliveryTemperature_WhenOptionsTemperatureIsNull()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = null
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyShadowCorruptionAsync("hello", "corrupt them", ShadowStatType.Fixation);

            // Assert
            Assert.Equal(LlmPhaseTemperatures.OverlayRewrite, transport.LastTemperature);
        }

        [Fact]
        public async Task ApplyShadowCorruptionAsync_UsesOptionsTemperature_WhenProvided()
        {
            // Arrange
            var transport = new TemperatureTrackingTransport();
            var options = new PinderLlmAdapterOptions
            {
                GameDefinition = GameDefinition.PinderDefaults,
                DeliveryTemperature = 0.5
            };
            var adapter = new PinderLlmAdapter(transport, options);

            // Act
            await adapter.ApplyShadowCorruptionAsync("hello", "corrupt them", ShadowStatType.Fixation);

            // Assert
            Assert.Equal(0.5, transport.LastTemperature);
        }

        [Fact]
        public void PinderLlmAdapterOptions_DefaultsToCanonicalLlmPhaseTemperature()
        {
            var options = new PinderLlmAdapterOptions();

            Assert.Equal(LlmPhaseTemperatures.Default, options.Temperature);
        }

        [Fact]
        public void PinderLlmAdapter_SourceUsesCanonicalTemperatureRegistry()
        {
            string source = File.ReadAllText(FindRepoFile("src", "Pinder.LlmAdapters", "PinderLlmAdapter.cs"));

            Assert.DoesNotContain("DefaultDialogueOptionsTemperature", source);
            Assert.DoesNotContain("DefaultDeliveryTemperature", source);
            Assert.DoesNotContain("DefaultDateeResponseTemperature", source);
            Assert.Contains("PinderLlmAdapterTemperatureSource", source);
            Assert.DoesNotContain("LlmPhaseTemperatures.", source);
        }

        [Fact]
        public void PinderLlmAdapterTemperatureSource_ResolvesEveryTypedPhase()
        {
            var options = new PinderLlmAdapterOptions
            {
                Temperature = 0.3,
                DialogueOptionsTemperature = 1.0,
                DeliveryTemperature = 0.5,
                DateeResponseTemperature = 0.8
            };
            var source = new PinderLlmAdapterTemperatureSource(options);
            var expected = new System.Collections.Generic.Dictionary<PinderLlmAdapterPhase, double>
            {
                [PinderLlmAdapterPhase.DialogueOptions] = 1.0,
                [PinderLlmAdapterPhase.DateeResponse] = 0.8,
                [PinderLlmAdapterPhase.InterestChangeBeat] = 0.3,
                [PinderLlmAdapterPhase.OverlayRewrite] = 0.5,
                [PinderLlmAdapterPhase.SuccessImprovement] = LlmPhaseTemperatures.SuccessImprovement,
                [PinderLlmAdapterPhase.SteeringQuestion] = LlmPhaseTemperatures.SteeringQuestion,
                [PinderLlmAdapterPhase.HorninessQuestion] = LlmPhaseTemperatures.HorninessQuestion,
            };

            foreach (PinderLlmAdapterPhase phase in System.Enum.GetValues(typeof(PinderLlmAdapterPhase)))
            {
                Assert.True(expected.ContainsKey(phase), $"Missing temperature assertion for {phase}.");
                Assert.Equal(expected[phase], source.For(phase));
            }
        }

        [Theory]
        [InlineData("dialogue_options")]
        [InlineData("datee_response")]
        [InlineData("interest_change_beat")]
        [InlineData("success_improvement")]
        [InlineData("steering_question")]
        [InlineData("horniness_question")]
        public async Task AdapterPhase_ForwardsConfiguredTemperatureToTransport(string phase)
        {
            const double defaultTemperature = 0.63;
            const double dialogueTemperature = 0.61;
            const double dateeTemperature = 0.62;
            string response = phase == "dialogue_options"
                ? @"{
  ""schema_version"": ""dialogue_options.v1"",
  ""options"": [
    { ""stat"": ""CHARM"", ""text"": ""A valid option"", ""callback"": null, ""combo"": null }
  ]
}"
                : "A valid response";
            var transport = new TemperatureTrackingTransport(response);
            var adapter = new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = new GameDefinition(
                        name: "Pinder",
                        gameMasterPrompt: "gm",
                        playerAvatarRoleDescription: "player role",
                        dateeRoleDescription: "datee role",
                        steeringPrompt: "Conversation: {conversation_history}\nAsk about {delivered_message}",
                        horninessPrompt: "Conversation: {conversation_history}\nAsk about {delivered_message}",
                        maxDialogueOptions: 1,
                        maxTurns: 30,
                        maxDeliveryWords: 80),
                    Temperature = defaultTemperature,
                    DialogueOptionsTemperature = dialogueTemperature,
                    DateeResponseTemperature = dateeTemperature,
                    MaxContractViolationRetries = 0,
                    StatDeliveryInstructions = StatDeliveryInstructions.LoadFrom(@"
delivery_instructions:
  charm:
    strong: ""rewrite {delivered_message}""
  horniness_overlay:
    fumble: ""mock horniness""
shadow_corruption:
  madness:
    fumble: ""mock madness""
success_improvement_prompt_template: |-
  Conversation: {conversation_history}
  Rewrite {delivered_message} for {stat} at {tier}/{tier_upper}: {instruction}
")
                });

            double expected;
            switch (phase)
            {
                case "dialogue_options":
                    await adapter.GetDialogueOptionsAsync(new DialogueContext(
                        "player", "datee", Array.Empty<(string Sender, string Text)>(), "",
                        Array.Empty<string>(), 10, playerName: "Player", dateeName: "Datee",
                        availableStats: new[] { StatType.Charm }, maxDialogueOptions: 1));
                    expected = dialogueTemperature;
                    break;
                case "datee_response":
                    await adapter.GetDateeResponseAsync(new DateeContext(
                        "datee", Array.Empty<(string Sender, string Text)>(), "",
                        Array.Empty<string>(), 10, "hello", 10, 11, 0,
                        playerName: "Player", dateeName: "Datee"));
                    expected = dateeTemperature;
                    break;
                case "interest_change_beat":
                    await adapter.GetInterestChangeBeatAsync(new InterestChangeContext(
                        "Datee", 10, 16, InterestState.VeryIntoIt));
                    expected = defaultTemperature;
                    break;
                case "success_improvement":
                    await adapter.GetSuccessImprovementAsync(new SuccessImprovementContext(
                        "player", "Datee", "Player", "hello", StatType.Charm, "strong",
                        Array.Empty<(string Sender, string Text)>()));
                    expected = LlmPhaseTemperatures.SuccessImprovement;
                    break;
                case "steering_question":
                    await adapter.GetSteeringQuestionAsync(new SteeringContext(
                        "player", "Datee", "Player", "hello",
                        Array.Empty<(string Sender, string Text)>()));
                    expected = LlmPhaseTemperatures.SteeringQuestion;
                    break;
                case "horniness_question":
                    await adapter.GetHorninessQuestionAsync(new HorninessQuestionContext(
                        "player", "Datee", "Player", "hello",
                        Array.Empty<(string Sender, string Text)>()));
                    expected = LlmPhaseTemperatures.HorninessQuestion;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown phase {phase}");
            }

            Assert.Equal(expected, transport.LastTemperature);
        }

        private static string FindRepoFile(params string[] segments)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, Path.Combine(segments));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("Could not find repo file.", Path.Combine(segments));
        }
    }
}
