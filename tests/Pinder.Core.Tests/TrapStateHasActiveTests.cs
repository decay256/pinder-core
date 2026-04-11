using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Xunit;

namespace Pinder.Core.Tests
{
    [Trait("Category", "Core")]
    public class TrapStateHasActiveTests
    {
        private static TrapDefinition MakeTrap(StatType stat) =>
            new TrapDefinition($"trap-{stat}", stat, TrapEffect.Disadvantage, 0, 2,
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
            state.Clear(StatType.Charm);
            Assert.False(state.HasActive);
        }

        [Fact]
        public void TwoTraps_ClearOne_HasActive_True()
        {
            var state = new TrapState();
            state.Activate(MakeTrap(StatType.Charm));
            state.Activate(MakeTrap(StatType.Wit));
            state.Clear(StatType.Charm);
            Assert.True(state.HasActive);
        }

        [Fact]
        public void ClearAll_HasActive_False()
        {
            var state = new TrapState();
            state.Activate(MakeTrap(StatType.Charm));
            state.Activate(MakeTrap(StatType.Wit));
            state.ClearAll();
            Assert.False(state.HasActive);
        }

        [Fact]
        public void AdvanceTurn_ExpiresAll_HasActive_False()
        {
            var state = new TrapState();
            // Duration=2 turns, so after 2 advances all expire
            state.Activate(MakeTrap(StatType.Charm));
            state.AdvanceTurn();
            Assert.True(state.HasActive); // 1 turn remaining
            state.AdvanceTurn();
            Assert.False(state.HasActive); // expired
        }
    }
}
