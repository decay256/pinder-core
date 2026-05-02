using System;
using System.Collections.Generic;

namespace Pinder.Core.Stats
{
    /// <summary>
    /// Mutable shadow tracking layer wrapping an immutable StatBlock.
    /// Tracks in-session shadow growth deltas and provides effective stat values
    /// that account for session-accumulated shadow growth.
    ///
    /// Invariant (issue #405): effective shadow values are floored at 0.
    /// Negative shadow values are not legal game state — they would silently
    /// buff the paired positive stat via integer-division flooring in
    /// <see cref="GetEffectiveStat"/>. The floor is enforced at three layers
    /// (read, write, restore) for defense in depth.
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
        /// Returns the effective shadow value: base shadow + in-session delta, floored at 0.
        /// (issue #405: negative effective shadows are not legal — they would silently buff
        /// the paired positive stat via floor-division in <see cref="GetEffectiveStat"/>.)
        /// </summary>
        public int GetEffectiveShadow(ShadowStatType shadow)
        {
            return Math.Max(0, _baseStats.GetShadow(shadow) + GetDelta(shadow));
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
        /// Formula: baseStats.GetBase(stat) - floor(max(0, baseStats.GetShadow(pairedShadow) + delta[pairedShadow]) / 3)
        /// Uses StatBlock.ShadowPairs to determine the paired shadow stat.
        /// (issue #405: shadow component is floored at 0 to prevent negative shadows from
        /// silently buffing the paired stat via integer-division of negatives.)
        /// </summary>
        public int GetEffectiveStat(StatType stat)
        {
            var pairedShadow = StatBlock.ShadowPairs[stat];
            int totalShadow = Math.Max(0, _baseStats.GetShadow(pairedShadow) + GetDelta(pairedShadow));
            int penalty = totalShadow / 3;
            return _baseStats.GetBase(stat) - penalty;
        }

        /// <summary>
        /// Returns only the in-session delta for a shadow stat (0 if no growth has occurred).
        /// Note: this is the RAW stored delta — it is bounded below such that
        /// base + delta &gt;= 0, but does not by itself indicate a clamp event. Use
        /// <see cref="GetEffectiveShadow"/> for the user-visible value.
        /// </summary>
        public int GetDelta(ShadowStatType shadow)
        {
            _deltas.TryGetValue(shadow, out int delta);
            return delta;
        }

        /// <summary>
        /// Applies a signed delta to a shadow stat. Unlike ApplyGrowth, allows negative values
        /// (e.g., Fixation −1 offset for using 4+ different stats). Stores a description event.
        ///
        /// (issue #405) The effective shadow value is floored at 0. If the requested negative
        /// delta would drive the effective value below 0, only the portion that brings the
        /// shadow down to 0 is applied; the remainder is recorded as a "(floored)" event so
        /// the audit log honestly reflects what actually happened.
        /// </summary>
        /// <param name="shadow">The shadow stat to adjust.</param>
        /// <param name="delta">Signed delta (positive = growth, negative = reduction).</param>
        /// <param name="reason">Human-readable reason for the change.</param>
        /// <returns>Description string: "{ShadowStatName} {+/-applied} ({reason})" — or
        /// "{ShadowStatName} +0 ({reason}) (floored)" when the reduction was fully suppressed.</returns>
        public string ApplyOffset(ShadowStatType shadow, int delta, string reason)
        {
            int currentEffective = _baseStats.GetShadow(shadow) + GetDelta(shadow);
            // Pre-clamp: if the running delta has been suppressed in the past, treat the
            // floor as 0 for the purposes of this call. (currentEffective is already
            // monotone with stored delta; we just guard against drifting below 0 from here.)
            if (currentEffective < 0) currentEffective = 0;

            int appliedDelta = delta;
            bool floored = false;

            if (delta < 0 && currentEffective + delta < 0)
            {
                // Only apply enough to take effective down to 0; record the rest as floored.
                appliedDelta = -currentEffective; // 0 or negative; brings effective to exactly 0
                floored = true;
            }

            if (appliedDelta != 0)
            {
                if (_deltas.ContainsKey(shadow))
                    _deltas[shadow] += appliedDelta;
                else
                    _deltas[shadow] = appliedDelta;
            }

            string sign = appliedDelta >= 0 ? $"+{appliedDelta}" : appliedDelta.ToString();
            string description = floored
                ? $"{shadow} {sign} ({reason}) (floored)"
                : $"{shadow} {sign} ({reason})";
            _growthEvents.Add(description);
            return description;
        }

        /// <summary>
        /// Restores shadow deltas so that effective values match the provided target values.
        /// For each shadow stat, sets delta = targetValue − baseStats.GetShadow(shadow).
        /// Clears any previously accumulated deltas and the growth event log before restoring.
        /// Used by GameSession.RestoreState when resimulating from a snapshot.
        ///
        /// (issue #405) Restored target values are clamped at 0. Old or corrupt snapshots
        /// containing negative effective values are normalised to 0 on restore so the floor
        /// invariant holds across resimulation.
        /// </summary>
        /// <param name="targetValues">Map of ShadowStatType.ToString() → desired effective value.</param>
        public void RestoreFromSnapshot(Dictionary<string, int> targetValues)
        {
            if (targetValues == null) return;
            _deltas.Clear();
            _growthEvents.Clear();

            foreach (ShadowStatType shadow in System.Enum.GetValues(typeof(ShadowStatType)))
            {
                string key = shadow.ToString();
                if (targetValues.TryGetValue(key, out int targetValue))
                {
                    int clampedTarget = Math.Max(0, targetValue);
                    int baseValue = _baseStats.GetShadow(shadow);
                    int delta = clampedTarget - baseValue;
                    if (delta != 0)
                        _deltas[shadow] = delta;
                }
            }
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

        /// <summary>
        /// #790 (Phase 4): deep clone for fast-gameplay engine forking. Returns
        /// an independent <see cref="SessionShadowTracker"/> wrapping the
        /// same immutable base <see cref="StatBlock"/> and carrying a copy
        /// of the in-session deltas + growth-event log. Mutating either side's
        /// shadow growth does not affect the other.
        /// </summary>
        public SessionShadowTracker Clone()
        {
            var copy = new SessionShadowTracker(_baseStats);
            foreach (var kv in _deltas)
                copy._deltas[kv.Key] = kv.Value;
            copy._growthEvents.AddRange(_growthEvents);
            return copy;
        }
    }
}
