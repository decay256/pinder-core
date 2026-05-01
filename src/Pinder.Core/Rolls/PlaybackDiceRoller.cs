using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Production deterministic dice roller. Issue #789, Phase 2 (D1).
    /// Wraps a fixed, FIFO-ordered sequence of pre-drawn integers (typically
    /// sourced from a <see cref="PerOptionDicePool"/>) and returns them in
    /// order from <see cref="Roll"/>.
    ///
    /// <para>
    /// Used by <c>GameSession.ResolveTurnAsync</c> to consume the pool that
    /// was drawn at <c>StartTurnAsync</c> (display) time. Drains exactly when
    /// the per-option dice budget is exactly consumed (Phase 0 I2 invariant);
    /// throws on under-allocation; reports <see cref="IsDrained"/>=false on
    /// over-allocation. Both failure modes are loud, by design — see Phase 0
    /// regression-pins-787.md, I2.
    /// </para>
    ///
    /// <para>
    /// This is the production class. The Phase 0 test fixture
    /// <c>tests/Pinder.Core.Tests/Phase0/PlaybackDiceRoller.cs</c> retains its
    /// own copy with extra diagnostic state (the <c>Trace</c> list of
    /// <c>(sides, value)</c> pairs). The fixture's test variant is kept distinct
    /// to avoid churning the Phase 0 regression pins on this PR — a future
    /// cleanup may merge them.
    /// </para>
    /// </summary>
    public sealed class PlaybackDiceRoller : IDiceRoller
    {
        private readonly Queue<int> _values;
        private readonly int _initialCount;
        private int _consumed;

        /// <summary>
        /// Construct from a raw value array. The values are consumed in order:
        /// first call to <see cref="Roll"/> returns <c>values[0]</c>, second
        /// returns <c>values[1]</c>, etc.
        /// </summary>
        public PlaybackDiceRoller(params int[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            _values = new Queue<int>(values);
            _initialCount = values.Length;
            _consumed = 0;
        }

        /// <summary>
        /// Construct from a <see cref="PerOptionDicePool"/>. Equivalent to
        /// <c>new PlaybackDiceRoller(pool.ToArray())</c> — convenience for
        /// the engine's hot path.
        /// </summary>
        public PlaybackDiceRoller(PerOptionDicePool pool)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            _values = new Queue<int>(pool.ToArray());
            _initialCount = pool.Count;
            _consumed = 0;
        }

        /// <summary>Total values prepared at construction.</summary>
        public int Prepared => _initialCount;

        /// <summary>Number of <see cref="Roll"/> calls made so far.</summary>
        public int Consumed => _consumed;

        /// <summary>Values still queued (i.e. <see cref="Prepared"/> minus <see cref="Consumed"/>).</summary>
        public int Remaining => _values.Count;

        /// <summary>
        /// True iff every prepared value was consumed exactly once. The
        /// architectural invariant the Phase 0 I2 budget asserts — when this
        /// returns false the per-option dice budget over-allocated; when
        /// <see cref="Roll"/> throws the budget under-allocated.
        /// </summary>
        public bool IsDrained => _values.Count == 0 && _consumed == _initialCount;

        /// <summary>
        /// Returns the next pre-drawn value (FIFO). The <paramref name="sides"/>
        /// parameter is ignored for value resolution but validated for sanity —
        /// the value comes from the pool, not from a fresh draw.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">If sides &lt; 1.</exception>
        /// <exception cref="InvalidOperationException">
        /// If the pool is exhausted. Indicates the per-option dice budget
        /// under-allocated.
        /// </exception>
        public int Roll(int sides)
        {
            if (sides < 1) throw new ArgumentOutOfRangeException(nameof(sides));
            if (_values.Count == 0)
            {
                throw new InvalidOperationException(
                    $"PlaybackDiceRoller exhausted: tried to draw d{sides} after consuming all {_initialCount} prepared values. " +
                    $"This indicates the per-option dice budget under-allocated for this turn. See PR #789 / regression-pins-787.md I2.");
            }
            _consumed++;
            return _values.Dequeue();
        }
    }
}
