using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;

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
        private GameOutcome? _terminalSettlementOutcome;
        private int? _terminalSettlementBaseXp;
        private double? _terminalSettlementMultiplier;
        private int? _terminalSettlementBonusXp;

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

        /// <summary>Terminal outcome already settled into this ledger, if any.</summary>
        public GameOutcome? TerminalSettlementOutcome => _terminalSettlementOutcome;

        /// <summary>XP total before terminal outcome bonus was applied.</summary>
        public int? TerminalSettlementBaseXp => _terminalSettlementBaseXp;

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
        /// Applies a terminal settlement exactly once. A second settlement for the
        /// same outcome is a no-op; a different outcome is invalid.
        /// </summary>
        public ProgressionSettlement RecordTerminalSettlement(ProgressionSettlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            if (_terminalSettlementOutcome.HasValue)
            {
                if (_terminalSettlementOutcome.Value != settlement.Outcome)
                {
                    throw new InvalidOperationException(
                        $"XP ledger was already settled for {_terminalSettlementOutcome.Value}; cannot settle {settlement.Outcome}.");
                }

                return new ProgressionSettlement(
                    settlement.Outcome,
                    _terminalSettlementBaseXp!.Value,
                    _terminalSettlementMultiplier!.Value,
                    0,
                    TotalXp,
                    settlement.CurrencyPerXp,
                    TotalXp * settlement.CurrencyPerXp);
            }

            if (settlement.BaseXp != TotalXp)
            {
                throw new InvalidOperationException(
                    $"Terminal settlement base XP {settlement.BaseXp} does not match ledger total {TotalXp}.");
            }

            _terminalSettlementOutcome = settlement.Outcome;
            _terminalSettlementBaseXp = settlement.BaseXp;
            _terminalSettlementMultiplier = settlement.OutcomeMultiplier;
            _terminalSettlementBonusXp = settlement.BonusXp;

            if (settlement.BonusXp > 0)
                Record($"OutcomeBonus_{settlement.Outcome}", settlement.BonusXp);

            return settlement;
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
            copy._terminalSettlementOutcome = _terminalSettlementOutcome;
            copy._terminalSettlementBaseXp = _terminalSettlementBaseXp;
            copy._terminalSettlementMultiplier = _terminalSettlementMultiplier;
            copy._terminalSettlementBonusXp = _terminalSettlementBonusXp;
            return copy;
        }
    }
}
