using System.Collections.Generic;
using System.Text.Json.Serialization;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Snapshot of the datee's defense posture at the start of a turn.
    /// One entry per <see cref="StatType"/> (6 entries total), keyed on the
    /// attacking stat. Each entry shows which defender stat resists that
    /// attack, and what modifier will be used when computing the defense DC.
    ///
    /// <para>
    /// Added in issue #903.
    /// </para>
    /// </summary>
    public sealed class DateeDefenseSnapshot
    {
        /// <summary>
        /// Defence data indexed by the player's attacking stat.
        /// Always contains exactly one entry per <see cref="StatType"/> value (6 entries).
        /// </summary>
        [JsonPropertyName("by_attacker_stat")]
        public IReadOnlyDictionary<StatType, DateeDefenseEntry> ByAttackerStat { get; }

        public DateeDefenseSnapshot(IReadOnlyDictionary<StatType, DateeDefenseEntry> byAttackerStat)
        {
            ByAttackerStat = byAttackerStat
                ?? throw new System.ArgumentNullException(nameof(byAttackerStat));
        }
    }

    /// <summary>
    /// Defence data for one attacker stat.
    ///
    /// <para>
    /// <see cref="EffectiveModifier"/> = <c>datee.Stats.GetEffective(DefendingStat)</c>
    /// plus any active <see cref="Pinder.Core.Traps.TrapEffect.DateeDCIncrease"/> bonus
    /// for the corresponding attacker stat. This matches the DC calculation in
    /// <c>RollEngine.Resolve</c>, where an active trap adds
    /// <c>activeTrap.Definition.EffectValue</c> to the computed DC.
    /// </para>
    ///
    /// <para>
    /// <see cref="BaseModifier"/> = raw <c>datee.Stats.GetBase(DefendingStat)</c>
    /// before shadow penalties or trap bonuses. Useful for UI that wants to
    /// show the "clean" base value alongside the effective one.
    /// </para>
    /// </summary>
    public sealed class DateeDefenseEntry
    {
        /// <summary>
        /// The datee's stat that defends against the attacker stat.
        /// Derived from <see cref="StatBlock.DefenceTable"/>.
        /// </summary>
        [JsonPropertyName("defending_stat")]
        public StatType DefendingStat { get; }

        /// <summary>
        /// Effective defense modifier: shadow-adjusted stat value plus any
        /// active <see cref="Pinder.Core.Traps.TrapEffect.DateeDCIncrease"/>
        /// bonus for this attacker stat. This is the modifier that will affect
        /// the defense DC at roll time.
        /// </summary>
        [JsonPropertyName("effective_modifier")]
        public int EffectiveModifier { get; }

        /// <summary>
        /// Base modifier: the raw stat value before shadow penalties.
        /// Always &lt;= <see cref="EffectiveModifier"/> when no trap bonus is
        /// active, because shadow penalties can only reduce it.
        /// </summary>
        [JsonPropertyName("base_modifier")]
        public int BaseModifier { get; }

        public DateeDefenseEntry(StatType defendingStat, int effectiveModifier, int baseModifier)
        {
            DefendingStat    = defendingStat;
            EffectiveModifier = effectiveModifier;
            BaseModifier     = baseModifier;
        }
    }
}
