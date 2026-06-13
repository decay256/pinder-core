using System;
using Pinder.Core.Conversation;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// #1124: the parsed/structured form of one Game Master turn, as defined by
    /// the canonical <see cref="GmOutputContract"/>. Carries the message text
    /// plus the optional gameplay signals (a tell and/or a weakness window).
    ///
    /// Value-equatable so tests can assert the round-trip
    /// <c>GmOutputContract.Parse(GmOutputContract.Emit(x)).Equals(x)</c>.
    /// </summary>
    public sealed class GmTurnOutput : IEquatable<GmTurnOutput>
    {
        /// <summary>The character's message text (signals stripped).</summary>
        public string Message { get; }

        /// <summary>A tell revealed this turn, or null.</summary>
        public Tell? Tell { get; }

        /// <summary>A weakness window opened this turn, or null.</summary>
        public WeaknessWindow? Weakness { get; }

        /// <summary>
        /// The parenthetical description that accompanied the weakness on the
        /// wire. Held here because <see cref="WeaknessWindow"/> itself does not
        /// carry a description; preserved so the contract round-trips exactly.
        /// </summary>
        public string? WeaknessDescription { get; }

        public GmTurnOutput(
            string message,
            Tell? tell = null,
            WeaknessWindow? weakness = null,
            string? weaknessDescription = null)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Tell = tell;
            Weakness = weakness;
            WeaknessDescription = weaknessDescription;
        }

        public bool Equals(GmTurnOutput? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            if (!string.Equals(Message, other.Message, StringComparison.Ordinal)) return false;
            if (!TellEquals(Tell, other.Tell)) return false;
            if (!WeaknessEquals(Weakness, other.Weakness)) return false;
            if (!string.Equals(WeaknessDescription, other.WeaknessDescription, StringComparison.Ordinal)) return false;
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as GmTurnOutput);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Message?.GetHashCode() ?? 0;
                hash = (hash * 397) ^ (Tell != null ? ((int)Tell.Stat * 17) ^ (Tell.Description?.GetHashCode() ?? 0) : 0);
                hash = (hash * 397) ^ (Weakness != null ? ((int)Weakness.DefendingStat * 17) ^ Weakness.DcReduction : 0);
                hash = (hash * 397) ^ (WeaknessDescription?.GetHashCode() ?? 0);
                return hash;
            }
        }

        private static bool TellEquals(Tell? a, Tell? b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            return a.Stat == b.Stat && string.Equals(a.Description, b.Description, StringComparison.Ordinal);
        }

        private static bool WeaknessEquals(WeaknessWindow? a, WeaknessWindow? b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            return a.DefendingStat == b.DefendingStat && a.DcReduction == b.DcReduction;
        }
    }
}
