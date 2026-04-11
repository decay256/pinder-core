namespace Pinder.Core.Stats
{
    /// <summary>
    /// The six positive stats a character can have.
    /// Each is paired with a shadow stat that grows on failure.
    /// </summary>
    public enum StatType
    {
        Charm,          // paired with Madness
        Rizz,           // paired with Despair
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
        /// <summary>
        /// Despair = the opposite of magnetism. The character wants to be desired and it shows.
        /// Paired with Rizz. Grows on: RIZZ Nat 1 (+2), RIZZ TropeTrap failure (+1),
        /// picking RIZZ 3+ turns in a row without success (+1).
        /// T1 (≥6): taints RIZZ delivery dialogue.
        /// T2 (≥12): penalises RIZZ rolls (−2 effective modifier).
        /// </summary>
        Despair,        // paired with Rizz
        Denial,         // paired with Honesty
        Fixation,       // paired with Chaos
        Dread,          // paired with Wit
        Overthinking    // paired with SelfAwareness
    }
}
