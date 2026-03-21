using Pinder.Core.Interfaces;
using System;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Default dice roller using System.Random.
    /// Thread-safe via lock (shared Random is not thread-safe).
    /// </summary>
    public sealed class SystemRandomDiceRoller : IDiceRoller
    {
        private readonly Random _rng;
        private readonly object _lock = new object();

        public SystemRandomDiceRoller(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public int Roll(int sides)
        {
            if (sides < 1) throw new ArgumentOutOfRangeException(nameof(sides));
            lock (_lock)
                return _rng.Next(1, sides + 1);
        }
    }
}
