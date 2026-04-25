using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.Core.Tests.SessionSetup
{
    internal static class CharacterFactory
    {
        public static CharacterProfile Make(string displayName, string bio = "")
        {
            var baseStats = new Dictionary<StatType, int>
            {
                { StatType.Charm,         3 },
                { StatType.Rizz,          2 },
                { StatType.Honesty,       2 },
                { StatType.Chaos,         1 },
                { StatType.Wit,           3 },
                { StatType.SelfAwareness, 1 },
            };
            var shadows = new Dictionary<ShadowStatType, int>
            {
                { ShadowStatType.Dread,        0 },
                { ShadowStatType.Fixation,     0 },
                { ShadowStatType.Denial,       0 },
                { ShadowStatType.Madness,      0 },
                { ShadowStatType.Despair,      0 },
                { ShadowStatType.Overthinking, 0 },
            };
            var stats = new StatBlock(baseStats, shadows);
            var timing = new TimingProfile(60, 0.2f, 1.0f, "always");
            return new CharacterProfile(
                stats,
                assembledSystemPrompt: $"Assembled prompt for {displayName}.",
                displayName: displayName,
                timing: timing,
                level: 1,
                bio: bio);
        }
    }
}
