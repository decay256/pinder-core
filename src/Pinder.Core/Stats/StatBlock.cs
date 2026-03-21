using System;
using System.Collections.Generic;

namespace Pinder.Core.Stats
{
    /// <summary>
    /// Holds all stats for one character. Immutable snapshot used for roll calculations.
    /// Stat values ARE the modifiers (e.g. Charm +3 adds +3 to rolls).
    /// Shadow stats reduce paired positives: -1 per 3 shadow points.
    /// </summary>
    public sealed class StatBlock
    {
        private readonly Dictionary<StatType, int> _base;
        private readonly Dictionary<ShadowStatType, int> _shadow;

        // Defence: which stat the opponent uses to resist each attack stat
        public static readonly Dictionary<StatType, StatType> DefenceTable = new Dictionary<StatType, StatType>
        {
            { StatType.Charm,         StatType.SelfAwareness },
            { StatType.Rizz,          StatType.Wit           },
            { StatType.Honesty,       StatType.SelfAwareness },
            { StatType.Chaos,         StatType.Charm         },
            { StatType.Wit,           StatType.Wit           },
            { StatType.SelfAwareness, StatType.Honesty       }
        };

        // Which shadow stat is paired with each positive stat
        public static readonly Dictionary<StatType, ShadowStatType> ShadowPairs = new Dictionary<StatType, ShadowStatType>
        {
            { StatType.Charm,         ShadowStatType.Madness      },
            { StatType.Rizz,          ShadowStatType.Horniness    },
            { StatType.Honesty,       ShadowStatType.Denial       },
            { StatType.Chaos,         ShadowStatType.Fixation     },
            { StatType.Wit,           ShadowStatType.Dread        },
            { StatType.SelfAwareness, ShadowStatType.Overthinking }
        };

        public StatBlock(
            Dictionary<StatType, int> baseStats,
            Dictionary<ShadowStatType, int> shadowStats)
        {
            _base   = baseStats   ?? throw new ArgumentNullException(nameof(baseStats));
            _shadow = shadowStats ?? throw new ArgumentNullException(nameof(shadowStats));
        }

        /// <summary>
        /// Raw base value before shadow penalty, clamped to valid range.
        /// </summary>
        public int GetBase(StatType stat)
        {
            _base.TryGetValue(stat, out int val);
            return val;
        }

        /// <summary>
        /// Current shadow value for a shadow stat.
        /// </summary>
        public int GetShadow(ShadowStatType shadow)
        {
            _shadow.TryGetValue(shadow, out int val);
            return val;
        }

        /// <summary>
        /// Effective modifier after shadow penalty. This is what gets added to d20 rolls.
        /// Penalty = floor(shadowValue / 3).
        /// </summary>
        public int GetEffective(StatType stat)
        {
            int baseVal   = GetBase(stat);
            int shadowVal = GetShadow(ShadowPairs[stat]);
            int penalty   = shadowVal / 3;
            return baseVal - penalty;
        }

        /// <summary>
        /// DC to resist an incoming stat. DC = 10 + defending stat's effective modifier.
        /// </summary>
        public int GetDefenceDC(StatType attackingStat)
        {
            StatType defenceStat = DefenceTable[attackingStat];
            return 10 + GetEffective(defenceStat);
        }
    }
}
