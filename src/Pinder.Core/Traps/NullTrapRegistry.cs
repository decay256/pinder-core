using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Traps
{
    /// <summary>
    /// No-op <see cref="ITrapRegistry"/>. Returns <c>null</c> for every lookup —
    /// traps effectively disabled. Useful as a fallback when trap data files
    /// are missing or corrupt.
    /// </summary>
    public sealed class NullTrapRegistry : ITrapRegistry
    {
        public TrapDefinition? GetTrap(StatType stat) => null;
        public string? GetLlmInstruction(StatType stat) => null;
    }
}
