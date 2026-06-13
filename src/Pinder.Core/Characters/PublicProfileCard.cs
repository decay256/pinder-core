using System.Text;

namespace Pinder.Core.Characters
{
    /// <summary>
    /// Issue #1123: the minimal, public "dating-app card" view of a character —
    /// the only thing the OTHER session's Game-Master is allowed to see about a
    /// character it is not playing.
    ///
    /// <para>
    /// Strict bleed-isolation contract. Each two-session GM session
    /// (<see cref="Pinder.Core.Conversation.DateeContext"/> and the avatar
    /// delivery path) carries ONLY its own character's private stake in its
    /// system prompt. The opposing character appears solely as (a) this public
    /// card and (b) sent messages in the labelled transcript. The full assembled
    /// system prompt (psychological stake, full stat block, archetype
    /// directives, voice spec) of the opposing character is NEVER carried across
    /// the session boundary.
    /// </para>
    ///
    /// <para>
    /// This is the symmetric sibling of <see cref="DateeVisibleProfile"/> (the
    /// dialogue-options "YOU ARE TALKING TO" card). Where
    /// <see cref="DateeVisibleProfile"/> renders the datee for the avatar's
    /// option-generation prompt, <see cref="PublicProfileCard"/> is the neutral
    /// cross-session card carried on the per-call context DTOs so neither
    /// session leaks the other's private spec.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Mirrors a single Tinder-style profile view: display name, self-reported
    /// gender identity, player-written bio, and an outfit/scene signal (LLM
    /// description when available, equipped-item display names as a degraded
    /// fallback). Deliberately excludes everything private: stake, stats,
    /// archetype, the full §3.1 system prompt.
    /// </remarks>
    public sealed class PublicProfileCard
    {
        /// <summary>Display name (e.g. "Sable_xo").</summary>
        public string DisplayName { get; }

        /// <summary>Self-reported gender identity (e.g. "she/her"). May be empty.</summary>
        public string GenderIdentity { get; }

        /// <summary>Player-written bio. May be empty.</summary>
        public string Bio { get; }

        /// <summary>
        /// LLM-generated outfit / scene description — the "what the photo looks
        /// like" signal. Empty when no describer was wired or the call failed;
        /// the renderer then falls back to
        /// <see cref="EquippedItemDisplayNamesFallback"/>.
        /// </summary>
        public string OutfitDescription { get; }

        /// <summary>
        /// Display names of equipped items. Used as a fallback "outfit" signal
        /// only when <see cref="OutfitDescription"/> is empty. May itself be
        /// empty when neither signal is available.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<string> EquippedItemDisplayNamesFallback { get; }

        public PublicProfileCard(
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

        /// <summary>An empty card (no public info). Used when a character is unavailable.</summary>
        public static PublicProfileCard Empty { get; } =
            new PublicProfileCard(string.Empty, string.Empty, string.Empty, string.Empty,
                new System.Collections.Generic.List<string>());

        /// <summary>
        /// Render the public card as a single string suitable for dropping into
        /// the opposing session's system prompt as a dating-app card.
        ///
        /// Format:
        ///   <c>{DisplayName} ({GenderIdentity}): "{Bio}" | {OutfitDescription or fallback}</c>
        ///
        /// Each segment is omitted when its source is empty so the rendered
        /// string is well-formed even for partial data.
        /// </summary>
        public string Render()
        {
            var sb = new StringBuilder();
            sb.Append(DisplayName);
            if (!string.IsNullOrWhiteSpace(GenderIdentity))
                sb.Append(" (").Append(GenderIdentity).Append(')');
            if (!string.IsNullOrWhiteSpace(Bio))
                sb.Append(": \"").Append(Bio).Append('"');

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
