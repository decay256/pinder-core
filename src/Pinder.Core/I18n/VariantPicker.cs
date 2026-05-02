using System;

namespace Pinder.Core.I18n
{
    /// <summary>
    /// Deterministic variant picker for event-interpretation strings —
    /// engine-side mirror of <c>variantIndex</c> in
    /// <c>frontend/src/i18n/useText.ts</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sprint: i18n string extraction (pinder-web issue #436 / Phase 1.5).
    /// </para>
    /// <para>
    /// Algorithm: 32-bit FNV-1a over the kind string, mixed with the
    /// turn number as 4 little-endian bytes, then modulo variant count.
    /// Constants <c>0x811c9dc5</c> (offset basis) and <c>0x01000193</c>
    /// (prime) are stable across language ports — change them and you
    /// break the cross-language fixture in
    /// <c>frontend/src/i18n/useText.test.ts</c> AND the parallel C#
    /// fixture in <c>VariantPickerTests</c>.
    /// </para>
    /// <para>
    /// Pure; no IO, no allocation beyond the input string. Safe to call
    /// inside hot paths.
    /// </para>
    /// </remarks>
    public static class VariantPicker
    {
        /// <summary>
        /// FNV-1a 32-bit offset basis. DO NOT CHANGE without updating
        /// the JS port + cross-language test fixtures.
        /// </summary>
        public const uint FnvOffsetBasis = 0x811c9dc5u;

        /// <summary>
        /// FNV-1a 32-bit prime. DO NOT CHANGE without updating the JS
        /// port + cross-language test fixtures.
        /// </summary>
        public const uint FnvPrime = 0x01000193u;

        /// <summary>
        /// Pick the deterministic variant index for
        /// <paramref name="kind"/> at <paramref name="turnNumber"/>,
        /// modulo <paramref name="variantCount"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">when kind is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">when variantCount &lt;= 0.</exception>
        public static int PickIndex(string kind, int turnNumber, int variantCount)
        {
            if (kind is null) throw new ArgumentNullException(nameof(kind));
            if (variantCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(variantCount),
                    "variantCount must be > 0");

            uint h = FnvOffsetBasis;
            // FNV-1a over the kind characters. JS uses charCodeAt (UTF-16
            // code units); C# `string` indexing is also UTF-16 — so a
            // BMP-only kind name produces the same byte stream in both.
            // All seeded event kinds are ASCII; surrogate-pair kinds
            // would diverge across ports and are rejected at the loader.
            for (int i = 0; i < kind.Length; i++)
            {
                h ^= kind[i];
                h = unchecked(h * FnvPrime);
            }
            // Mix in the turn number as 4 little-endian bytes — same
            // shape the JS port emits via shift+mask.
            uint tn = unchecked((uint)turnNumber);
            h ^= tn & 0xFFu;
            h = unchecked(h * FnvPrime);
            h ^= (tn >> 8) & 0xFFu;
            h = unchecked(h * FnvPrime);
            h ^= (tn >> 16) & 0xFFu;
            h = unchecked(h * FnvPrime);
            h ^= (tn >> 24) & 0xFFu;
            h = unchecked(h * FnvPrime);
            // Modulo a non-negative count keeps the result in [0, count).
            return (int)(h % (uint)variantCount);
        }
    }
}
