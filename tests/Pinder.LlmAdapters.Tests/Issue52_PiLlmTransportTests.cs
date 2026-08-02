using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pi.AI;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Pi;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public sealed class Issue52_PiLlmTransportTests
    {
        [Fact]
        public async Task SendStreamAsync_YieldsPiTextDeltasAndRecordsUsage()
        {
            var final = Response("hello world");
            final.Usage = new Usage { Input = 10, Output = 3, CacheRead = 2, CacheWrite = 1, Cost = new UsageCost() };
            var transport = new PiLlmTransport(
                Model("model-1"),
                (_, __, ___) => Task.FromResult(final),
                (_, __, ___) =>
                {
                    AssistantMessageEventStream stream = EventStreams.CreateAssistantMessageEventStream();
                    stream.Push(new TextStartEvent(0, final));
                    stream.Push(new TextDeltaEvent(0, "hello ", final));
                    stream.Push(new TextDeltaEvent(0, "world", final));
                    stream.Push(new TextEndEvent(0, "hello world", final));
                    stream.Push(new AssistantMessageDoneEvent(StopReason.Stop, final));
                    return stream;
                });
            var chunks = new List<string>();

            await foreach (string chunk in transport.SendStreamAsync("system", "user")) chunks.Add(chunk);

            Assert.Equal(new[] { "hello ", "world" }, chunks);
            Pinder.Core.Interfaces.SessionTokenUsage usage = transport.GetSessionUsage();
            Assert.Equal(10, usage.InputTokens);
            Assert.Equal(2, usage.CacheReadInputTokens);
            Assert.Equal(1, usage.CacheCreationInputTokens);
            Assert.Equal(3, usage.OutputTokens);
            Assert.Equal(1, usage.CallCount);
        }

        [Fact]
        public async Task SendStructuredAsync_RequiresOneToolAndReturnsItsArguments()
        {
            Context? capturedContext = null;
            ModelsSimpleStreamOptions? capturedOptions = null;
            string? observedPhase = null;
            var response = new AssistantMessage(
                new IAssistantMessageContent[]
                {
                    new ToolCall("call-1", "datee_reaction", new Dictionary<string, object?>
                    {
                        ["emotion"] = "suspicion",
                        ["intensity"] = 4L
                    })
                },
                KnownApi.OpenAIResponses,
                KnownProvider.OpenAI,
                "model-1",
                Usage.Zero,
                StopReason.ToolUse,
                1234L);
            var transport = new PiLlmTransport(
                Model("model-1"),
                (_, context, options) =>
                {
                    capturedContext = context;
                    capturedOptions = options;
                    return Task.FromResult(response);
                },
                optionsFactory: _ => new ModelsSimpleStreamOptions(),
                responseObserver: (_, phase) => observedPhase = phase);
            var request = new Pinder.Core.Interfaces.StructuredLlmRequest(
                "datee reaction",
                "1",
                "{\"type\":\"object\",\"properties\":{\"emotion\":{\"type\":\"string\",\"enum\":[\"suspicion\",\"anger\"]},\"intensity\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5}},\"required\":[\"emotion\",\"intensity\"],\"additionalProperties\":false}",
                "system",
                "user",
                0.2,
                256,
                "emotional_director");

            Pinder.Core.Interfaces.StructuredLlmResponse result = await transport.SendStructuredAsync(request);

            Assert.True(result.UsedNativeStructuredOutput);
            Assert.Equal("pi_required_tool", result.ValidationMode);
            Assert.Equal("emotional_director", observedPhase);
            Assert.Equal("required", capturedOptions!.Extra["toolChoice"]);
            Tool tool = Assert.Single(capturedContext!.Tools!);
            Assert.Equal("datee_reaction", tool.Name);
            Assert.Equal(new[] { "emotion", "intensity" }, tool.Parameters.Required);
            Assert.False(tool.Parameters.AdditionalPropertiesAllowed);
            Assert.Equal(1m, tool.Parameters.Properties!["intensity"].Minimum);
            using JsonDocument json = JsonDocument.Parse(result.JsonText);
            Assert.Equal("suspicion", json.RootElement.GetProperty("emotion").GetString());
            Assert.Equal(4, json.RootElement.GetProperty("intensity").GetInt32());
        }

        [Fact]
        public async Task SendStructuredAsync_MarksTextOnlyResponseAsFallback()
        {
            var transport = new PiLlmTransport(
                Model("model-1"),
                (_, __, ___) => Task.FromResult(Response("{\"ok\":true}")));
            var request = new Pinder.Core.Interfaces.StructuredLlmRequest(
                "result", "1", "{\"type\":\"object\"}", "system", "user", 0.2, 128, "phase");

            Pinder.Core.Interfaces.StructuredLlmResponse result = await transport.SendStructuredAsync(request);

            Assert.False(result.UsedNativeStructuredOutput);
            Assert.Equal("pi_text_fallback", result.ValidationMode);
            Assert.Equal("{\"ok\":true}", result.JsonText);
        }

        [Fact]
        public async Task PublicBoundary_CompletesThroughPiModelsCollection()
        {
            FauxProviderHandle faux = Faux.Provider();
            faux.SetResponses(new[]
            {
                FauxResponseStep.FromMessage(Faux.AssistantMessage("integrated answer"))
            });
            var models = new ModelsCollection();
            models.SetProvider(faux.Provider);
            var transport = new PiLlmTransport(models, faux.GetModel()!);

            string result = await transport.SendAsync("system", "user", 0.4, 256, "integration");

            Assert.Equal("integrated answer", result);
            Assert.Equal(1, faux.State.CallCount);
        }

        [Fact]
        public async Task SendAsync_PreservesPromptsAndMapsRequestOptions()
        {
            Model? capturedModel = null;
            Context? capturedContext = null;
            ModelsSimpleStreamOptions? capturedOptions = null;
            string? capturedPhase = null;
            var model = Model("model-1");
            var transport = new PiLlmTransport(
                model,
                (selectedModel, context, options) =>
                {
                    capturedModel = selectedModel;
                    capturedContext = context;
                    capturedOptions = options;
                    return Task.FromResult(Response("answer"));
                },
                phase =>
                {
                    capturedPhase = phase;
                    return new ModelsSimpleStreamOptions { MaxRetries = 0 };
                },
                () => 1234L);

            string result = await transport.SendAsync("system\r\nexact", "user\nexact", 0.25, 321, "datee");

            Assert.Equal("answer", result);
            Assert.Same(model, capturedModel);
            Assert.Equal("system\r\nexact", capturedContext!.SystemPrompt);
            UserMessage user = Assert.IsType<UserMessage>(Assert.Single(capturedContext.Messages));
            Assert.Equal("user\nexact", user.Content.Text);
            Assert.Equal(1234L, user.Timestamp);
            Assert.Equal(0.25, capturedOptions!.Temperature);
            Assert.Equal(321, capturedOptions.MaxTokens);
            Assert.Equal(0, capturedOptions.MaxRetries);
            Assert.Equal("datee", capturedPhase);
        }

        [Fact]
        public async Task SendAsync_PropagatesCancellationToPiOptions()
        {
            var cancellation = new CancellationTokenSource();
            var transport = new PiLlmTransport(
                Model("model-1"),
                (_, __, options) =>
                {
                    Assert.Equal(cancellation.Token, options.CancellationToken);
                    cancellation.Cancel();
                    return Task.FromResult(Response("late answer"));
                });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                transport.SendAsync("system", "user", ct: cancellation.Token));
        }

        [Fact]
        public async Task SendAsync_RejectsProviderErrorWithoutReturningItsText()
        {
            var transport = new PiLlmTransport(
                Model("model-1"),
                (_, __, ___) => Task.FromResult(Response("unsafe", StopReason.Error, "provider failed")));

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.SendAsync("system", "user"));

            Assert.Equal("provider failed", error.Message);
        }

        [Fact]
        public async Task SendAsync_RejectsResponseWithoutTextContent()
        {
            var transport = new PiLlmTransport(
                Model("model-1"),
                (_, __, ___) => Task.FromResult(Response(string.Empty)));

            await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendAsync("system", "user"));
        }

        private static Model Model(string id)
        {
            return new Model
            {
                Id = id,
                Name = id,
                Api = KnownApi.OpenAIResponses,
                Provider = KnownProvider.OpenAI
            };
        }

        private static AssistantMessage Response(
            string text,
            StopReason? stopReason = null,
            string? errorMessage = null)
        {
            return new AssistantMessage(
                new List<IAssistantMessageContent> { new TextContent(text) },
                KnownApi.OpenAIResponses,
                KnownProvider.OpenAI,
                "model-1",
                Usage.Zero,
                stopReason ?? StopReason.Stop,
                1234L)
            {
                ErrorMessage = errorMessage
            };
        }
    }
}
