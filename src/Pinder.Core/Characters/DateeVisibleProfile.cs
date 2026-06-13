using System.Text;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Issue #562: structured representation of what the player can see
    /// about the datee — the equivalent of a Tinder profile card.
    /// Strict contract: this is what a real dating-app user could plausibly
    /// see from a single profile view. NOT the full LLM system prompt
    /// (psychological stake, full stat block, archetype directives, etc.
    /// stay private to the datee's own assembled prompt).
    ///
    /// Replaces the previous one-line <c>"name: \"bio\" | Wearing: items"</c>
    /// concat in <see cref="Conversation.GameSessionHelpers.BuildDateeVisibleProfile(CharacterProfile)"/>,
    /// which leaked the raw equipped-items list (including items that
    /// wouldn't be visible from a single Tinder photo) and omitted
    /// self-reported demographic info that real Tinder cards do show.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Outfit description vs. raw items list.</b> When the
    /// session-setup outfit-describer LLM call produced a non-empty
    /// description (#333), it replaces the items list — that prose is
    /// the closest thing in the engine to "what the photo looks like."
    /// When the description is empty (no describer wired, or the call
    /// failed) the renderer falls back to the items list as a degraded
    /// but still-bounded signal. Either way the player-LLM never sees
    /// the bare item-id list — only display names.
    /// </para>
    /// <para>
    /// <b>What's intentionally NOT here.</b> Age, height, distance,
    /// orientation tags, photos. The character-file schema doesn't
    /// carry age today and the ticket explicitly defers any
    /// schema-extension to a separate follow-up.
    /// </para>
    /// </remarks>
    public sealed class DateeVisibleProfile
    {
        /// <summary>Display name (e.g. "Sable_xo").</summary>
        public string DisplayName { get; }

        /// <summary>Self-reported gender identity (e.g. "she/her"). May be empty.</summary>
        public string GenderIdentity { get; }

        /// <summary>Player-written bio. May be empty.</summary>
        public string Bio { get; }

        /// <summary>
        /// LLM-generated outfit / scene description (#333) — the
        /// "what the photo looks like" signal. Empty when no describer
        /// was wired or the call failed; the renderer then falls back
        /// to <see cref="EquippedItemDisplayNamesFallback"/>.
        /// </summary>
        public string OutfitDescription { get; }

        /// <summary>
        /// Display names of equipped items. Used as a fallback "outfit"
        /// signal only when <see cref="OutfitDescription"/> is empty.
        /// May itself be empty when neither signal is available.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<string> EquippedItemDisplayNamesFallback { get; }

        public DateeVisibleProfile(
            string displayName,
            string genderIdentity,
            string bio,
            string outfitDescription,
            System.Collections.Generic.IReadOnlyList<string> equippedItemDisplayNamesFallback)
        {
            DisplayName    = displayName    ?? string.Empty;
            GenderIdentity = genderIdentity ?? string.Empty;
            Bio            = bio            ?? string.Empty;
            OutfitDescription = outfitDescription ?? string.Empty;
            EquippedItemDisplayNamesFallback =
                equippedItemDisplayNamesFallback
                ?? new System.Collections.Generic.List<string>();
        }

        /// <summary>
        /// Render the visible profile as a single string suitable for
        /// dropping into the dialogue-options user message
        /// (<c>YOU ARE TALKING TO: {render}</c>).
        ///
        /// Format:
        ///   <c>{DisplayName} ({GenderIdentity}): "{Bio}" | {OutfitDescription or fallback}</c>
        ///
        /// Each segment is omitted when its source is empty so the
        /// rendered string is well-formed even for partial data
        /// (e.g. test fixtures with no bio or no outfit signal).
        /// </summary>
        public string Render()
        {
            var sb = new StringBuilder();
            sb.Append(DisplayName);
            if (!string.IsNullOrWhiteSpace(GenderIdentity))
                sb.Append(" (").Append(GenderIdentity).Append(')');
            if (!string.IsNullOrWhiteSpace(Bio))
                sb.Append(": \"").Append(Bio).Append('"');

            // Outfit description wins over items list when present — see
            // class doc on OutfitDescription. Fall back to the items
            // list only when the describer didn't produce anything.
            if (!string.IsNullOrWhiteSpace(OutfitDescription))
            {
                sb.Append(" | Outfit: ").Append(OutfitDescription);
            }
            else if (EquippedItemDisplayNamesFallback != null
                     && EquippedItemDisplayNamesFallback.Count > 0)
            {
                sb.Append(" | Wearing: ").Append(
                    string.Join(", ", EquippedItemDisplayNamesFallback));
            }

            return sb.ToString();
        }
    }
}
