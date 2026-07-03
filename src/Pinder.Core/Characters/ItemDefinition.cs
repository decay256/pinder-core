using System.Collections.Generic;
using Pinder.Core.Stats;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Immutable definition of a wearable item. Carries all six fragment types
    /// that feed the character-assembly pipeline.
    ///
    /// As of issue #1176, items are keyed by their Unity-verbatim id (the <c>id</c>
    /// JSON field). The old fictional <c>tier</c> field has been REMOVED and
    /// replaced by <c>ItemType</c>.
    ///
    /// AUTHORITY SPLIT:
    ///   Unity  = SSOT for item existence, ids, slot enum, and attachment transforms
    ///            (transforms = graphics, NOT carried here).
    ///   Core   = SSOT for gameplay meaning (stat mods, fragments, item_type).
    ///
    /// SLOT VOCABULARY:
    ///   Accessories / Outfits use Unity's slot enum strings verbatim:
    ///     Head | Face | Body | Waist | Special
    ///   LookCatalog items use their logical slot:
    ///     Hair | Arms
    ///   TatooCatalog items use their logical slot:
    ///     Tattoo | Sticker
    ///   (Sticker and Tattoo share the same id pool; the item_type field
    ///    distinguishes them — same id can be a tattoo when in a tattoo slot
    ///    and a sticker when in a sticker slot at the Unity equip layer. Core
    ///    only stores tattoo-type entries; the sticker semantic is a Unity-side
    ///    equip-context distinction and does not require separate core records.)
    ///
    /// SCHEMA VERSION: 3 (item_id → id; tier removed; item_type added; conflict_tags and priority removed).
    /// </summary>
    public sealed class ItemDefinition
    {
        /// <summary>Unity-verbatim item id (e.g. "head_tophat", "vest1", "classic2").</summary>
        public string ItemId       { get; }

        /// <summary>Human-readable display name.</summary>
        public string DisplayName  { get; }

        /// <summary>
        /// Slot string. For accessories/outfits: Unity enum verbatim (Head/Face/Body/Waist/Special).
        /// For LookCatalog: Hair or Arms. For TatooCatalog: Tattoo or Sticker.
        /// </summary>
        public string Slot         { get; }

        /// <summary>
        /// Item type: one of accessory | outfit | hair | arms | tattoo | sticker.
        /// Sticker and tattoo share the same id pool; item_type disambiguates usage.
        /// </summary>
        public string ItemType     { get; }

        /// <summary>Flat bonuses/penalties to base stats.</summary>
        public IReadOnlyDictionary<StatType, int> StatModifiers { get; }

        public string PersonalityFragment    { get; }
        public string BackstoryFragment      { get; }
        public string TextingStyleFragment   { get; }
        public string[] ArchetypeTendencies  { get; }
        public TimingModifier ResponseTimingModifier { get; }

        public ItemDefinition(
            string itemId,
            string displayName,
            string slot,
            string itemType,
            IReadOnlyDictionary<StatType, int> statModifiers,
            string personalityFragment,
            string backstoryFragment,
            string textingStyleFragment,
            string[] archetypeTendencies,
            TimingModifier responseTimingModifier)
        {
            ItemId                  = itemId;
            DisplayName             = displayName ?? itemId;
            Slot                    = slot;
            ItemType                = itemType ?? "accessory";
            StatModifiers           = statModifiers;
            PersonalityFragment     = personalityFragment;
            BackstoryFragment       = backstoryFragment;
            TextingStyleFragment    = textingStyleFragment;
            ArchetypeTendencies     = archetypeTendencies ?? System.Array.Empty<string>();
            ResponseTimingModifier  = responseTimingModifier;
        }
    }
}
