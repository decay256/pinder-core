using System.Collections.Generic;
using Pinder.Core.Stats;
using Xunit;

namespace Pinder.Core.Tests
{
    public class StatBlockTests
    {
        private static StatBlock MakeDefender(
            int charm = 0, int rizz = 0, int honesty = 0,
            int chaos = 0, int wit = 0, int selfAwareness = 0)
        {
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm,         charm         },
                { StatType.Rizz,          rizz          },
                { StatType.Honesty,       honesty       },
                { StatType.Chaos,         chaos         },
                { StatType.Wit,           wit           },
                { StatType.SelfAwareness, selfAwareness },
            };
            var shadowStats = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Madness,       0 },
                { ShadowStatType.Horniness,     0 },
                { ShadowStatType.Denial,        0 },
                { ShadowStatType.Fixation,      0 },
                { ShadowStatType.Dread,         0 },
                { ShadowStatType.Overthinking,  0 },
            };
            return new StatBlock(baseStats, shadowStats);
        }

        // --- Defence pairing tests (rules v3.4) ---

        [Fact]
        public void DefenceTable_HonestyMapsToChaos()
        {
            Assert.Equal(StatType.Chaos, StatBlock.DefenceTable[StatType.Honesty]);
        }

        [Fact]
        public void DefenceTable_WitMapsToRizz()
        {
            Assert.Equal(StatType.Rizz, StatBlock.DefenceTable[StatType.Wit]);
        }

        [Fact]
        public void DefenceTable_CharmMapsToSelfAwareness()
        {
            Assert.Equal(StatType.SelfAwareness, StatBlock.DefenceTable[StatType.Charm]);
        }

        [Fact]
        public void DefenceTable_RizzMapsToWit()
        {
            Assert.Equal(StatType.Wit, StatBlock.DefenceTable[StatType.Rizz]);
        }

        [Fact]
        public void DefenceTable_ChaosMapsToCharm()
        {
            Assert.Equal(StatType.Charm, StatBlock.DefenceTable[StatType.Chaos]);
        }

        [Fact]
        public void DefenceTable_SelfAwarenessMapsToHonesty()
        {
            Assert.Equal(StatType.Honesty, StatBlock.DefenceTable[StatType.SelfAwareness]);
        }

        // --- Base DC = 13 tests ---

        [Fact]
        public void GetDefenceDC_BaseDCIs13()
        {
            var defender = MakeDefender();
            // All stats 0, so DC = 13 + 0 = 13
            Assert.Equal(13, defender.GetDefenceDC(StatType.Charm));
        }

        [Fact]
        public void GetDefenceDC_Honesty_UsesChaos()
        {
            var defender = MakeDefender(chaos: 5);
            // DC = 13 + Chaos effective (5) = 18
            Assert.Equal(18, defender.GetDefenceDC(StatType.Honesty));
        }

        [Fact]
        public void GetDefenceDC_Wit_UsesRizz()
        {
            var defender = MakeDefender(rizz: 3);
            // DC = 13 + Rizz effective (3) = 16
            Assert.Equal(16, defender.GetDefenceDC(StatType.Wit));
        }
    }
}
