using System;
using System.Collections.Generic;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Pre-drawn dice pool for a single dialogue option. Issue #789, Phase 2 (D1).
    ///
    /// <para>
    /// Holds a deterministic, statically-bounded list of pre-drawn random integers
    /// scoped to one option choice. The values are drawn from the session's
    /// underlying <see cref="Pinder.Core.Interfaces.IDiceRoller"/> at
    /// <c>StartTurnAsync</c> time (display-time, before any LLM call fires) and
    /// then consumed in order by a <see cref="PlaybackDiceRoller"/> when the player
    /// selects this option in <c>ResolveTurnAsync</c>.
    /// </para>
    ///
    /// <para>
    /// The pool's purpose is to make the engine's randomness <em>display-time-known</em>.
    /// At the moment the option list is rendered, every die that <c>ResolveTurnAsync</c>
    /// will consume for this choice is already drawn — so a downstream replay tool
    /// or audit log can record the exact dice the engine will use, BEFORE the LLM
    /// runs. This is the foundation for deterministic replay (#793-equivalent for
    /// the post-Phase-2 engine).
    /// </para>
    ///
    /// <para>
    /// Per Phase 0 (#787) invariant I2, the per-option dice budget inside
    /// <c>ResolveTurnAsync</c> is statically bounded:
    /// <list type="bullet">
    ///   <item>Always: 1× d20 (main roll, <c>RollEngine.cs:52</c>) +
    ///         1× d100 (timing variance, <c>TimingProfile.cs:53</c>).</item>
    ///   <item>Conditionally: 1× d20 (advantage/disadvantage 2nd roll,
    ///         <c>RollEngine.cs:53</c>) iff
    ///         <c>hasAdvantage || hasDisadvantage</c>.</item>
    /// </list>
    /// The advantage/disadvantage flag for a given option is computable at
    /// display-time from session state (<c>_currentHasAdvantage</c>,
    /// <c>_currentHasDisadvantage</c>, the option's stat shadow-pair, the
    /// option's stat trap effect) — never from intermediate LLM output.
    /// That's the architectural property the dice budget locks.
    /// </para>
    ///
    /// <para>
    /// Steering / horniness / shadow rolls in <see cref="Pinder.Core.Conversation.SteeringEngine"/>
    /// and <see cref="Pinder.Core.Conversation.HorninessEngine"/> use a SEPARATE
    /// seeded <see cref="Random"/> and do NOT participate in this pool. They are
    /// deterministic via their own seeding mechanism. See PR #789 body for the
    /// rationale (less-invasive option — no refactor of those engines required
    /// to land Phase 2).
    /// </para>
    /// </summary>
    public sealed class PerOptionDicePool
    {
        private readonly int[] _values;

        /// <summary>
        /// Index of the option this pool was drawn for (0..N-1 where N is the
        /// option count returned from <c>StartTurnAsync</c>). Diagnostic only —
        /// the engine consumes the pool by reference, not by index lookup.
        /// </summary>
        public int OptionIndex { get; }

        /// <summary>
        /// The pre-drawn values, in draw order. The first value is consumed first
        /// by <see cref="PlaybackDiceRoller.Roll"/>. Exposed for snapshot/audit
        /// recording — engine code should not read this directly.
        /// </summary>
        public IReadOnlyList<int> Values => _values;

        /// <summary>Number of pre-drawn values in this pool.</summary>
        public int Count => _values.Length;

        public PerOptionDicePool(int optionIndex, params int[] values)
        {
            if (optionIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(optionIndex));
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            OptionIndex = optionIndex;
            _values = (int[])values.Clone();
        }

        /// <summary>
        /// Get the underlying values as a defensive copy, e.g. for snapshot
        /// serialization. The engine does not call this — it constructs a
        /// <see cref="PlaybackDiceRoller"/> from the pool instead.
        /// </summary>
        public int[] ToArray() => (int[])_values.Clone();
    }
}
