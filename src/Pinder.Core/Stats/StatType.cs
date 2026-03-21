namespace Pinder.Core.Stats
{
    /// <summary>
    /// The six positive stats a character can have.
    /// Each is paired with a shadow stat that grows on failure.
    /// </summary>
    public enum StatType
    {
        Charm,          // paired with Madness
        Rizz,           // paired with Horniness
        Honesty,        // paired with Denial
        Chaos,          // paired with Fixation
        Wit,            // paired with Dread
        SelfAwareness   // paired with Overthinking
    }

    /// <summary>
    /// The six shadow (corruption) stats. Never rolled — passive debuffs only.
    /// Every 3 points of a shadow stat reduces the paired positive stat by 1.
    /// </summary>
    public enum ShadowStatType
    {
        Madness,        // paired with Charm
        Horniness,      // paired with Rizz (rolled fresh each conversation: 1d10)
        Denial,         // paired with Honesty
        Fixation,       // paired with Chaos
        Dread,          // paired with Wit
        Overthinking    // paired with SelfAwareness
    }
}
