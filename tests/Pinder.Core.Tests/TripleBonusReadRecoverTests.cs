using System.Collections.Generic;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for #312: Triple combo bonus (+1) applied to Read and Recover rolls.
    /// </summary>
    public class TripleBonusReadRecoverTests
    {
        // What: Triple bonus active → Read roll gets +1 external bonus
        // Mutation: Fails if ExternalBonus on ReadResult.Roll is 0 when triple is active
        [Fact]
        public async Task ReadAsync_TripleBonusActive_RollHasExternalBonusOne()
        {
            // SA +0, dice rolls 10 → base total 10+0=10; with triple +1 → FinalTotal 11
            var session = MakeSession(diceValue: 10, saModifier: 0);
            SetupTripleBonus(session);

            var result = await session.ReadAsync();

            Assert.Equal(1, result.Roll.ExternalBonus);
        }

        // What: Triple bonus NOT active → Read roll has 0 external bonus
        // Mutation: Fails if ExternalBonus is non-zero when triple is not active
        [Fact]
        public async Task ReadAsync_NoTripleBonus_RollHasZeroExternalBonus()
        {
            var session = MakeSession(diceValue: 10, saModifier: 0);

            var result = await session.ReadAsync();

            Assert.Equal(0, result.Roll.ExternalBonus);
        }

        // What: Triple bonus can turn a Read failure into success (DC 12, roll 11 + 1 = 12 >= 12)
        // Mutation: Fails if the triple bonus doesn't affect the success calculation
        [Fact]
        public async Task ReadAsync_TripleBonusTurnsFailureIntoSuccess()
        {
            // SA +0, dice rolls 11 → base total 11, fails DC 12
            // With triple +1 → FinalTotal 12, succeeds DC 12
            var session = MakeSession(diceValue: 11, saModifier: 0);
            SetupTripleBonus(session);

            var result = await session.ReadAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.InterestValue);
        }

        // What: Without triple, same roll fails
        [Fact]
        public async Task ReadAsync_WithoutTripleBonus_SameRollFails()
        {
            // SA +0, dice rolls 11 → total 11 < DC 12 → failure
            var session = MakeSession(diceValue: 11, saModifier: 0);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
        }

        // What: Triple bonus consumed after Read (not available for next action)
        // Mutation: Fails if triple bonus persists after Read
        [Fact]
        public async Task ReadAsync_TripleBonusConsumedAfterRead()
        {
            var session = MakeSession(diceValue: 10, saModifier: 0);
            SetupTripleBonus(session);

            // First Read consumes triple
            await session.ReadAsync();

            // Second Read should NOT have triple bonus
            var result2 = await session.ReadAsync();
            Assert.Equal(0, result2.Roll.ExternalBonus);
        }

        // What: Triple bonus active → Recover roll gets +1 external bonus
        // Mutation: Fails if ExternalBonus on RecoverResult.Roll is 0 when triple is active
        [Fact]
        public async Task RecoverAsync_TripleBonusActive_RollHasExternalBonusOne()
        {
            // SA +0, dice rolls 10; need active trap for Recover
            var session = MakeSession(diceValue: 10, saModifier: 0);
            ActivateTrap(session);
            SetupTripleBonus(session);

            var result = await session.RecoverAsync();

            Assert.Equal(1, result.Roll.ExternalBonus);
        }

        // What: Triple bonus NOT active → Recover roll has 0 external bonus
        [Fact]
        public async Task RecoverAsync_NoTripleBonus_RollHasZeroExternalBonus()
        {
            var session = MakeSession(diceValue: 10, saModifier: 0);
            ActivateTrap(session);

            var result = await session.RecoverAsync();

            Assert.Equal(0, result.Roll.ExternalBonus);
        }

        // What: Triple bonus can turn a Recover failure into success
        [Fact]
        public async Task RecoverAsync_TripleBonusTurnsFailureIntoSuccess()
        {
            // SA +0, dice rolls 11 → base 11 < DC 12; with triple → 12 >= 12 → success
            var session = MakeSession(diceValue: 11, saModifier: 0);
            ActivateTrap(session);
            SetupTripleBonus(session);

            var result = await session.RecoverAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.ClearedTrapName);
        }

        // What: Triple bonus consumed after Recover
        [Fact]
        public async Task RecoverAsync_TripleBonusConsumedAfterRecover()
        {
            var session = MakeSession(diceValue: 10, saModifier: 0);
            ActivateTrap(session);
            SetupTripleBonus(session);

            await session.RecoverAsync();

            // Re-activate trap and check second Recover has no bonus
            ActivateTrap(session);
            var result2 = await session.RecoverAsync();
            Assert.Equal(0, result2.Roll.ExternalBonus);
        }

        // ======================== Helpers ========================

        /// <summary>
        /// Sets up triple bonus on the session's ComboTracker by recording
        /// 3 distinct stats (Charm, Wit, Honesty) with success on the 3rd.
        /// After this, HasTripleBonus is true for the next action.
        /// </summary>
        private static void SetupTripleBonus(GameSession session)
        {
            var trackerField = typeof(GameSession).GetField("_comboTracker",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var tracker = (ComboTracker)trackerField!.GetValue(session)!;

            // Record 3 distinct stats with no 2-stat combo overlap to trigger The Triple
            tracker.RecordTurn(StatType.Rizz, true);
            tracker.RecordTurn(StatType.SelfAwareness, true);
            tracker.RecordTurn(StatType.Chaos, true);

            // Verify triple bonus is active
            if (!tracker.HasTripleBonus)
                throw new System.InvalidOperationException("Triple bonus setup failed");
        }

        private static void ActivateTrap(GameSession session)
        {
            var trapsField = typeof(GameSession).GetField("_traps",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var trapState = (TrapState)trapsField!.GetValue(session)!;

            var trap = new TrapDefinition("test-trap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 3, "test trap active", "recover", "none");
            trapState.Activate(trap);
        }

        private static GameSession MakeSession(int diceValue, int saModifier)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 2 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, saModifier }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Horniness, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            var player = new CharacterProfile(stats, "system prompt", "player", timing, 1);
            var opponent = new CharacterProfile(stats, "system prompt", "opponent", timing, 1);

            return new GameSession(
                player,
                opponent,
                new StubLlmAdapter(),
                new StubDice(diceValue),
                new StubTrapRegistry());
        }

        private sealed class StubDice : IDiceRoller
        {
            private readonly int _value;
            public StubDice(int value) => _value = value;
            public int Roll(int sides) => _value;
        }

        private sealed class StubLlmAdapter : ILlmAdapter
        {
            public Task<DialogueOption[]> GetDialogueOptionsAsync(DialogueContext context)
                => Task.FromResult(new DialogueOption[0]);

            public Task<string> DeliverMessageAsync(DeliveryContext context)
                => Task.FromResult("delivered");

            public Task<OpponentResponse> GetOpponentResponseAsync(OpponentContext context)
                => Task.FromResult(new OpponentResponse("response"));

            public Task<string?> GetInterestChangeBeatAsync(InterestChangeContext context)
                => Task.FromResult<string?>(null);
        }

        private sealed class StubTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }
    }
}
