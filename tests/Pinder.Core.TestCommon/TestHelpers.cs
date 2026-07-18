using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;
using Pinder.Core.Characters;
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

        public static IReadOnlyDictionary<string, string> MakePsychiatricDiagnosis()
            => new Dictionary<string, string>
            {
                { "derived_feeling", "fear of being overlooked" },
                { "defense_reaction", "tests sincerity before trusting it" }
            };

        public static IReadOnlyDictionary<string, BackstoryFact> MakeBackstory()
        {
            var facts = new Dictionary<string, BackstoryFact>();
            foreach (var category in BackstoryValidator.RequiredCategories)
            {
                facts[category] = new BackstoryFact(
                    bioLie: $"lie for {category}",
                    tragicReality: $"truth for {category}");
            }

            return facts;
        }

        public static IReadOnlyList<string> MakeStakeLines()
        {
            var stakes = new List<string>();
            for (var i = 1; i <= 15; i++)
            {
                stakes.Add($"stake line {i}");
            }

            return stakes;
        }

        public static SessionShadowTracker MakeShadowTracker(
            int dread = 0,
            int denial = 0,
            int fixation = 0,
            int madness = 0,
            int overthinking = 0,
            int horniness = 0)
        {
            var stats = new Dictionary<StatType, int>
            {
                { StatType.Charm, 2 },
                { StatType.Rizz, 2 },
                { StatType.Honesty, 2 },
                { StatType.Chaos, 2 },
                { StatType.Wit, 2 },
                { StatType.SelfAwareness, 2 }
            };
            var shadow = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Dread, dread },
                { ShadowStatType.Denial, denial },
                { ShadowStatType.Fixation, fixation },
                { ShadowStatType.Madness, madness },
                { ShadowStatType.Overthinking, overthinking },
                { ShadowStatType.Despair, horniness }
            };
            return new SessionShadowTracker(new StatBlock(stats, shadow));
        }
    }
}
