using Pinder.Core.Interfaces;
using System;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Wraps an inner IDiceRoller and applies a difficulty bias by capping the
    /// effective roll result. A positive <paramref name="dcBias"/> raises all DCs
    /// by that amount (making rolls harder), equivalent to reducing every roll total
    /// by the bias value.
    ///
    /// Implemented by subtracting dcBias from every d20 roll result, clamped to [1, sides].
    /// Nat 20 and Nat 1 special cases still fire on the *raw* roll before bias is applied.
    /// </summary>
    public sealed class BiasedDiceRoller : IDiceRoller
    {
        private readonly IDiceRoller _inner;
        private readonly int _dcBias;

        /// <param name="inner">Underlying dice roller.</param>
        /// <param name="dcBias">
        /// Positive = harder (roll result reduced by this amount).
        /// Negative = easier (roll result increased by this amount).
        /// 0 = no effect.
        /// </param>
        public BiasedDiceRoller(IDiceRoller inner, int dcBias)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _dcBias = dcBias;
        }

        public int Roll(int sides)
        {
            int raw = _inner.Roll(sides);
            if (_dcBias == 0) return raw;
            // Preserve Nat 1 and Nat 20 on d20 rolls
            if (sides == 20 && (raw == 1 || raw == 20)) return raw;
            int biased = raw - _dcBias;
            return Math.Max(1, Math.Min(sides, biased));
        }
    }
}
