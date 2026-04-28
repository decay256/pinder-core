using System.Collections.Generic;
using System.Linq;
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
    /// Issue #333: regression tests for the turn-0 scene-setting entries
    /// (player bio, opponent bio, LLM-generated outfit description) seeded
    /// onto <see cref="GameSession.ConversationHistory"/> via
    /// <see cref="GameSession.SeedSceneEntries"/>.
    /// </summary>
    [Trait("Category", "Core")]
    public class Issue333_TurnZeroSceneEntriesTests
    {
        [Fact]
        public void SeedSceneEntries_appends_three_scene_entries_to_history()
        {
            var session = MakeSession(out _);

            session.SeedSceneEntries(
                "Player bio text.",
                "Opponent bio text.",
                "Both wear something quietly out of fashion.");

            var history = session.ConversationHistory;
            Assert.Equal(3, history.Count);
            Assert.All(history, e => Assert.Equal(Senders.Scene, e.Sender));
            Assert.Equal("Player bio text.",                                history[0].Text);
            Assert.Equal("Opponent bio text.",                              history[1].Text);
            Assert.Equal("Both wear something quietly out of fashion.",    history[2].Text);
        }

        [Theory]
        [InlineData(null,    "B", "C", new[] { "B", "C" })]
        [InlineData("",      "B", "C", new[] { "B", "C" })]
        [InlineData("   ",   "B", "C", new[] { "B", "C" })]
        [InlineData("A",     null, "C", new[] { "A", "C" })]
        [InlineData("A",     "B", "",   new[] { "A", "B" })]
        public void SeedSceneEntries_skips_empty_entries(string a, string b, string c, string[] expected)
        {
            var session = MakeSession(out _);
            session.SeedSceneEntries(a, b, c);

            Assert.Equal(expected.Length, session.ConversationHistory.Count);
            Assert.Equal(
                expected,
                session.ConversationHistory.Select(e => e.Text).ToArray());
        }

        [Fact]
        public async Task SeedSceneEntries_throws_after_first_turn_resolved()
        {
            var session = MakeSession(out _);

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            Assert.Throws<System.InvalidOperationException>(() =>
                session.SeedSceneEntries("a", "b", "c"));
        }

        [Fact]
        public async Task Scene_entries_are_excluded_from_LLM_context_history()
        {
            var session = MakeSession(out var llm);
            session.SeedSceneEntries("Player bio.", "Opponent bio.", "Outfit prose.");

            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0);

            // The DeliveryContext capture is the proof: scene entries
            // must NOT appear in the conversation history fed to the LLM.
            Assert.NotNull(llm.CapturedDeliveryContext);
            Assert.DoesNotContain(
                llm.CapturedDeliveryContext!.ConversationHistory,
                e => e.Sender == Senders.Scene);

            // BUT the public ConversationHistory still includes them.
            Assert.Contains(session.ConversationHistory, e => e.Sender == Senders.Scene);
        }

        [Fact]
        public void Scene_entries_round_trip_through_ResimulateData()
        {
            var session = MakeSession(out _);
            session.SeedSceneEntries("a", "b", "c");

            // Snapshot the history into a ResimulateData and rebuild.
            var snapshotHistory = session.ConversationHistory
                .Select(e => (e.Sender, e.Text))
                .ToList();
            var resim = new ResimulateData
            {
                ConversationHistory = snapshotHistory,
                ShadowValues = new Dictionary<string, int>(),
            };

            var freshSession = MakeSession(out _);
            freshSession.RestoreState(resim, new NullTrapRegistry());

            Assert.Equal(3, freshSession.ConversationHistory.Count);
            Assert.All(freshSession.ConversationHistory,
                e => Assert.Equal(Senders.Scene, e.Sender));
        }

        // ── helpers ──────────────────────────────────────────────────────

        private static GameSession MakeSession(out CapturingLlm llm)
        {
            llm = new CapturingLlm();
            var dice = new FixedDice(15, 5);
            return new GameSession(
                MakeProfile("Sable"), MakeProfile("Brick"),
                llm, dice, new NullTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));
        }

        private static CharacterProfile MakeProfile(string name)
        {
            return new CharacterProfile(
                stats: TestHelpers.MakeStatBlock(2),
                assembledSystemPrompt: $"You are {name}.",
                displayName: name,
                timing: new TimingProfile(5, 0.0f, 0.0f, "neutral"),
                level: 1);
        }

        private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _rolls;
            public FixedDice(params int[] rolls) { _rolls = new Queue<int>(rolls); }
            public int Roll(int sides) => _rolls.Count > 0 ? _rolls.Dequeue() : 1;
            public int RollWithAdvantage(int sides) => Roll(sides);
            public int RollWithDisadvantage(int sides) => Roll(sides);
        }

        private sealed class CapturingLlm : ILlmAdapter
        {
            public DeliveryContext? CapturedDeliveryContext { get; private set; }
            public OpponentContext? CapturedOpponentContext { get; private set; }

            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(new[]
                {
                    new DialogueOption(StatType.Charm, "Hey there"),
                    new DialogueOption(StatType.Rizz, "Nice"),
                    new DialogueOption(StatType.Honesty, "Real talk"),
                    new DialogueOption(StatType.Wit, "Clever")
                });

            public Task<string> DeliverMessageAsync(DeliveryContext context)
            {
                CapturedDeliveryContext = context;
                return Task.FromResult(context.ChosenOption.IntendedText);
            }

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
            {
                CapturedOpponentContext = context;
                return Task.FromResult(new OpponentResponse("Reply"));
            }

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
            public Task<string> ApplyHorninessOverlayAsync(string message, string instruction, string? opponentContext = null)
                => Task.FromResult(message);
            public Task<string> ApplyShadowCorruptionAsync(string message, string instruction, ShadowStatType shadow)
                => Task.FromResult(message);
        }
    }
}
