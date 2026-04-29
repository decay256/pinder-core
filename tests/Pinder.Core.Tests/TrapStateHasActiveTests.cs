using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class TrapStateHasActiveTests
    {
        // Per #371 (W2a): every trap is fixed at 3 turns regardless of the
        // definition's DurationTurns; the activation turn counts as turn 1
        // of 3 so TurnsRemaining starts at 3.
        private static TrapDefinition MakeTrap(StatType stat) =>
            new TrapDefinition($"trap-{stat}", stat, TrapEffect.Disadvantage, 0, 3,
                "instruction", "clear", "nat1");

        [Fact]
        public void FreshState_HasActive_False()
        {
            var state = new TrapState();
            Assert.False(state.HasActive);
        }

        [Fact]
        public void AfterActivate_HasActive_True()
        {
            var state = new TrapState();
            state.Activate(MakeTrap(StatType.Charm));
            Assert.True(state.HasActive);
        }

        [Fact]
        public void AfterClear_OnlyTrap_HasActive_False()
        {
            var state = new TrapState();
            state.Activate(MakeTrap(StatType.Charm));
            state.Clear();
            Assert.False(state.HasActive);
        }

        [Fact]
        public void SingleSlot_NewActivationReplacesOld()
        {
            // Per #371: single-slot — a fresh Activate REPLACES whatever was active.
            var state = new TrapState();
            state.Activate(MakeTrap(StatType.Charm));
            state.Activate(MakeTrap(StatType.Wit));

            Assert.True(state.HasActive);
            Assert.False(state.IsActive(StatType.Charm));
            Assert.True(state.IsActive(StatType.Wit));
            Assert.Equal(StatType.Wit, state.Get()!.Definition.Stat);
        }

        [Fact]
        public void ClearAll_HasActive_False()
        {
            var state = new TrapState();
            state.Activate(MakeTrap(StatType.Charm));
            state.ClearAll();
            Assert.False(state.HasActive);
        }

        [Fact]
        public void AdvanceTurn_ExpiresAfterThreeTurns()
        {
            // Activation counts as turn 1; after the activation turn TurnsRemaining
            // decrements from 3 → 2 → 1 → 0 over three end-of-turn AdvanceTurn() calls.
            var state = new TrapState();
            state.Activate(MakeTrap(StatType.Charm));

            Assert.Equal(3, state.Get()!.TurnsRemaining);
            state.AdvanceTurn();
            Assert.True(state.HasActive);
            Assert.Equal(2, state.Get()!.TurnsRemaining);

            state.AdvanceTurn();
            Assert.True(state.HasActive);
            Assert.Equal(1, state.Get()!.TurnsRemaining);

            state.AdvanceTurn();
            Assert.False(state.HasActive); // expired after third AdvanceTurn
        }
    }
}
