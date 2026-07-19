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

        /// <summary>User-visible one-line summary for inventory/profile display.</summary>
        public string SummaryText  { get; }

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

        /// <summary>Cash price in the shop. Starter items are always 0; locked items are positive.</summary>
        public int ShopPrice { get; }

        /// <summary>Whether a new player starts with this item unlocked.</summary>
        public bool StarterUnlocked { get; }

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
            TimingModifier responseTimingModifier,
            string summaryText = "",
            int shopPrice = 0,
            bool starterUnlocked = true)
        {
            ItemId                  = itemId;
            DisplayName             = displayName ?? itemId;
            SummaryText             = summaryText ?? string.Empty;
            Slot                    = slot;
            ItemType                = itemType ?? "accessory";
            if (shopPrice < 0)
                throw new System.ArgumentOutOfRangeException(nameof(shopPrice), "Shop price must be non-negative.");
            if (starterUnlocked && shopPrice != 0)
                throw new System.ArgumentException("Starter-unlocked items must have shop_price 0.", nameof(shopPrice));
            if (!starterUnlocked && shopPrice <= 0)
                throw new System.ArgumentException("Locked items must have a positive shop_price.", nameof(shopPrice));
            ShopPrice               = shopPrice;
            StarterUnlocked         = starterUnlocked;
            StatModifiers           = statModifiers;
            PersonalityFragment     = personalityFragment;
            BackstoryFragment       = backstoryFragment;
            TextingStyleFragment    = textingStyleFragment;
            ArchetypeTendencies     = archetypeTendencies ?? System.Array.Empty<string>();
            ResponseTimingModifier  = responseTimingModifier;
        }
    }
}
