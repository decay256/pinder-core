using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Tests.Phase0;
using Pinder.LlmAdapters;
using Xunit;

namespace Pinder.Core.Tests.Conversation
{
    public sealed class Issue1348_CurrentMessageOnceTests
    {
        private const string ValidDirectorJson =
            "{\"schema_version\":\"emotional_director.v1\",\"primary_emotion\":\"relief\",\"intensity\":\"moderate and steadily rising\",\"underlying_feeling\":\"fear of being dismissed\",\"interpretation\":\"reads the message as specific warmth that is probably meant for them\",\"impulse\":\"leans in with a careful question\",\"restraint\":\"keeps the reply tentative but available\",\"response_posture\":\"Writing from relief, turns warmer while still checking sincerity\"}";

        [Fact]
        public async Task DateeTransportPrompt_ContainsCurrentDeliveredMessageExactlyOnce()
        {
            const string currentDelivered = "ISSUE-1348-CURRENT-SENTINEL";
            var transport = QueuedTransport();
            transport.QueueDialogueOptions(CannedDialogueOptionsWith(currentDelivered));
            transport.QueueDatee("datee-one");

            var session = CreateSession(transport, new PlaybackDiceRoller(5, 16, 50));

            await session.StartTurnAsync();
            var result = await session.ResolveTurnAsync(0);

            var dateePrompt = AssertSingleDateePrompt(transport, 0);
            Assert.Equal(1, CountOccurrences(dateePrompt, result.DeliveredMessage));
            Assert.Equal(2, session.ConversationHistory.Count);
            Assert.Equal(("Player", result.DeliveredMessage), session.ConversationHistory[0]);
            Assert.Equal(("Datee", "datee-one"), session.ConversationHistory[1]);
        }

        [Fact]
        public async Task SecondTurnDateePrompt_ContainsPriorExchangeInOrderAndCurrentLineOnce()
        {
            const string turnOneDelivered = "ISSUE-1348-TURN-ONE";
            const string turnOneDatee = "ISSUE-1348-DATEE-ONE";
            const string turnTwoDelivered = "ISSUE-1348-TURN-TWO-CURRENT";
            const string turnTwoDatee = "ISSUE-1348-DATEE-TWO";

            var transport = QueuedTransport();
            QueueTurn(transport, turnOneDelivered, turnOneDatee);
            QueueTurn(transport, turnTwoDelivered, turnTwoDatee);

            var session = CreateSession(transport, new PlaybackDiceRoller(5, 16, 50, 16, 16, 50));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var secondDateePrompt = AssertSingleDateePrompt(transport, 1);
            AssertOrdered(secondDateePrompt,
                $"[T1|PLAYER AVATAR] \"{turnOneDelivered}\"",
                $"[T1|DATEE] \"{turnOneDatee}\"",
                "[CURRENT_TURN]",
                "PLAYER'S LAST MESSAGE",
                $"\"{turnTwoDelivered}\"");
            Assert.Equal(1, CountOccurrences(secondDateePrompt, turnTwoDelivered));
            Assert.Equal(new[]
            {
                ("Player", turnOneDelivered),
                ("Datee", turnOneDatee),
                ("Player", turnTwoDelivered),
                ("Datee", turnTwoDatee),
            }, session.ConversationHistory.Select(e => (e.Sender, e.Text)).ToArray());
        }

        [Fact]
        public async Task IdenticalDeliveredTextAcrossTurns_RemainsLegitimatelyRepeated()
        {
            const string repeatedDelivered = "ISSUE-1348-REPEATED-PLAYER-LINE";

            var transport = QueuedTransport();
            QueueTurn(transport, repeatedDelivered, "ISSUE-1348-DATEE-FIRST");
            QueueTurn(transport, repeatedDelivered, "ISSUE-1348-DATEE-SECOND");

            var session = CreateSession(transport, new PlaybackDiceRoller(5, 16, 50, 16, 16, 50));

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var secondDateePrompt = AssertSingleDateePrompt(transport, 1);
            Assert.Equal(2, CountOccurrences(secondDateePrompt, repeatedDelivered));
            Assert.Equal(2, session.ConversationHistory.Count(e => e.Sender == "Player" && e.Text == repeatedDelivered));
        }

        [Fact]
        public async Task SceneEntriesStayExcludedAndCurrentEventMetadataRemainsAttached()
        {
            const string playerScene = "ISSUE-1348-PLAYER-SCENE";
            const string dateeScene = "ISSUE-1348-DATEE-SCENE";
            const string currentDelivered = "ISSUE-1348-METADATA-CURRENT";

            var transport = QueuedTransport();
            QueueTurn(transport, currentDelivered, "metadata datee");
            var session = CreateSession(transport, new PlaybackDiceRoller(5, 16, 50));
            session.SeedSceneEntries(playerScene, dateeScene, outfitDescription: null);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            var dateePrompt = AssertSingleDateePrompt(transport, 0);
            Assert.DoesNotContain(playerScene, dateePrompt, StringComparison.Ordinal);
            Assert.DoesNotContain(dateeScene, dateePrompt, StringComparison.Ordinal);
            Assert.Contains("PLAYER'S LAST MESSAGE", dateePrompt, StringComparison.Ordinal);
            Assert.Contains($"\"{currentDelivered}\"", dateePrompt, StringComparison.Ordinal);
            Assert.Contains("Interest moved from", dateePrompt, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(dateePrompt, currentDelivered));
        }

        [Fact]
        public async Task DateeFailureStoresNoVisiblePlayerOrDateePair()
        {
            const string failedDelivered = "ISSUE-1348-FAILED-CURRENT";

            var transport = new FailingDateeTransport();
            transport.Inner.QueueDialogueOptions(CannedDialogueOptionsWith(failedDelivered));

            var session = CreateSession(transport, new PlaybackDiceRoller(5, 16, 50));

            await session.StartTurnAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ResolveTurnAsync(0));

            Assert.DoesNotContain(session.ConversationHistory, e => e.Text == failedDelivered);
            Assert.Empty(session.ConversationHistory);
            Assert.NotNull(session.CurrentDicePools);
        }

        private static RecordingLlmTransport QueuedTransport()
        {
            return new RecordingLlmTransport((phase, systemPrompt, userMessage) =>
                string.Equals(phase, LlmPhase.EmotionalDirector, StringComparison.Ordinal)
                    ? ValidDirectorJson
                    : string.Empty)
            { DefaultResponse = string.Empty };
        }

        private static void QueueTurn(RecordingLlmTransport transport, string delivered, string datee)
        {
            transport.QueueDialogueOptions(CannedDialogueOptionsWith(delivered));
            transport.QueueDatee(datee);
        }

        private static string CannedDialogueOptionsWith(string firstOptionText)
        {
            return "OPTION_1\n[STAT: Charm]\n\"" + firstOptionText + "\"\n\n" +
                "OPTION_2\n[STAT: Wit]\n\"secondary option\"\n\n" +
                "OPTION_3\n[STAT: Honesty]\n\"third option\"\n";
        }

        private static GameSession CreateSession(ILlmTransport transport, PlaybackDiceRoller dice)
        {
            return new GameSession(
                MakeProfile("Player"),
                MakeProfile("Datee"),
                Phase0Fixtures.MakeAdapter(transport),
                dice,
                new NullTrapRegistry(),
                Phase0Fixtures.MakeConfig());
        }

        private static CharacterProfile MakeProfile(string name)
        {
            return TestHelpers.MakeCharacterProfile(
                stats: TestHelpers.MakeStatBlock(2),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1,
                backstory: TestHelpers.MakeBackstory(),
                stakeLines: TestHelpers.MakeStakeLines());
        }

        private static string AssertSingleDateePrompt(RecordingLlmTransport transport, int index)
        {
            var dateeExchanges = transport.ExchangesByPhase(LlmPhase.DateeResponse);
            Assert.True(index >= 0 && index < dateeExchanges.Count,
                $"Expected datee prompt at index {index}, got {dateeExchanges.Count} datee exchanges.");
            return dateeExchanges[index].UserMessage;
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(needle))
                return 0;

            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static void AssertOrdered(string text, params string[] fragments)
        {
            int lastIndex = -1;
            foreach (var fragment in fragments)
            {
                int currentIndex = text.IndexOf(fragment, StringComparison.Ordinal);
                Assert.True(currentIndex > lastIndex,
                    $"Expected fragment after index {lastIndex}: {fragment}\nPrompt:\n{text}");
                lastIndex = currentIndex;
            }
        }

        private sealed class FailingDateeTransport : ILlmTransport
        {
            public RecordingLlmTransport Inner { get; } = new RecordingLlmTransport { DefaultResponse = string.Empty };

            public Task<string> SendAsync(
                string systemPrompt,
                string userMessage,
                double temperature = 0.9,
                int? maxTokens = null,
                string? phase = null,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(phase, LlmPhase.EmotionalDirector, StringComparison.Ordinal))
                {
                    return Task.FromResult(ValidDirectorJson);
                }

                if (string.Equals(phase, LlmPhase.DateeResponse, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("simulated datee failure");
                }

                return Inner.SendAsync(systemPrompt, userMessage, temperature, maxTokens, phase, ct);
            }
        }
    }
}
