using Pinder.Core.Stats;
using System.Collections.Generic;

namespace Pinder.Core.Traps
{
    /// <summary>
    /// Tracks currently active traps for a character in a conversation.
    /// Traps persist across turns and taint ALL messages, not just the trapped stat.
    /// </summary>
    public sealed class TrapState
    {
        private readonly Dictionary<StatType, ActiveTrap> _active = new Dictionary<StatType, ActiveTrap>();

        /// <summary>True if any trap is currently active.</summary>
        public bool HasActive => _active.Count > 0;

        /// <summary>Activate a trap. Replaces an existing trap on the same stat.</summary>
        public void Activate(TrapDefinition definition)
        {
            _active[definition.Stat] = new ActiveTrap(definition, definition.DurationTurns);
        }

        /// <summary>
        /// Activate a trap with an explicit turns-remaining override.
        /// Used when restoring session state from a snapshot where the trap is mid-duration.
        /// </summary>
        public void Activate(TrapDefinition definition, int turnsRemaining)
        {
            _active[definition.Stat] = new ActiveTrap(definition, turnsRemaining);
        }

        /// <summary>True if a trap is active on this stat.</summary>
        public bool IsActive(StatType stat) => _active.ContainsKey(stat);

        /// <summary>Returns the active trap for a stat, or null.</summary>
        public ActiveTrap? GetActive(StatType stat)
        {
            _active.TryGetValue(stat, out var trap);
            return trap;
        }

        /// <summary>All currently active traps (for LLM prompt taint assembly).</summary>
        public IEnumerable<ActiveTrap> AllActive => _active.Values;

        /// <summary>
        /// Advance all trap counters by one turn. Removes traps that have expired.
        /// Call once at the end of each player turn.
        /// </summary>
        public void AdvanceTurn()
        {
            var toRemove = new List<StatType>();
            foreach (var kv in _active)
            {
                kv.Value.DecrementTurn();
                if (kv.Value.TurnsRemaining <= 0)
                    toRemove.Add(kv.Key);
            }
            foreach (var key in toRemove)
                _active.Remove(key);
        }

        /// <summary>Manually clear a trap (e.g. via clear method action).</summary>
        public void Clear(StatType stat) => _active.Remove(stat);

        /// <summary>Clear all traps.</summary>
        public void ClearAll() => _active.Clear();

        /// <summary>
        /// Clears the oldest active trap (the one with fewest turns remaining).
        /// Used when SA roll succeeds vs DC 12 to allow a trap clear.
        /// Does nothing if no traps are active.
        /// </summary>
        public void ClearOldest()
        {
            if (_active.Count == 0) return;

            StatType? oldestKey = null;
            int minTurns = int.MaxValue;
            foreach (var kv in _active)
            {
                if (kv.Value.TurnsRemaining < minTurns)
                {
                    minTurns = kv.Value.TurnsRemaining;
                    oldestKey = kv.Key;
                }
            }
            if (oldestKey.HasValue)
                _active.Remove(oldestKey.Value);
        }
    }

    /// <summary>
    /// A trap that is currently active, with a countdown.
    /// </summary>
    public sealed class ActiveTrap
    {
        public TrapDefinition Definition  { get; }
        public int TurnsRemaining         { get; private set; }

        public ActiveTrap(TrapDefinition definition, int turnsRemaining)
        {
            Definition    = definition;
            TurnsRemaining = turnsRemaining;
        }

        internal void DecrementTurn() => TurnsRemaining--;
    }
}
