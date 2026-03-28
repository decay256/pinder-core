using Pinder.Core.Progression;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Supplies failure/success prompt pool entries.
    /// Unity: implement via ScriptableObjects.
    /// Standalone: implement via JSON deserialization of data/ files.
    /// </summary>
    public interface IFailurePool
    {
        /// <summary>Returns a failure prompt string for the given stat, tier, and level pool.</summary>
        string GetFailureEntry(StatType stat, FailureTier tier, FailurePoolTier levelTier);

        /// <summary>Returns a success prompt string for the given stat.</summary>
        string GetSuccessEntry(StatType stat);
    }

    /// <summary>
    /// Supplies trap definitions keyed by the stat that triggers them.
    /// </summary>
    public interface ITrapRegistry
    {
        /// <summary>Returns the trap definition for a stat, or null if none defined.</summary>
        TrapDefinition? GetTrap(StatType stat);

        /// <summary>
        /// Returns the LLM instruction (prompt taint text) for the trap on the given stat,
        /// or null if no trap is defined for that stat.
        /// </summary>
        string? GetLlmInstruction(StatType stat);
    }

    /// <summary>
    /// Dice roller abstraction. Swap out for deterministic testing.
    /// </summary>
    public interface IDiceRoller
    {
        /// <summary>Roll a single die with the given number of sides. Returns 1..sides.</summary>
        int Roll(int sides);
    }
}
