using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.TestCommon
{
    public static class TestHelpers
    {
        /// <summary>
        /// Returns a zero-modifier IGameClock for test isolation.
        /// </summary>
        public static IGameClock MakeClock(int horninessModifier = 0)
            => new ZeroModifierClock(horninessModifier);

        private sealed class ZeroModifierClock : IGameClock
        {
            private readonly int _mod;
            public ZeroModifierClock(int mod) => _mod = mod;
            public DateTimeOffset Now => DateTimeOffset.UtcNow;
            public void Advance(TimeSpan amount) { }
            public void AdvanceTo(DateTimeOffset target) { }
            public TimeOfDay GetTimeOfDay() => TimeOfDay.Afternoon;
            public int GetHorninessModifier() => _mod;
        }

        public static StatBlock MakeStatBlock(int allStats = 2, int allShadow = 0)
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, allStats },
                { StatType.Rizz, allStats },
                { StatType.Honesty, allStats },
                { StatType.Chaos, allStats },
                { StatType.Wit, allStats },
                { StatType.SelfAwareness, allStats }
            };
            var shadow = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness, allShadow },
                { ShadowStatType.Despair, allShadow },
                { ShadowStatType.Denial, allShadow },
                { ShadowStatType.Fixation, allShadow },
                { ShadowStatType.Dread, allShadow },
                { ShadowStatType.Overthinking, allShadow }
            };
            return new StatBlock(stats, shadow);
        }
    }
}
