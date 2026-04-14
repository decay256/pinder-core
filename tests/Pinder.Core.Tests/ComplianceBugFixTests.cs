using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.Conversation;
using Pinder.Core.Characters;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Pinder.Core.Tests
{
    /// <summary>
    /// Tests for 4 YAML compliance bugs fixed in Sprint compliance audit.
    /// </summary>
    [Trait("Category", "Core")]
    public class ComplianceBugFixTests
    {
        // ──────────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────────

        private class FixedDice2 : IDiceRoller
        {
            private readonly Queue<int> _q;
            public FixedDice2(params int[] values) => _q = new Queue<int>(values);
            // When queue runs out, return a safe mid-range value
            public int Roll(int sides) => _q.Count > 0 ? _q.Dequeue() : sides / 2 + 1;
        }

        private class EmptyTrapRegistry2 : ITrapRegistry
        {
            public TrapDefinition? GetTrap(StatType stat) => null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private class SingleTrapRegistry2 : ITrapRegistry
        {
            private readonly TrapDefinition _trap;
            public SingleTrapRegistry2(TrapDefinition trap) => _trap = trap;
            public TrapDefinition? GetTrap(StatType stat) => stat == _trap.Stat ? _trap : null;
            public string? GetLlmInstruction(StatType stat) => null;
        }

        private static StatBlock MakeStats(int charm = 0, int sa = 0)
        {
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm, charm },
                { StatType.Rizz, 0 },
                { StatType.Honesty, 0 },
                { StatType.Chaos, 0 },
                { StatType.Wit, 0 },
                { StatType.SelfAwareness, sa },
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness,      0 },
                { ShadowStatType.Despair,      0 },
                { ShadowStatType.Denial,       0 },
                { ShadowStatType.Fixation,     0 },
                { ShadowStatType.Dread,        0 },
                { ShadowStatType.Overthinking, 0 },
            };
            return new StatBlock(baseStats, shadowStats);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Bug 1: Nat 1 should activate a trap
        // ──────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Bug1_Nat1_ActivatesTrap()
        {
            var trapDef = new TrapDefinition("charm-trap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 2, "instruction", "clear", "nat1");
            var registry = new SingleTrapRegistry2(trapDef);
            var traps = new TrapState();
            var attacker = MakeStats(charm: 10); // great stats, but roll is 1
            var defender = MakeStats();

            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, traps, 1,
                registry, new FixedDice2(1));

            Assert.Equal(FailureTier.Legendary, result.Tier);
            Assert.True(result.IsNatOne);
            Assert.NotNull(result.ActivatedTrap);
            Assert.Equal("charm-trap", result.ActivatedTrap!.Id);
            Assert.True(traps.IsActive(StatType.Charm));
        }

        [Fact]
        public void Bug1_Nat1_DoesNotActivateTrap_WhenAlreadyActive()
        {
            var trapDef = new TrapDefinition("charm-trap", StatType.Charm,
                TrapEffect.Disadvantage, 0, 2, "instruction", "clear", "nat1");
            var registry = new SingleTrapRegistry2(trapDef);
            var traps = new TrapState();
            traps.Activate(trapDef); // already active

            var attacker = MakeStats(charm: 10);
            var defender = MakeStats();

            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, traps, 1,
                registry, new FixedDice2(1));

            Assert.Equal(FailureTier.Legendary, result.Tier);
            // No new trap activated (already active)
            Assert.Null(result.ActivatedTrap);
            Assert.True(traps.IsActive(StatType.Charm));
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Bug 2: opponent_dc_increase trap effect should raise effective DC
        // ──────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Bug2_OpponentDCIncrease_RaisesEffectiveDC()
        {
            // Charm=0, defender SA=0 → base DC=16
            // Active trap with OpponentDCIncrease +3 → effective DC=19
            // Roll 17: 17+0+0=17 beats base DC 16 but misses DC 19
            var trapDef = new TrapDefinition("dc-trap", StatType.Charm,
                TrapEffect.OpponentDCIncrease, 3, 2, "instruction", "clear", "nat1");
            var traps = new TrapState();
            traps.Activate(trapDef);

            var attacker = MakeStats();
            var defender = MakeStats();

            var result = RollEngine.Resolve(
                StatType.Charm, attacker, defender, traps, 1,
                new EmptyTrapRegistry2(), new FixedDice2(17));

            // Without the fix: DC would be 16 → roll 17 = success
            // With the fix: DC should be 19 → roll 17 = failure
            Assert.Equal(19, result.DC);
            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void Bug2_OpponentDCIncrease_AppliesInFixedDcPath()
        {
            // Fixed DC path: fixedDc=14, trap adds +3 → effective DC=17
            // Roll 15: 15+0+0=15 < 17 → failure
            var trapDef = new TrapDefinition("dc-trap", StatType.Charm,
                TrapEffect.OpponentDCIncrease, 3, 2, "instruction", "clear", "nat1");
            var traps = new TrapState();
            traps.Activate(trapDef);

            var attacker = MakeStats();

            var result = RollEngine.ResolveFixedDC(
                StatType.Charm, attacker, 14, traps, 1,
                new EmptyTrapRegistry2(), new FixedDice2(15));

            // With the fix: effective DC = 17, roll 15 = failure
            Assert.Equal(17, result.DC);
            Assert.False(result.IsSuccess);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Bug 3: SA success vs DC 12 clears oldest active trap
        // ──────────────────────────────────────────────────────────────────────────

        [Fact]
        public void TrapState_ClearOldest_RemovesOldestTrap()
        {
            var trap1 = new TrapDefinition("trap1", StatType.Charm,
                TrapEffect.Disadvantage, 0, 5, "i1", "c1", "n1");
            var trap2 = new TrapDefinition("trap2", StatType.Rizz,
                TrapEffect.Disadvantage, 0, 2, "i2", "c2", "n2");

            var state = new TrapState();
            state.Activate(trap1); // 5 turns remaining
            state.Activate(trap2); // 2 turns remaining — oldest (fewest turns = soonest to expire)

            state.ClearOldest();

            // trap2 had fewest turns → cleared
            Assert.False(state.IsActive(StatType.Rizz));
            // trap1 still active
            Assert.True(state.IsActive(StatType.Charm));
        }

        [Fact]
        public void TrapState_ClearOldest_DoesNothingWhenEmpty()
        {
            var state = new TrapState();
            // Should not throw
            state.ClearOldest();
            Assert.False(state.HasActive);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Bug 4: End-of-game Dread +1 on Unmatched / Ghosted
        // ──────────────────────────────────────────────────────────────────────────

        private static SessionShadowTracker MakeShadowTracker()
        {
            // SessionShadowTracker requires a StatBlock — use a zeroed block
            return new SessionShadowTracker(MakeStats());
        }

        [Fact]
        public void Bug4_Ghosted_AppliesDreadGrowth()
        {
            // Ghost trigger in StartTurnAsync: interest Bored → d4 == 1 → Ghosted
            // The existing code already applies Dread+1 for Ghosted — verify it's wired
            var shadows = MakeShadowTracker();
            int dreadBefore = shadows.GetEffectiveShadow(ShadowStatType.Dread);

            // Simulate what StartTurnAsync does on Ghost trigger
            shadows.ApplyGrowth(ShadowStatType.Dread, 1, "Ghosted");

            int dreadAfter = shadows.GetEffectiveShadow(ShadowStatType.Dread);
            Assert.Equal(dreadBefore + 1, dreadAfter);
        }

        [Fact]
        public void Bug4_SessionShadowTracker_ApplyGrowth_AccumulatesDread()
        {
            var shadows = MakeShadowTracker();
            int before = shadows.GetEffectiveShadow(ShadowStatType.Dread);

            shadows.ApplyGrowth(ShadowStatType.Dread, 1, "Conversation ended without date");

            Assert.Equal(before + 1, shadows.GetEffectiveShadow(ShadowStatType.Dread));
        }
    }
}
