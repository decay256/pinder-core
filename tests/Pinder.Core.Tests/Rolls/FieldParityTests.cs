using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Per-wrapper bespoke-field vs Check.* field-parity tests.
    /// Verifies that the new <c>Check</c> property mirrors the bespoke fields
    /// (or explicitly documents intentional divergence).
    /// #901.
    /// </summary>
    public class FieldParityTests
    {
        private sealed class FixedDice : IDiceRoller
        {
            private readonly Queue<int> _values;
            public FixedDice(params int[] values) => _values = new Queue<int>(values);
            public int Roll(int sides) => _values.Count > 0 ? _values.Dequeue() : 10;
        }

        private class EmptyTrapRegistry : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private static StatBlock MakeStats(int charm = 0)
        {
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm, charm }, { StatType.Rizz, 0 }, { StatType.Honesty, 0 },
                { StatType.Chaos, 0 }, { StatType.Wit, 0 }, { StatType.SelfAwareness, 0 },
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, 0 }, { ShadowStatType.Despair, 0 },
                { ShadowStatType.Denial, 0 }, { ShadowStatType.Fixation, 0 },
                { ShadowStatType.Dread, 0 }, { ShadowStatType.Overthinking, 0 },
            };
            return new StatBlock(baseStats, shadowStats);
        }

        // ===================== RollResult =====================

        [Fact]
        public void RollResult_NormalMiss_CheckFieldsMatchBespoke()
        {
            // Roll 8, stat +2, level +1 = 11 vs DC 15 → miss by 4 → Misfire
            var result = RollEngine.Resolve(
                StatType.Charm, MakeStats(charm: 2), MakeStats(),
                new TrapState(), 1, new EmptyTrapRegistry(), new FixedDice(8));

            Assert.NotNull(result.Check);
            // DieRoll parity
            Assert.Equal(result.DieRoll, result.Check.DieRoll);
            // UsedDieRoll parity
            Assert.Equal(result.UsedDieRoll, result.Check.UsedDieRoll);
            // DC parity
            Assert.Equal(result.DC, result.Check.Dc);
            // IsSuccess parity (both should be false for miss)
            Assert.Equal(result.IsSuccess, result.Check.IsSuccess);
            // Tier parity for non-nat-1 miss
            Assert.Equal(result.Tier, result.Check.Tier);
        }

        [Fact]
        public void RollResult_Success_CheckFieldsMatchBespoke()
        {
            // Roll 15, stat +2 = 17 vs DC 13 → success
            var attacker = MakeStats(charm: 2);
            var result = RollEngine.Resolve(
                StatType.Charm, attacker, MakeStats(),
                new TrapState(), 1, new EmptyTrapRegistry(), new FixedDice(15));

            Assert.NotNull(result.Check);
            Assert.Equal(result.DieRoll, result.Check.DieRoll);
            Assert.Equal(result.DC, result.Check.Dc);
            Assert.True(result.IsSuccess);
            Assert.True(result.Check.IsSuccess);
            Assert.Equal(FailureTier.Success, result.Tier);
            Assert.Equal(FailureTier.Success, result.Check.Tier);
        }

        [Fact]
        public void RollResult_Nat1_TierDivergence_IsDocumented()
        {
            // Nat-1: RollResult.Tier = Legendary (game rule), Check.Tier = FailureTierLadder result.
            // This intentional divergence is documented: Legendary is not produced by the ladder.
            var result = RollEngine.Resolve(
                StatType.Charm, MakeStats(), MakeStats(),
                new TrapState(), 1, new EmptyTrapRegistry(), new FixedDice(1));

            Assert.NotNull(result.Check);
            Assert.Equal(FailureTier.Legendary, result.Tier);        // game-rule special case
            Assert.NotEqual(FailureTier.Legendary, result.Check.Tier); // ladder never produces Legendary
            Assert.True(result.Check.IsNatOne);
        }

        [Fact]
        public void RollResult_Nat20_CheckIsNatTwentyTrue()
        {
            var result = RollEngine.Resolve(
                StatType.Charm, MakeStats(), MakeStats(),
                new TrapState(), 1, new EmptyTrapRegistry(), new FixedDice(20));

            Assert.NotNull(result.Check);
            Assert.True(result.Check.IsNatTwenty);
            Assert.True(result.IsSuccess);
        }

        // ===================== HorninessCheckResult =====================

        [Fact]
        public void HorninessCheckResult_Miss_CheckFieldsMatchBespoke()
        {
            // sessionHorniness = 5 → DC = 15. Roll 8 → miss by 7 → TropeTrap.
            var engine = new HorninessEngine(new Random(42));
            // We need a fixed roll of 8. Seed the Random to get a specific first roll.
            // Use a brute-force search for a seed that yields roll 8 as first Next(1,21).
            Random? rng = null;
            for (int seed = 0; seed < 10000; seed++)
            {
                var r = new Random(seed);
                if (r.Next(1, 21) == 8) { rng = new Random(seed); break; }
            }
            Assert.NotNull(rng);
            var eng = new HorninessEngine(rng!);
            var shadows = new SessionShadowTracker(MakeStats());
            var (result, _) = eng.PeekAsync(5, shadows, null);

            Assert.Equal(8, result.Roll);
            Assert.NotNull(result.Check);
            Assert.Equal(result.Roll, result.Check!.DieRoll);
            Assert.Equal(result.Total, result.Check.Total);
            Assert.Equal(result.DC, result.Check.Dc);
            Assert.Equal(result.IsMiss, !result.Check.IsSuccess);
            Assert.Equal(result.Tier, result.Check.Tier); // ladder results match for non-nat-1
        }

        [Fact]
        public void HorninessCheckResult_NotPerformed_CheckIsNull()
        {
            Assert.Null(HorninessCheckResult.NotPerformed.Check);
        }

        // ===================== ShadowCheckResult =====================

        [Fact]
        public void ShadowCheckResult_NotPerformed_CheckIsNull()
        {
            Assert.Null(ShadowCheckResult.NotPerformed.Check);
        }

        [Fact]
        public void ShadowCheckResult_CheckPresent_OnPerformedCheck()
        {
            // Construct via ShadowCheckEngine
            var rng = new Random(1); // deterministic
            var engine = new ShadowCheckEngine(rng);
            var result = engine.Check(ShadowStatType.Madness, shadowValue: 5);

            Assert.NotNull(result.Check);
            Assert.Equal(result.Roll, result.Check!.DieRoll);
            Assert.Equal(result.DC, result.Check.Dc);
            Assert.Equal(result.IsMiss, !result.Check.IsSuccess);
            if (result.IsMiss)
                Assert.Equal(result.Tier, result.Check.Tier);
        }

        // ===================== SteeringRollResult =====================

        [Fact]
        public void SteeringRollResult_NotAttempted_CheckIsNull()
        {
            Assert.Null(SteeringRollResult.NotAttempted.Check);
        }
    }
}
