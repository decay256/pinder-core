using System;
using System.Collections.Generic;
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
