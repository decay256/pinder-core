using System;
using System.Collections.Generic;

namespace Pinder.Core.Stats
{
    /// <summary>
    /// Mutable shadow tracking layer wrapping an immutable StatBlock.
    /// Tracks in-session shadow growth deltas and provides effective stat values
    /// that account for session-accumulated shadow growth.
    /// </summary>
    public sealed class SessionShadowTracker
    {
        private readonly StatBlock _baseStats;
        private readonly Dictionary<ShadowStatType, int> _deltas;
        private readonly List<string> _growthEvents;

        /// <summary>
        /// Wraps an immutable StatBlock for mutable shadow tracking.
        /// </summary>
        /// <param name="baseStats">Immutable StatBlock to wrap. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when baseStats is null.</exception>
        public SessionShadowTracker(StatBlock baseStats)
        {
            _baseStats = baseStats ?? throw new ArgumentNullException(nameof(baseStats));
            _deltas = new Dictionary<ShadowStatType, int>();
            _growthEvents = new List<string>();
        }

        /// <summary>
        /// Returns the effective shadow value: base shadow + in-session delta.
        /// </summary>
        public int GetEffectiveShadow(ShadowStatType shadow)
        {
            return _baseStats.GetShadow(shadow) + GetDelta(shadow);
        }

        /// <summary>
        /// Applies positive growth to a shadow stat. Stores the description for DrainGrowthEvents().
        /// </summary>
        /// <param name="shadow">The shadow stat to grow.</param>
        /// <param name="amount">Growth amount. Must be &gt; 0.</param>
        /// <param name="reason">Human-readable reason for the growth.</param>
        /// <returns>Description string: "{ShadowStatName} +{amount} ({reason})"</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when amount is less than or equal to 0.</exception>
        public string ApplyGrowth(ShadowStatType shadow, int amount, string reason)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be greater than 0.");

            if (_deltas.ContainsKey(shadow))
                _deltas[shadow] += amount;
            else
                _deltas[shadow] = amount;

            string description = $"{shadow} +{amount} ({reason})";
            _growthEvents.Add(description);
            return description;
        }

        /// <summary>
        /// Returns the effective stat modifier accounting for in-session shadow growth.
        /// Formula: baseStats.GetBase(stat) - floor((baseStats.GetShadow(pairedShadow) + delta[pairedShadow]) / 3)
        /// Uses StatBlock.ShadowPairs to determine the paired shadow stat.
        /// </summary>
        public int GetEffectiveStat(StatType stat)
        {
            var pairedShadow = StatBlock.ShadowPairs[stat];
            int totalShadow = _baseStats.GetShadow(pairedShadow) + GetDelta(pairedShadow);
            int penalty = totalShadow / 3;
            return _baseStats.GetBase(stat) - penalty;
        }

        /// <summary>
        /// Returns only the in-session delta for a shadow stat (0 if no growth has occurred).
        /// </summary>
        public int GetDelta(ShadowStatType shadow)
        {
            _deltas.TryGetValue(shadow, out int delta);
            return delta;
        }

        /// <summary>
        /// Returns all growth event description strings accumulated since last drain, then clears the internal log.
        /// Returns an empty list if no growth events have occurred since the last drain (or since construction).
        /// Added per #161 resolution — this is the canonical drain method, replacing the dropped CharacterState concept.
        /// </summary>
        public IReadOnlyList<string> DrainGrowthEvents()
        {
            var events = new List<string>(_growthEvents);
            _growthEvents.Clear();
            return events;
        }
    }
}
