using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    [Collection("PromptTraceSingleton")]
    public class IssuePromptHardcodingHorninessQuestionTests
    {
        [Fact]
        public async Task GetHorninessQuestion_UsesConfiguredGameDefinitionPrompt()
        {
            var transport = new CapturingTransport("how strong are your hands?");
            var adapter = CreateAdapter(
                transport,
                horninessPrompt:
                    "CONFIG HORNINESS for {player_name} to {datee_name}: \"{delivered_message}\"");

            var result = await adapter.GetHorninessQuestionAsync(Context());

            Assert.Equal("how strong are your hands?", result);
            var call = Assert.Single(transport.Calls);
            Assert.Contains("CONFIG HORNINESS for Player to Datee: \"nice jacket\"", call.UserMessage);
            Assert.DoesNotContain("{player_name}", call.UserMessage);
            Assert.DoesNotContain("{datee_name}", call.UserMessage);
            Assert.DoesNotContain("{delivered_message}", call.UserMessage);
        }

        [Fact]
        public async Task GetHorninessQuestion_MissingConfiguredPrompt_ThrowsBeforeTransportCall()
        {
            var transport = new CapturingTransport("unused");
            var adapter = CreateAdapter(transport, horninessPrompt: string.Empty);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => adapter.GetHorninessQuestionAsync(Context()));

            Assert.Contains("horniness_prompt", ex.Message);
            Assert.Contains("data/game-definition.yaml", ex.Message);
            Assert.Empty(transport.Calls);
        }

        [Fact]
        public async Task GetHorninessQuestion_EmptyModelOutput_ThrowsAndEmitsDegradation()
        {
            var events = new List<OverlayDegradedEvent>();
            var adapter = CreateAdapter(
                new CapturingTransport("   "),
                horninessPrompt: "configured prompt",
                onOverlayDegraded: events.Add);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => adapter.GetHorninessQuestionAsync(Context()));

            Assert.Contains("horniness_question output is empty", ex.Message);
            var evt = Assert.Single(events);
            Assert.Equal("horniness_question", evt.OverlayType);
            Assert.Equal("empty_output", evt.Reason);
            Assert.Equal(OverlayOutcome.Degraded, evt.Outcome);
        }

        [Fact]
        public void SourceDoesNotContainHorninessQuestionFallback()
        {
            string adapterSource = File.ReadAllText(FindRepoFile("src", "Pinder.LlmAdapters", "PinderLlmAdapter.cs"));
            string defaultsSource = File.ReadAllText(FindRepoFile("src", "Pinder.LlmAdapters", "GameDefinition.Defaults.cs"));
            string nullAdapterSource = File.ReadAllText(FindRepoFile("src", "Pinder.Core", "Conversation", "NullLlmAdapter.cs"));

            Assert.DoesNotContain("DefaultHorninessPrompt", adapterSource);
            Assert.DoesNotContain("DefaultHorninessPrompt", defaultsSource);
            Assert.DoesNotContain("your place or mine", adapterSource);
            Assert.DoesNotContain("your place or mine", nullAdapterSource);
        }

        private static HorninessQuestionContext Context()
        {
            return new HorninessQuestionContext(
                playerAvatarPrompt: "player profile",
                dateeName: "Datee",
                playerName: "Player",
                deliveredMessage: "nice jacket",
                conversationHistory: new[] { ("Datee", "I restore antique chairs.") });
        }

        private static PinderLlmAdapter CreateAdapter(
            ILlmTransport transport,
            string horninessPrompt,
            Action<OverlayDegradedEvent>? onOverlayDegraded = null)
        {
            return new PinderLlmAdapter(
                transport,
                new PinderLlmAdapterOptions
                {
                    GameDefinition = new GameDefinition(
                        name: "Pinder",
                        gameMasterPrompt: "game master",
                        playerAvatarRoleDescription: "player",
                        dateeRoleDescription: "datee",
                        horninessPrompt: horninessPrompt),
                    OnOverlayDegraded = onOverlayDegraded,
                });
        }

        private static string FindRepoFile(params string[] relativeParts)
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 12; i++)
            {
                string candidate = Path.Combine(Prepend(dir, relativeParts));
                if (File.Exists(candidate))
                    return candidate;

                string? parent = Directory.GetParent(dir)?.FullName;
                if (parent == null)
                    break;

                dir = parent;
            }

            throw new FileNotFoundException("Could not locate repo file.", Path.Combine(relativeParts));
        }

        private static string[] Prepend(string first, string[] rest)
        {
            var parts = new string[rest.Length + 1];
            parts[0] = first;
            Array.Copy(rest, 0, parts, 1, rest.Length);
            return parts;
        }

        private sealed class CapturingTransport : ILlmTransport
        {
            private readonly string _response;

            public CapturingTransport(string response)
            {
                _response = response;
            }

            public List<(string SystemPrompt, string UserMessage, string? Phase)> Calls { get; } =
                new List<(string, string, string?)>();

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int maxTokens = 1024,
                string? phase = null,
                CancellationToken ct = default)
            {
                Calls.Add((systemPrompt, userMessage, phase));
                return Task.FromResult(_response);
            }
        }
    }
}
