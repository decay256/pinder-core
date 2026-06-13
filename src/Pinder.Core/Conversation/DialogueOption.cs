using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// A single dialogue option presented to the player during their turn.
    /// </summary>
    public sealed class DialogueOption
    {
        /// <summary>The stat used for this option's roll.</summary>
        public StatType Stat { get; }

        /// <summary>
        /// The candidate line for this option.
        ///
        /// <para>
        /// #1125 — semantics changed: this is now the <b>full, sendable</b>
        /// line the avatar GM produced, NOT a gist/intent to be expanded by a
        /// second "delivery" LLM call. The delivery LLM call was collapsed into
        /// a deterministic, non-LLM commit/overlay step
        /// (<see cref="DeliveryOverlay"/>): on a success the picked line commits
        /// verbatim; on a failure it is degraded deterministically. The property
        /// name is retained (rather than introducing a parallel <c>FullText</c>
        /// field) to keep the option DTO non-jagged and avoid a dual-field
        /// migration — the field simply now carries the final line. A steering
        /// question, when one fires, is appended to this line before the overlay.
        /// </para>
        /// </summary>
        public string IntendedText { get; }

        /// <summary>Turn number for callback bonus, if applicable.</summary>
        public int? CallbackTurnNumber { get; }

        /// <summary>Name of the combo being completed, if any.</summary>
        public string? ComboName { get; }

        /// <summary>Whether this option has a tell bonus.</summary>
        public bool HasTellBonus { get; }

        /// <summary>
        /// True if a weakness window is active for this option's defending stat.
        /// UI displays a 🔓 icon when true. The DC shown already reflects the reduction.
        /// </summary>
        public bool HasWeaknessWindow { get; }

        /// <summary>
        /// True if this option was replaced by the Madness T3 (≥18) shadow threshold effect.
        /// The LLM should generate unhinged/chaotic text for this option slot.
        /// </summary>
        public bool IsUnhingedReplacement { get; }

        public DialogueOption(
            StatType stat,
            string intendedText,
            int? callbackTurnNumber = null,
            string? comboName = null,
            bool hasTellBonus = false,
            bool hasWeaknessWindow = false,
            bool isUnhingedReplacement = false)
        {
            Stat = stat;
            IntendedText = intendedText ?? throw new System.ArgumentNullException(nameof(intendedText));
            CallbackTurnNumber = callbackTurnNumber;
            ComboName = comboName;
            HasTellBonus = hasTellBonus;
            HasWeaknessWindow = hasWeaknessWindow;
            IsUnhingedReplacement = isUnhingedReplacement;
        }
    }
}
