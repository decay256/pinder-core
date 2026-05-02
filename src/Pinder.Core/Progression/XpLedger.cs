using System;
using System.Collections.Generic;

namespace Pinder.Core.Progression
{
    /// <summary>
    /// Accumulates XP events with source labels during a single game session.
    /// Immutable event log — events can only be added, never removed.
    /// </summary>
    public sealed class XpLedger
    {
        /// <summary>
        /// A single XP award event with source label and amount.
        /// </summary>
        public sealed class XpEvent
        {
            /// <summary>Human-readable label identifying the XP source (e.g. "Success_DC_Mid", "Nat20", "DateSecured").</summary>
            public string Source { get; }

            /// <summary>XP amount awarded. Always positive.</summary>
            public int Amount { get; }

            public XpEvent(string source, int amount)
            {
                if (string.IsNullOrEmpty(source))
                    throw new ArgumentException("Source must not be null or empty.", nameof(source));
                if (amount <= 0)
                    throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than 0.");

                Source = source;
                Amount = amount;
            }
        }

        private readonly List<XpEvent> _events;
        private int _drainCursor;

        /// <summary>Creates an empty ledger with TotalXp == 0 and no events.</summary>
        public XpLedger()
        {
            _events = new List<XpEvent>();
            _drainCursor = 0;
        }

        /// <summary>Sum of all recorded event amounts. Starts at 0.</summary>
        public int TotalXp { get; private set; }

        /// <summary>All recorded XP events in chronological order.</summary>
        public IReadOnlyList<XpEvent> Events => _events;

        /// <summary>
        /// Records an XP event with the given source label and amount.
        /// </summary>
        /// <param name="source">Human-readable label. Must not be null or empty.</param>
        /// <param name="amount">XP amount. Must be greater than 0.</param>
        /// <exception cref="ArgumentException">If source is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">If amount is less than or equal to 0.</exception>
        public void Record(string source, int amount)
        {
            var evt = new XpEvent(source, amount);
            _events.Add(evt);
            TotalXp += amount;
        }

        /// <summary>
        /// Returns all events recorded since the last drain (or since construction if never drained).
        /// Advances the internal cursor. Returns an empty list if no new events.
        /// The returned list is a new List — caller owns it.
        /// Does NOT remove events from the ledger or affect TotalXp.
        /// </summary>
        public IReadOnlyList<XpEvent> DrainTurnEvents()
        {
            if (_drainCursor >= _events.Count)
                return new List<XpEvent>();

            var result = new List<XpEvent>(_events.Count - _drainCursor);
            for (int i = _drainCursor; i < _events.Count; i++)
            {
                result.Add(_events[i]);
            }
            _drainCursor = _events.Count;
            return result;
        }

        /// <summary>
        /// #790 (Phase 4): deep clone for fast-gameplay engine forking. Returns
        /// an independent <see cref="XpLedger"/> with the same recorded events
        /// (each <see cref="XpEvent"/> is itself immutable), the same
        /// <see cref="TotalXp"/>, and the same drain cursor. Mutating either
        /// side (recording new events / draining) does not affect the other.
        /// </summary>
        public XpLedger Clone()
        {
            var copy = new XpLedger();
            copy._events.AddRange(_events); // XpEvent is immutable.
            copy.TotalXp = TotalXp;
            copy._drainCursor = _drainCursor;
            return copy;
        }
    }
}
