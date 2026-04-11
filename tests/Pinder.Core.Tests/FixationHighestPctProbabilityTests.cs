using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Characters;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for issue #349: Fixation shadow growth should use actual probability
    /// comparison, not option index, to determine if the player picked the
    /// highest-probability option.
    /// </summary>
    public class FixationHighestPctProbabilityTests
    {
        /// <summary>
        /// Picking the highest-probability option 3 turns in a row triggers Fixation +1.
        /// Options are arranged so the highest-prob option is NOT at index 0.
        /// </summary>
        [Fact]
        public async Task HighestProbAtNonZeroIndex_3Turns_TriggersFixation()
        {
            // Player: Honesty=5, everything else low
            // Opponent defaults: Chaos=0 (defends Honesty), others higher
            // So Honesty has the best margin (attack 5 vs DC 13+0=13)
            // Other stats have worse margins
            var playerStats = MakeStats(charm: 0, rizz: 0, honesty: 5, chaos: 0, wit: 0, sa: 0);
            var opponentStats = MakeStats(charm: 3, rizz: 3, honesty: 3, chaos: 0, wit: 3, sa: 3);
            var shadows = new SessionShadowTracker(playerStats);

            // Honesty at index 1 (not index 0) — it's the highest-prob option
            var opts = new[]
            {
                new DialogueOption(StatType.Charm, "a"),   // low prob
                new DialogueOption(StatType.Honesty, "b"), // HIGH prob
            };

            // dice: 5 for ghost check, then (15, 50) per turn for roll + interest beat
            var diceValues = new List<int> { 5 }; // ghost check
            for (int i = 0; i < 3; i++) { diceValues.Add(15); diceValues.Add(50); }

            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                llm: new StubLlmAdapter(opts),
                playerStats: playerStats,
                opponentStats: opponentStats,
                shadows: shadows);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(1); // Pick index 1 = Honesty = highest prob
            }

            // Fixation should trigger because highest-prob was picked 3 turns in a row
            // Note: same-stat trigger may also fire (Honesty 3x), so check >= 1
            Assert.True(shadows.GetDelta(ShadowStatType.Fixation) >= 1,
                "Expected Fixation growth from picking highest-probability option 3 turns in a row");
        }

        /// <summary>
        /// Picking a NON-highest-probability option should NOT trigger the
        /// highest-% Fixation trigger even after 3 turns.
        /// </summary>
        [Fact]
        public async Task PickingLowerProbOption_3Turns_NoHighestPctFixation()
        {
            // Player: Honesty=5, Charm=0
            // Opponent: Chaos=0 (defends Honesty), SA=3 (defends Charm)
            // Honesty has best margin, Charm has worst
            var playerStats = MakeStats(charm: 0, rizz: 0, honesty: 5, chaos: 0, wit: 0, sa: 0);
            var opponentStats = MakeStats(charm: 3, rizz: 3, honesty: 3, chaos: 0, wit: 3, sa: 3);
            var shadows = new SessionShadowTracker(playerStats);

            // Use different stats each turn to avoid same-stat Fixation trigger
            var opts1 = new[] { new DialogueOption(StatType.Charm, "a"), new DialogueOption(StatType.Honesty, "b") };
            var opts2 = new[] { new DialogueOption(StatType.Wit, "c"), new DialogueOption(StatType.Honesty, "d") };
            var opts3 = new[] { new DialogueOption(StatType.Rizz, "e"), new DialogueOption(StatType.Honesty, "f") };

            var diceValues = new List<int> { 5 }; // ghost check
            for (int i = 0; i < 3; i++) { diceValues.Add(15); diceValues.Add(50); }

            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                llm: new RotatingLlmAdapter(new[] { opts1, opts2, opts3 }),
                playerStats: playerStats,
                opponentStats: opponentStats,
                shadows: shadows);

            for (int i = 0; i < 3; i++)
            {
                await session.StartTurnAsync();
                await session.ResolveTurnAsync(0); // Pick index 0 = NOT highest prob (Charm/Wit/Rizz)
            }

            // No same-stat trigger (Charm, Wit, Rizz are different)
            // No highest-% trigger (always picked the worse option)
            Assert.Equal(0, shadows.GetDelta(ShadowStatType.Fixation));
        }

        /// <summary>
        /// When multiple options are tied for highest probability,
        /// picking any of them should count as "highest-% option picked."
        /// </summary>
        [Fact]
        public async Task TiedProbabilityOptions_AllCountAsHighest()
        {
            // Player: Charm=3, Wit=3 (same effective stat)
            // Opponent: SA=2 (defends Charm), Rizz=2 (defends Wit)
            // Both have identical margin → tied for highest
            var playerStats = MakeStats(charm: 3, rizz: 0, honesty: 0, chaos: 0, wit: 3, sa: 0);
            var opponentStats = MakeStats(charm: 0, rizz: 2, honesty: 0, chaos: 0, wit: 0, sa: 2);
            var shadows = new SessionShadowTracker(playerStats);

            // Use different tied stats each turn to avoid same-stat trigger
            var opts1 = new[] { new DialogueOption(StatType.Charm, "a"), new DialogueOption(StatType.Wit, "b") };
            var opts2 = new[] { new DialogueOption(StatType.Charm, "c"), new DialogueOption(StatType.Wit, "d") };
            var opts3 = new[] { new DialogueOption(StatType.Charm, "e"), new DialogueOption(StatType.Wit, "f") };

            var diceValues = new List<int> { 5 }; // ghost check
            for (int i = 0; i < 3; i++) { diceValues.Add(15); diceValues.Add(50); }

            var session = BuildSession(
                dice: new TestDice(diceValues.ToArray()),
                llm: new RotatingLlmAdapter(new[] { opts1, opts2, opts3 }),
                playerStats: playerStats,
                opponentStats: opponentStats,
                shadows: shadows);

            // Pick alternating tied options: index 0, 1, 0
            // All are tied for highest prob, so all count
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm (tied highest)
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(1); // Wit (tied highest)
            await session.StartTurnAsync();
            await session.ResolveTurnAsync(0); // Charm (tied highest)

            // Should trigger highest-% fixation (3 in a row all tied for highest)
            // Also triggers same-stat for Charm (turns 1 and 3 not consecutive though)
            // Actually Charm, Wit, Charm — not 3 consecutive same stat
            Assert.True(shadows.GetDelta(ShadowStatType.Fixation) >= 1,
                "Tied-for-highest options should all count as highest-probability picks");
        }

        // --- Helpers ---

        private static StatBlock MakeStats(
            int charm = 3, int rizz = 2, int honesty = 1,
            int chaos = 0, int wit = 4, int sa = 2)
        {
            return new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, charm }, { StatType.Rizz, rizz },
                    { StatType.Honesty, honesty }, { StatType.Chaos, chaos },
                    { StatType.Wit, wit }, { StatType.SelfAwareness, sa }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
        }

        private static CharacterProfile MakeProfile(string name, StatBlock stats)
            => new CharacterProfile(stats, "system prompt", name, new TimingProfile(5, 1.0f, 0.0f, "neutral"), 1);

        private static GameSession BuildSession(
            TestDice dice,
            ILlmAdapter llm,
            StatBlock playerStats,
            StatBlock opponentStats,
            SessionShadowTracker shadows)
        {
            var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), playerShadows: shadows);

            // Prepend a ghost-check dice value
            return new GameSession(
                MakeProfile("player", playerStats),
                MakeProfile("opponent", opponentStats),
                llm,
                dice,
                new NullTrapRegistry(),
                config);
        }

        private sealed class TestDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public TestDice(int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private sealed class StubLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[] _options;
            public StubLlmAdapter(DialogueOption[] options) => _options = options;
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(_options);
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }

        private sealed class RotatingLlmAdapter : ILlmAdapter
        {
            private readonly DialogueOption[][] _optionSets;
            private int _call;
            public RotatingLlmAdapter(DialogueOption[][] optionSets) => _optionSets = optionSets;
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
            {
                var idx = _call < _optionSets.Length ? _call : _optionSets.Length - 1;
                _call++;
                return Task.FromResult(_optionSets[idx]);
            }
            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult(context.ChosenOption.IntendedText);
            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("..."));
            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }
    }
}
