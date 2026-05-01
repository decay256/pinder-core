using System;
using Pinder.Core.Rolls;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of starting a turn: the dialogue options, current game state, and
    /// per-option pre-rolled dice pools (#789, Phase 2).
    /// </summary>
    public sealed class TurnStart
    {
        /// <summary>The dialogue options available to the player.</summary>
        public DialogueOption[] Options { get; }

        /// <summary>Snapshot of game state at the start of this turn.</summary>
        public GameStateSnapshot State { get; }

        /// <summary>
        /// Pre-rolled dice pools, one per option index. Issue #789, Phase 2 (D1).
        ///
        /// <para>
        /// At the moment <c>StartTurnAsync</c> returns, the engine has already
        /// drawn every die that <c>ResolveTurnAsync</c> will consume for each
        /// possible option choice. The pool at <c>DicePools[i]</c> is the
        /// deterministic dice budget for selecting <c>Options[i]</c>. Public
        /// for audit/replay tooling — engine code uses these pools internally
        /// when the player makes a selection.
        /// </para>
        ///
        /// <para>
        /// Always one entry per <c>Options[i]</c>. Steering / horniness / shadow
        /// rolls are deterministic via their own seeded RNG and are NOT reflected
        /// in these pools (see <see cref="PerOptionDicePool"/> remarks).
        /// </para>
        /// </summary>
        public PerOptionDicePool[] DicePools { get; }

        public TurnStart(DialogueOption[] options, GameStateSnapshot state, PerOptionDicePool[] dicePools)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            State = state ?? throw new ArgumentNullException(nameof(state));
            DicePools = dicePools ?? throw new ArgumentNullException(nameof(dicePools));

            if (dicePools.Length != options.Length)
                throw new ArgumentException(
                    $"DicePools length ({dicePools.Length}) must equal Options length ({options.Length}). " +
                    "Each option must have exactly one pre-rolled dice pool (#789).",
                    nameof(dicePools));
        }

        /// <summary>
        /// Backward-compatible constructor for tests / non-engine callers that
        /// don't pre-roll dice. Each option is paired with an empty pool —
        /// callers using this overload must NOT route through the engine's
        /// pre-rolled <c>ResolveTurnAsync</c> path (it'd under-allocate).
        ///
        /// <para>
        /// Production engine code (<c>GameSession.StartTurnAsync</c>) uses the
        /// 3-arg constructor with real <see cref="PerOptionDicePool"/>s. This
        /// overload is purely a test ergonomic — see <see cref="DicePools"/>.
        /// </para>
        /// </summary>
        public TurnStart(DialogueOption[] options, GameStateSnapshot state)
            : this(options, state, BuildEmptyPools(options))
        {
        }

        private static PerOptionDicePool[] BuildEmptyPools(DialogueOption[] options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var pools = new PerOptionDicePool[options.Length];
            for (int i = 0; i < options.Length; i++)
                pools[i] = new PerOptionDicePool(i);
            return pools;
        }
    }
}
