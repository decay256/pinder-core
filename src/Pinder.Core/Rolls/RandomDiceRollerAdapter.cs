using System;
using Pinder.Core.Interfaces;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Lightweight adapter that wraps a <see cref="System.Random"/> as an <see cref="IDiceRoller"/>.
    /// Not thread-safe (same contract as the underlying <see cref="System.Random"/>).
    /// Used by per-check engines (HorninessEngine, ShadowCheckEngine) that own a single-threaded RNG.
    /// </summary>
    internal sealed class RandomDiceRollerAdapter : IDiceRoller
    {
        private readonly Random _rng;

        public RandomDiceRollerAdapter(Random rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public int Roll(int sides)
        {
            if (sides < 1) throw new ArgumentOutOfRangeException(nameof(sides));
            return _rng.Next(1, sides + 1);
        }
    }
}
