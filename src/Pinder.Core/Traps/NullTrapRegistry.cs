using Pinder.Core.Interfaces;
using Pinder.Core.Stats;

namespace Pinder.Core.Traps
{
    /// <summary>
    /// No-op <see cref="ITrapRegistry"/>. Returns <c>null</c> for every lookup —
    /// traps effectively disabled. Intended for tests that don't exercise trap
    /// data, and for deliberate, explicit no-traps opt-outs (e.g. the
    /// session-runner <c>--disable-traps</c> flag). Production trap-data loading
    /// should not fall back here on a missing or corrupt traps.json — see
    /// TrapRegistryLoader in session-runner, which throws instead.
    /// </summary>
    public sealed class NullTrapRegistry : ITrapRegistry
    {
        public TrapDefinition? GetTrap(StatType stat) => null;
        public string? GetLlmInstruction(StatType stat) => null;
    }
}
