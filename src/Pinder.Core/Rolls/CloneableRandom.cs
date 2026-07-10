using System;
using System.Security.Cryptography;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// #790/#425 follow-up (audit 2026-07-10): owned, explicitly cloneable PRNG used
    /// for the two engine RNG slots that <see cref="Conversation.GameSession.Clone"/> /
    /// <see cref="Conversation.GameSession.AdoptStateFrom"/> must deep-copy — the shared
    /// steering/horniness/shadow RNG and the stat-draw RNG (see
    /// <see cref="Conversation.GameSessionConfig.SteeringRng"/> /
    /// <see cref="Conversation.GameSessionConfig.StatDrawRng"/>).
    ///
    /// <para>
    /// Previously, cloning those slots relied on <c>RandomCloner</c> reflecting into
    /// <see cref="System.Random"/>'s private <c>_impl</c> field and BCL-internal impl
    /// types (<c>Net5CompatSeedImpl</c> / <c>CompatPrng</c>) to deep-copy state that
    /// .NET does not expose publicly. That is brittle: a servicing update, a different
    /// runtime, or a trimmed/AOT build can change or remove those internals and silently
    /// break session-fork determinism. This type instead owns its full PRNG state as
    /// four plain <see cref="ulong"/> fields (xoshiro256** — Blackman/Vigna, public
    /// domain) that <see cref="Clone"/> copies directly. No reflection anywhere.
    /// </para>
    ///
    /// <para>
    /// <see cref="CloneableRandom"/> subclasses <see cref="System.Random"/> and overrides
    /// only the protected <see cref="Sample"/> extension point (the officially supported
    /// way to plug a custom generator into <see cref="System.Random"/> since .NET 6 — see
    /// remarks on <c>Random.Sample()</c>). Because <c>Sample</c> is overridden, the base
    /// class routes <c>Next()</c>/<c>Next(int)</c>/<c>Next(int,int)</c>/<c>NextDouble()</c>
    /// through the legacy Sample()-based implementation rather than the internal fast
    /// path, so this type is a drop-in <see cref="System.Random"/> everywhere the engine
    /// expects one (<see cref="RandomDiceRollerAdapter"/>, <c>SteeringEngine</c>,
    /// <c>HorninessEngine</c>, <c>ShadowCheckEngine</c>, <c>OptionFilterEngine.DrawRandomStats</c>) —
    /// this is an internal implementation swap for the cloneable-session-state path, not
    /// a public dice/stat-draw API change. Plain <see cref="System.Random"/> instances
    /// (including test doubles like <c>FixedRandom</c>) remain fully supported anywhere
    /// cloning is not required; <see cref="GameSession.Clone"/> only requires a
    /// <see cref="CloneableRandom"/> when it actually needs to fork the RNG (see
    /// <see cref="RequireCloneable"/>).
    /// </para>
    /// </summary>
    public sealed class CloneableRandom : Random
    {
        private ulong _s0, _s1, _s2, _s3;

        /// <summary>
        /// Deterministic construction from a 32-bit seed. The seed is expanded to the
        /// full 256-bit xoshiro256** state via SplitMix64 (standard practice for this
        /// generator family — avoids the low-quality all-zero-adjacent states a raw
        /// small seed would otherwise produce).
        /// </summary>
        public CloneableRandom(int seed)
        {
            ulong sm = unchecked((ulong)seed);
            _s0 = SplitMix64(ref sm);
            _s1 = SplitMix64(ref sm);
            _s2 = SplitMix64(ref sm);
            _s3 = SplitMix64(ref sm);
        }

        /// <summary>
        /// Non-deterministic construction, seeded from a cryptographically strong
        /// entropy source across the full 256 bits of state (used when
        /// <see cref="Conversation.GameSessionConfig.SteeringRng"/> /
        /// <see cref="Conversation.GameSessionConfig.StatDrawRng"/> are left null).
        /// </summary>
        public CloneableRandom()
        {
            // netstandard2.0 target (Pinder.Core is consumed by Unity) — no Span<T> /
            // RandomNumberGenerator.Fill here, so use the classic byte[] API surface.
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            _s0 = BitConverter.ToUInt64(bytes, 0);
            _s1 = BitConverter.ToUInt64(bytes, 8);
            _s2 = BitConverter.ToUInt64(bytes, 16);
            _s3 = BitConverter.ToUInt64(bytes, 24);
            // xoshiro256** requires non-all-zero state; astronomically unlikely with
            // 256 bits of crypto entropy, but guard explicitly rather than trust luck.
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
        }

        private CloneableRandom(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            _s0 = s0;
            _s1 = s1;
            _s2 = s2;
            _s3 = s3;
        }

        /// <summary>
        /// Deep-copy this generator's state by plain field assignment. The returned
        /// instance produces the identical next-N sequence as this instance would have
        /// at the moment of the call, and is fully independent thereafter — advancing
        /// either instance does not perturb the other. No reflection involved.
        /// </summary>
        public CloneableRandom Clone() => new CloneableRandom(_s0, _s1, _s2, _s3);

        /// <summary>
        /// Casts <paramref name="src"/> to <see cref="CloneableRandom"/> and clones it,
        /// or throws a clear, actionable <see cref="InvalidOperationException"/> if
        /// <paramref name="src"/> is some other <see cref="System.Random"/> (e.g. a
        /// plain <c>new Random(seed)</c> or a test double). Used by
        /// <see cref="Conversation.GameSession.Clone"/> /
        /// <see cref="Conversation.GameSession.AdoptStateFrom"/> — a hard fault here is
        /// intentional (matches the old RandomCloner's fail-fast contract): silently
        /// falling back to a fresh, unrelated RNG would break clone equivalence instead
        /// of surfacing the misconfiguration.
        /// </summary>
        public static CloneableRandom RequireCloneable(Random src, string paramName)
        {
            if (src == null) throw new ArgumentNullException(paramName);
            if (src is CloneableRandom cloneable) return cloneable.Clone();

            throw new InvalidOperationException(
                $"GameSession.Clone()/AdoptStateFrom requires {paramName} to be a " +
                $"{nameof(CloneableRandom)}, but it is a {src.GetType().FullName}. " +
                $"{nameof(Conversation.GameSessionConfig)}.SteeringRng/StatDrawRng default to a " +
                $"{nameof(CloneableRandom)} when left null; only pass an explicit " +
                $"System.Random for sessions that never call Clone()/AdoptStateFrom " +
                $"(e.g. single-turn tests), or pass a {nameof(CloneableRandom)} instance " +
                "for deterministic, cloneable seeding.");
        }

        /// <summary>
        /// The officially supported System.Random extension point since .NET 6: overriding
        /// Sample() routes Next()/Next(int)/Next(int,int)/NextDouble()/NextBytes() on the
        /// base class through the legacy Sample()-based implementation using this method,
        /// rather than the internal (non-overridable) fast xoshiro path. Returns a value in
        /// [0, 1) built from the top 53 bits of a xoshiro256** draw (matches System.Random's
        /// documented double precision).
        /// </summary>
        protected override double Sample()
        {
            ulong result = NextState();
            return (result >> 11) * (1.0 / (1UL << 53));
        }

        // xoshiro256** (Blackman & Vigna, public domain — https://prng.di.unimi.it/xoshiro256starstar.c)
        private ulong NextState()
        {
            ulong s0 = _s0, s1 = _s1, s2 = _s2, s3 = _s3;

            ulong result = RotateLeft(s1 * 5, 7) * 9;

            ulong t = s1 << 17;

            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;
            s2 ^= t;
            s3 = RotateLeft(s3, 45);

            _s0 = s0;
            _s1 = s1;
            _s2 = s2;
            _s3 = s3;

            return result;
        }

        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

        // SplitMix64 (Vigna) — standard seed expansion for xoshiro-family generators.
        private static ulong SplitMix64(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
