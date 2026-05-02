using Pinder.Core.Stats;
using System.Collections.Generic;

namespace Pinder.Core.Traps
{
    /// <summary>
    /// Tracks the currently active trap for a character in a conversation.
    ///
    /// Single-slot design (issue #371 redesign): at most one trap is active at
    /// any time. Activating a new trap REPLACES the existing one — traps no
    /// longer stack. Every trap has a fixed 3-turn duration and self-deletes
    /// after affecting the third turn.
    ///
    /// Per-stat lookup methods (<see cref="IsActive"/>, <see cref="GetActive"/>)
    /// are preserved for the existing call sites in <c>RollEngine</c> — they
    /// return state for the single active trap iff its stat matches.
    /// </summary>
    public sealed class TrapState
    {
        // The fixed duration applied to every newly activated trap (#371).
        // The activation turn counts as turn 1 of 3.
        public const int FixedDurationTurns = 3;

        private ActiveTrap? _active;

        /// <summary>True if any trap is currently active.</summary>
        public bool HasActive => _active != null;

        /// <summary>The single active trap, or null when none is active.</summary>
        public ActiveTrap? Active => _active;

        /// <summary>
        /// Returns the single active trap (or null when none is active).
        /// Convenience accessor used by tests and by callers that don't care
        /// which stat triggered the trap. Equivalent to <see cref="Active"/>.
        /// </summary>
        public ActiveTrap? Get() => _active;

        /// <summary>Activate a trap. Replaces the existing active trap (if any).</summary>
        public void Activate(TrapDefinition definition)
        {
            // Per #371: every trap is exactly 3 turns regardless of its
            // declared duration_turns. The activation turn counts as turn 1 of 3.
            _active = new ActiveTrap(definition, FixedDurationTurns);
        }

        /// <summary>
        /// Activate a trap with an explicit turns-remaining override.
        /// Used when restoring session state from a snapshot where the trap is mid-duration.
        /// </summary>
        public void Activate(TrapDefinition definition, int turnsRemaining)
        {
            _active = new ActiveTrap(definition, turnsRemaining);
        }

        /// <summary>
        /// True if a trap is active AND it was triggered by this stat.
        /// (Single-slot model: at most one trap is active at a time.)
        /// </summary>
        public bool IsActive(StatType stat) => _active != null && _active.Definition.Stat == stat;

        /// <summary>Returns the active trap iff its stat matches, else null.</summary>
        public ActiveTrap? GetActive(StatType stat)
        {
            if (_active != null && _active.Definition.Stat == stat) return _active;
            return null;
        }

        /// <summary>
        /// All currently active traps. Single-slot: yields zero or one trap.
        /// Kept as IEnumerable for backward compatibility with prompt-builder
        /// and helper code that iterated the prior multi-slot collection.
        /// </summary>
        public IEnumerable<ActiveTrap> AllActive
        {
            get
            {
                if (_active != null) yield return _active;
            }
        }

        /// <summary>
        /// Advance the active trap's counter by one turn. Removes the trap
        /// if it has reached zero. Call once at the end of each player turn
        /// the trap's effects fired in (including the activation turn).
        /// </summary>
        public void AdvanceTurn()
        {
            if (_active == null) return;
            _active.DecrementTurn();
            if (_active.TurnsRemaining <= 0)
                _active = null;
        }

        /// <summary>
        /// Manually clear the active trap iff it matches the given stat.
        /// Preserved for callers that target a specific stat. Single-slot model.
        /// </summary>
        public void Clear(StatType stat)
        {
            if (_active != null && _active.Definition.Stat == stat)
                _active = null;
        }

        /// <summary>Clear the active trap (if any) — used by SA disarm + RestoreState reset.</summary>
        public void Clear() => _active = null;

        /// <summary>Clear all traps. Equivalent to <see cref="Clear()"/> in the single-slot model.</summary>
        public void ClearAll() => _active = null;

        /// <summary>
        /// Clears the oldest (== only) active trap. Preserved for backward
        /// compatibility with #371's pre-redesign callers; in the single-slot
        /// model this is identical to <see cref="Clear()"/>.
        /// </summary>
        public void ClearOldest() => _active = null;

        /// <summary>
        /// #790 (Phase 4): deep clone for fast-gameplay engine forking. Returns
        /// a new <see cref="TrapState"/> with an independent <see cref="ActiveTrap"/>
        /// instance carrying the same <see cref="TrapDefinition"/> reference
        /// (definitions are immutable) and the same
        /// <see cref="ActiveTrap.TurnsRemaining"/>. Mutating either side does
        /// not affect the other.
        /// </summary>
        public TrapState Clone()
        {
            var copy = new TrapState();
            if (_active != null)
            {
                copy._active = new ActiveTrap(_active.Definition, _active.TurnsRemaining);
            }
            return copy;
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
