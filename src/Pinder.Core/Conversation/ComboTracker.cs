using System.Collections.Generic;
using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Tracks stat play history and detects the 8 named combo sequences from §15.
    /// Pure data tracker — returns combo name/bonus; GameSession applies the effect.
    /// </summary>
    public sealed class ComboTracker
    {
        /// <summary>
        /// Entry recording a single turn's stat and outcome.
        /// </summary>
        private struct TurnEntry
        {
            public StatType Stat;
            public bool Succeeded;
        }

        private readonly List<TurnEntry> _history = new List<TurnEntry>();
        private ComboResult? _lastCombo;
        private bool _pendingTripleBonus;

        /// <summary>
        /// True if The Triple was completed on a previous turn and the bonus
        /// has not yet been consumed. GameSession reads this before calling
        /// RollEngine.Resolve to pass +1 externalBonus.
        /// </summary>
        public bool HasTripleBonus => _pendingTripleBonus;

        /// <summary>
        /// Record the stat used this turn and whether the roll succeeded.
        /// Must be called exactly once per turn, in chronological order.
        /// Internally updates the history buffer and checks for combo completion.
        /// </summary>
        /// <param name="stat">The StatType played this turn.</param>
        /// <param name="succeeded">True if the roll succeeded, false if it failed.</param>
        public void RecordTurn(StatType stat, bool succeeded)
        {
            // Consume triple bonus at the start of each recorded turn
            // (it was already applied to the roll by GameSession)
            if (_pendingTripleBonus)
            {
                _pendingTripleBonus = false;
            }

            _history.Add(new TurnEntry { Stat = stat, Succeeded = succeeded });
            _lastCombo = succeeded ? DetectCombo() : null;

            // If The Triple was detected, set pending bonus for next turn
            if (_lastCombo != null && _lastCombo.IsTriple)
            {
                _pendingTripleBonus = true;
            }
        }

        /// <summary>
        /// After RecordTurn, returns the combo that completed this turn, or null.
        /// Only valid immediately after RecordTurn — returns the result of the
        /// most recent RecordTurn call.
        /// </summary>
        /// <returns>A ComboResult with name and bonus, or null if no combo fired.</returns>
        public ComboResult? CheckCombo()
        {
            return _lastCombo;
        }

        /// <summary>
        /// Preview: returns the combo name that would complete if the given stat
        /// is played and succeeds this turn. Returns null if no combo would complete.
        /// Does NOT mutate internal state — safe to call for each dialogue option.
        /// Used by GameSession.StartTurnAsync to populate DialogueOption.ComboName.
        /// </summary>
        /// <param name="stat">The StatType being previewed.</param>
        /// <returns>Combo name string (e.g. "The Setup") or null.</returns>
        public string? PeekCombo(StatType stat)
        {
            // Temporarily add entry, check, remove
            _history.Add(new TurnEntry { Stat = stat, Succeeded = true });
            var combo = DetectCombo();
            _history.RemoveAt(_history.Count - 1);
            return combo?.Name;
        }

        /// <summary>
        /// Restores combo tracker history and pending triple bonus from snapshot data.
        /// Replaces the current history with the provided entries; clears _lastCombo.
        /// Used by GameSession.RestoreState when resimulating from a snapshot.
        /// </summary>
        /// <param name="history">Ordered (stat name, succeeded) pairs from the snapshot window.</param>
        /// <param name="pendingTripleBonus">Whether The Triple bonus is pending for the next roll.</param>
        public void RestoreFromSnapshot(
            IReadOnlyList<(string StatName, bool Succeeded)> history,
            bool pendingTripleBonus)
        {
            _history.Clear();
            if (history != null)
            {
                foreach (var (statName, succeeded) in history)
                {
                    if (System.Enum.TryParse<StatType>(statName, out var stat))
                        _history.Add(new TurnEntry { Stat = stat, Succeeded = succeeded });
                }
            }
            _pendingTripleBonus = pendingTripleBonus;
            _lastCombo = null;
        }

        /// <summary>
        /// Consumes the Triple bonus without recording a turn.
        /// Used when the player takes a non-Speak action (Read/Recover/Wait)
        /// during the bonus turn.
        /// </summary>
        public void ConsumeTripleBonus()
        {
            _pendingTripleBonus = false;
        }

        /// <summary>
        /// Detects the best combo from current history (last entry is the completing turn).
        /// Returns the highest-interest-bonus combo, or null if none match.
        /// The completing turn (last entry) must have succeeded.
        /// </summary>
        private ComboResult? DetectCombo()
        {
            int count = _history.Count;
            if (count < 2)
                return null;

            var current = _history[count - 1];
            if (!current.Succeeded)
                return null;

            var prev = _history[count - 2];
            ComboResult? best = null;

            // Check 2-stat combos (previous → current)
            // The Recovery: any fail → SelfAwareness success
            if (!prev.Succeeded && current.Stat == StatType.SelfAwareness)
            {
                best = PickBest(best, new ComboResult("The Recovery", 2, false));
            }

            if (prev.Succeeded || IsRecoveryOnly(prev, current))
            {
                // Only check stat-sequence combos if prev succeeded
                // (except Recovery which needs prev to fail)
            }

            // Stat-sequence combos: prev must have any outcome for sequence matching,
            // but per spec the completing roll must succeed (already checked above).
            // The sequence is based on stat types, not success of earlier turns.

            // The Setup: Wit → Charm
            if (prev.Stat == StatType.Wit && current.Stat == StatType.Charm)
                best = PickBest(best, new ComboResult("The Setup", 1, false));

            // The Reveal: Charm → Honesty
            if (prev.Stat == StatType.Charm && current.Stat == StatType.Honesty)
                best = PickBest(best, new ComboResult("The Reveal", 1, false));

            // The Read: SelfAwareness → Honesty
            if (prev.Stat == StatType.SelfAwareness && current.Stat == StatType.Honesty)
                best = PickBest(best, new ComboResult("The Read", 1, false));

            // The Pivot: Honesty → Chaos
            if (prev.Stat == StatType.Honesty && current.Stat == StatType.Chaos)
                best = PickBest(best, new ComboResult("The Pivot", 1, false));

            // The Escalation: Chaos → Rizz
            if (prev.Stat == StatType.Chaos && current.Stat == StatType.Rizz)
                best = PickBest(best, new ComboResult("The Escalation", 1, false));

            // The Disarm: Wit → Honesty
            if (prev.Stat == StatType.Wit && current.Stat == StatType.Honesty)
                best = PickBest(best, new ComboResult("The Disarm", 1, false));

            // The Triple: 3 different stats in 3 consecutive turns, success on 3rd
            if (count >= 3)
            {
                var prev2 = _history[count - 3];
                if (prev2.Stat != prev.Stat
                    && prev2.Stat != current.Stat
                    && prev.Stat != current.Stat)
                {
                    best = PickBest(best, new ComboResult("The Triple", 0, true));
                }
            }

            return best;
        }

        /// <summary>
        /// Returns the combo with the higher interest bonus.
        /// If tied, returns the existing (first-matched) combo.
        /// </summary>
        private static ComboResult PickBest(ComboResult? existing, ComboResult candidate)
        {
            if (existing == null)
                return candidate;
            // Higher interest bonus wins; if tied, first-matched (existing) wins
            return candidate.InterestBonus > existing.InterestBonus ? candidate : existing;
        }

        private static bool IsRecoveryOnly(TurnEntry prev, TurnEntry current)
        {
            return !prev.Succeeded && current.Stat == StatType.SelfAwareness;
        }
    }
}
