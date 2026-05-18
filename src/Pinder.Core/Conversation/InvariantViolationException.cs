using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Thrown when a per-turn game invariant is violated, indicating a data-corruption
    /// or state-mutation bug (e.g. a successful roll with a negative base interest delta).
    ///
    /// Issue #942: added as a hard guard on the ResolveTurnAsync output path.
    /// The invariant "roll.is_success == true ⇒ base_interest_delta ≥ 0" can never be
    /// violated by correct gameplay logic (SuccessScale always returns ≥ 1 for success
    /// rolls). A violation means a phantom turn was produced from a pre-corrupted session.
    /// </summary>
    public sealed class InvariantViolationException : InvalidOperationException
    {
        public InvariantViolationException(string message)
            : base(message) { }

        public InvariantViolationException(string message, Exception inner)
            : base(message, inner) { }
    }
}
