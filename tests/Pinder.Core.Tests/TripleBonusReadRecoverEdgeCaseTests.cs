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
    /// Edge case tests for #312: Triple combo bonus on Read/Recover rolls.
    /// Complements TripleBonusReadRecoverTests with spec edge cases.
    /// </summary>
    public class TripleBonusReadRecoverEdgeCaseTests
    {
        // What: AC1 — FinalTotal = Total + 1 when triple is active (spec Example 1)
        // Mutation: Fails if externalBonus is not added to FinalTotal
        [Fact]
        public async Task ReadAsync_TripleActive_FinalTotalEqualsTotalPlusOne()
        {
            // dice=10, SA=+2 → Total=12, FinalTotal should be 13
            var session = MakeSession(diceValue: 10, saModifier: 2);
            SetupTripleBonus(session);

            var result = await session.ReadAsync();

            Assert.Equal(1, result.Roll.ExternalBonus);
            Assert.Equal(result.Roll.Total + 1, result.Roll.FinalTotal);
        }

        // What: AC2 — Recover FinalTotal = Total + 1 when triple is active (spec Example 3)
        // Mutation: Fails if Recover path doesn't pass externalBonus to ResolveFixedDC
        [Fact]
        public async Task RecoverAsync_TripleActive_FinalTotalEqualsTotalPlusOne()
        {
            var session = MakeSession(diceValue: 10, saModifier: 1);
            ActivateTrap(session);
            SetupTripleBonus(session);

            var result = await session.RecoverAsync();

            Assert.Equal(1, result.Roll.ExternalBonus);
            Assert.Equal(result.Roll.Total + 1, result.Roll.FinalTotal);
        }

        // What: Spec Example 4 — Triple bonus consumed even on failure
        // Mutation: Fails if implementation preserves bonus on failed roll
        [Fact]
        public async Task ReadAsync_TripleConsumedEvenOnFailure()
        {
            // dice=3, SA=+0 → Total=3, FinalTotal=4. DC 12 → failure. Bonus still consumed.
            var session = MakeSession(diceValue: 3, saModifier: 0);
            SetupTripleBonus(session);

            var result = await session.ReadAsync();

            Assert.False(result.Success);
            Assert.Equal(1, result.Roll.ExternalBonus); // bonus was applied

            // Second read should not have bonus
            var result2 = await session.ReadAsync();
            Assert.Equal(0, result2.Roll.ExternalBonus);
        }

        // What: Spec edge case — Triple bonus + advantage stacking (independent mechanics)
        // Mutation: Fails if advantage logic interferes with external bonus application
        [Fact]
        public async Task ReadAsync_TriplePlusAdvantage_BothApply()
        {
            // VeryIntoIt (16-20) grants advantage. Triple grants +1 external bonus.
            // Both should apply independently.
            var session = MakeSession(diceValue: 10, saModifier: 0, startingInterest: 17);
            SetupTripleBonus(session);

            var result = await session.ReadAsync();

            Assert.Equal(1, result.Roll.ExternalBonus);
            // Advantage means higher of two dice rolls — with stub returning constant,
            // both rolls are the same value so total is unchanged, but bonus still applies
        }

        // What: Spec edge case — Recover with no active trap throws before consuming triple
        // Mutation: Fails if Recover silently succeeds with no trap instead of throwing
        [Fact]
        public async Task RecoverAsync_NoTrap_ThrowsAndTripleBonusSurvives()
        {
            // No trap activated — Recover throws InvalidOperationException
            var session = MakeSession(diceValue: 10, saModifier: 0);
            SetupTripleBonus(session);

            await Assert.ThrowsAsync<System.InvalidOperationException>(
                () => session.RecoverAsync());

            // Triple bonus survives because the exception prevented consumption
            // It can still be used on the next Read action
            var readResult = await session.ReadAsync();
            Assert.Equal(1, readResult.Roll.ExternalBonus);
        }

        // What: Spec edge case — Recover failure still consumes triple
        // Mutation: Fails if implementation retains bonus after failed Recover
        [Fact]
        public async Task RecoverAsync_TripleConsumedEvenOnFailure()
        {
            // dice=3, SA=+0 → Total=3+1=4 < DC 12 → failure
            var session = MakeSession(diceValue: 3, saModifier: 0);
            ActivateTrap(session);
            SetupTripleBonus(session);

            var result = await session.RecoverAsync();

            Assert.False(result.Success);
            Assert.Equal(1, result.Roll.ExternalBonus);

            // Second Recover has no bonus
            var result2 = await session.RecoverAsync();
            Assert.Equal(0, result2.Roll.ExternalBonus);
        }

        // What: AC4 — No triple bonus → Read ExternalBonus is exactly 0 (boundary)
        // Mutation: Fails if default externalBonus is non-zero
        [Fact]
        public async Task ReadAsync_NoTriple_ExternalBonusIsZero()
        {
            var session = MakeSession(diceValue: 15, saModifier: 0);

            var result = await session.ReadAsync();

            Assert.Equal(0, result.Roll.ExternalBonus);
            Assert.Equal(result.Roll.Total, result.Roll.FinalTotal);
        }

        // What: AC4 — No triple bonus → Recover ExternalBonus is exactly 0 (boundary)
        // Mutation: Fails if Recover path has non-zero default externalBonus
        [Fact]
        public async Task RecoverAsync_NoTriple_ExternalBonusIsZero()
        {
            var session = MakeSession(diceValue: 15, saModifier: 0);
            ActivateTrap(session);

            var result = await session.RecoverAsync();

            Assert.Equal(0, result.Roll.ExternalBonus);
            Assert.Equal(result.Roll.Total, result.Roll.FinalTotal);
        }

        // What: Spec — Triple bonus value is exactly +1 (not +2 or other)
        // Mutation: Fails if bonus value is anything other than 1
        [Fact]
        public async Task ReadAsync_TripleBonusIsExactlyOne()
        {
            var session = MakeSession(diceValue: 10, saModifier: 0);
            SetupTripleBonus(session);

            var result = await session.ReadAsync();

            Assert.Equal(1, result.Roll.ExternalBonus);
        }

        // ======================== Helpers ========================

        private static void SetupTripleBonus(GameSession session)
        {
            var trackerField = typeof(GameSession).GetField("_comboTracker",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var tracker = (ComboTracker)trackerField!.GetValue(session)!;

            tracker.RecordTurn(StatType.Rizz, true);
            tracker.RecordTurn(StatType.SelfAwareness, true);
            tracker.RecordTurn(StatType.Chaos, true);

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

        private static GameSession MakeSession(int diceValue, int saModifier, int startingInterest = 10)
        {
            var stats = new StatBlock(
                new Dictionary<StatType, int>
                {
                    { StatType.Charm, 3 }, { StatType.Rizz, 2 }, { StatType.Honesty, 1 },
                    { StatType.Chaos, 0 }, { StatType.Wit, 4 }, { StatType.SelfAwareness, saModifier }
                },
                new Dictionary<ShadowStatType, int>
                {
                    { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                    { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                    { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 }
                });
            var timing = new TimingProfile(5, 1.0f, 0.0f, "neutral");
            var player = new CharacterProfile(stats, "system prompt", "player", timing, 1);
            var opponent = new CharacterProfile(stats, "system prompt", "opponent", timing, 1);

            if (startingInterest != 10)
            {
                var config = new GameSessionConfig(clock: TestHelpers.MakeClock(), startingInterest: startingInterest);
                return new GameSession(
                    player, opponent,
                    new StubLlmAdapter(),
                    new StubDice(diceValue),
                    new StubTrapRegistry(),
                    config);
            }

            return new GameSession(
                player, opponent,
                new StubLlmAdapter(),
                new StubDice(diceValue),
                new StubTrapRegistry(),
                new GameSessionConfig(clock: TestHelpers.MakeClock()));
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
