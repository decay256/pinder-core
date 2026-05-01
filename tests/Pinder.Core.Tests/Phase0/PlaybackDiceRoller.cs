using System;
using System.Collections.Generic;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Tests.Phase0
{
    /// <summary>
    /// I2: counting deterministic dice roller. Wraps a fixed sequence of values
    /// (queue, FIFO) and counts every <see cref="Roll"/> call. <see cref="IsDrained"/>
    /// reports whether the consumed-count exactly equals the prepared-count.
    ///
    /// This is the architectural canary for the upcoming Phase 2 (#789) refactor:
    /// the engine's per-turn dice budget is statically bounded for a given option
    /// choice. If a future PR introduces a draw site whose count depends on
    /// intermediate LLM output (or worse, on the LLM's randomness), the budget
    /// will overshoot or undershoot and the I2 test will fail loudly — flagging
    /// the architectural regression before it lands.
    ///
    /// Compared to the existing <c>FixedDice</c> in <c>GameSessionTests.cs</c>:
    ///  - exposes <see cref="Consumed"/> and <see cref="Prepared"/> counts
    ///  - exposes <see cref="IsDrained"/> for a single-line invariant check
    ///  - records the (sides, returnedValue) of every draw for diagnostic dumps
    /// </summary>
    public sealed class PlaybackDiceRoller : IDiceRoller
    {
        private readonly Queue<int> _values;
        private readonly List<(int Sides, int Value)> _trace;
        private readonly int _initialCount;

        public PlaybackDiceRoller(params int[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            _values = new Queue<int>(values);
            _trace = new List<(int, int)>(values.Length);
            _initialCount = values.Length;
        }

        /// <summary>Total number of d20/d100/etc draws prepared at construction.</summary>
        public int Prepared => _initialCount;

        /// <summary>Number of <see cref="Roll"/> calls made so far.</summary>
        public int Consumed => _trace.Count;

        /// <summary>
        /// Number of values still queued for future <see cref="Roll"/> calls.
        /// Equivalent to <c>Prepared - Consumed</c> but reads more naturally in
        /// drain assertions.
        /// </summary>
        public int Remaining => _values.Count;

        /// <summary>
        /// True iff every prepared value was consumed exactly once. The invariant
        /// gate the I2 test asserts.
        /// </summary>
        public bool IsDrained => _values.Count == 0 && _trace.Count == _initialCount;

        /// <summary>
        /// Sequence of (sides, returnedValue) draws in call order. Used for
        /// diagnostic dumps when a budget assertion fails.
        /// </summary>
        public IReadOnlyList<(int Sides, int Value)> Trace => _trace;

        public int Roll(int sides)
        {
            if (sides < 1) throw new ArgumentOutOfRangeException(nameof(sides));
            if (_values.Count == 0)
            {
                throw new InvalidOperationException(
                    $"PlaybackDiceRoller exhausted: tried to draw d{sides} after consuming all {_initialCount} prepared values. " +
                    $"Trace so far: [{string.Join(", ", FormatTrace())}].");
            }
            int v = _values.Dequeue();
            _trace.Add((sides, v));
            return v;
        }

        private IEnumerable<string> FormatTrace()
        {
            foreach (var (s, v) in _trace) yield return $"d{s}={v}";
        }
    }
}
