using System;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for Issue #542: GameSession creates LLM conversation session at start.
    /// Verifies IStatefulLlmAdapter detection, StartConversation wiring, and
    /// backward compatibility with non-stateful adapters.
    /// </summary>
    public class Issue542_StatefulSessionTests
    {
        private static StatBlock MakeStatBlock(int allStats = 2, int allShadow = 0)
        {
            return TestHelpers.MakeStatBlock(allStats, allShadow);
        }

        private static CharacterProfile MakeProfile(string name, int allStats = 2)
        {
            return new CharacterProfile(
                stats: MakeStatBlock(allStats),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        /// <summary>
        /// Mock stateful adapter that records StartConversation calls.
        /// </summary>
        private sealed class MockStatefulAdapter : IStatefulLlmAdapter
        {
            public string? LastSystemPrompt { get; private set; }
            public int StartConversationCallCount { get; private set; }
            public bool HasActiveConversation => LastSystemPrompt != null;

            public void StartConversation(string systemPrompt)
            {
                LastSystemPrompt = systemPrompt;
                StartConversationCallCount++;
            }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                return Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey"),
                    new DialogueOption(StatType.Honesty, "Real talk"),
                    new DialogueOption(StatType.Wit, "Clever"),
                    new DialogueOption(StatType.Chaos, "Wild")
                });
            }

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                return Task.FromResult(context.ChosenOption.IntendedText);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                return Task.FromResult(new OpponentResponse("..."));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
            {
                return Task.FromResult<string?>(null);
            }
        }

        [Fact]
        public void Constructor_WithStatefulAdapter_CallsStartConversation()
        {
            var adapter = new MockStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            // horniness roll (1d10)
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            Assert.Equal(1, adapter.StartConversationCallCount);
            Assert.True(adapter.HasActiveConversation);
        }

        [Fact]
        public void Constructor_WithStatefulAdapter_SystemPromptContainsBothProfiles()
        {
            var adapter = new MockStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            Assert.NotNull(adapter.LastSystemPrompt);
            Assert.Contains("You are Velvet.", adapter.LastSystemPrompt);
            Assert.Contains("You are Sable.", adapter.LastSystemPrompt);
            Assert.Contains("\n\n---\n\n", adapter.LastSystemPrompt);
        }

        [Fact]
        public void Constructor_WithStatefulAdapter_SystemPromptFormat()
        {
            var adapter = new MockStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            var expected = "You are Velvet.\n\n---\n\nYou are Sable.";
            Assert.Equal(expected, adapter.LastSystemPrompt);
        }

        [Fact]
        public void Constructor_WithNullLlmAdapter_DoesNotCallStartConversation()
        {
            // NullLlmAdapter does NOT implement IStatefulLlmAdapter
            var adapter = new NullLlmAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);

            // Should construct without error — no stateful session started
            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            // NullLlmAdapter has no HasActiveConversation — this just verifies no exception
            Assert.NotNull(session);
        }

        [Fact]
        public void Constructor_WithStatefulAdapter_AndConfig_CallsStartConversation()
        {
            var adapter = new MockStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);
            var config = new GameSessionConfig(startingInterest: 12);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry(), config);

            Assert.Equal(1, adapter.StartConversationCallCount);
            Assert.True(adapter.HasActiveConversation);
        }

        [Fact]
        public void Constructor_WithStatefulAdapter_NullConfig_CallsStartConversation()
        {
            var adapter = new MockStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry(), null);

            Assert.Equal(1, adapter.StartConversationCallCount);
            Assert.True(adapter.HasActiveConversation);
        }

        [Fact]
        public void IStatefulLlmAdapter_ExtendsILlmAdapter()
        {
            // Verify the interface hierarchy
            Assert.True(typeof(ILlmAdapter).IsAssignableFrom(typeof(IStatefulLlmAdapter)));
        }

        [Fact]
        public void NullLlmAdapter_DoesNotImplementIStatefulLlmAdapter()
        {
            var adapter = new NullLlmAdapter();
            Assert.False(adapter is IStatefulLlmAdapter);
        }

        [Fact]
        public async Task StatefulAdapter_GameSessionTurnStillWorks()
        {
            var adapter = new MockStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            // horniness roll (1d10), then d20=15, d100=50 for turn
            var dice = new FixedDice(5, 15, 50);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            // Verify session works normally after stateful init
            var turnStart = await session.StartTurnAsync();
            Assert.NotNull(turnStart);
            Assert.Equal(4, turnStart.Options.Length);
        }

        [Fact]
        public void FiveParamConstructor_WithStatefulAdapter_CallsStartConversation()
        {
            // The 5-param constructor delegates to 6-param with null config
            var adapter = new MockStatefulAdapter();
            var player = MakeProfile("Velvet");
            var opponent = MakeProfile("Sable");
            var dice = new FixedDice(5);

            var session = new GameSession(player, opponent, adapter, dice, new NullTrapRegistry());

            Assert.Equal(1, adapter.StartConversationCallCount);
        }
    }
}
