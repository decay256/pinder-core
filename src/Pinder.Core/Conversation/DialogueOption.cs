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

        /// <summary>The intended message text before degradation.</summary>
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
        /// True if this option was replaced by Madness T3 (≥18) with unhinged text.
        /// The stat is preserved for roll purposes, but the LLM generates tainted dialogue.
        /// </summary>
        public bool IsUnhinged { get; }

        public DialogueOption(
            StatType stat,
            string intendedText,
            int? callbackTurnNumber = null,
            string? comboName = null,
            bool hasTellBonus = false,
            bool hasWeaknessWindow = false,
            bool isUnhinged = false)
        {
            Stat = stat;
            IntendedText = intendedText ?? throw new System.ArgumentNullException(nameof(intendedText));
            CallbackTurnNumber = callbackTurnNumber;
            ComboName = comboName;
            HasTellBonus = hasTellBonus;
            HasWeaknessWindow = hasWeaknessWindow;
            IsUnhinged = isUnhinged;
        }
    }
}
