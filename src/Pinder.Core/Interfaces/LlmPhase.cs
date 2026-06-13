namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Canonical phase identifiers passed through <see cref="ILlmTransport.SendAsync"/>
    /// and <see cref="IStreamingLlmTransport.SendStreamAsync"/> so that decorators
    /// (e.g. snapshot recorders, telemetry) can label exchanges without re-deriving
    /// the phase from prompt text.
    /// </summary>
    /// <remarks>
    /// Phase strings are part of the public contract: external decorators rely on
    /// these exact values. Changes are breaking — add new constants instead of
    /// renaming existing ones. Values are intentionally lower-snake-case to match
    /// existing snapshot/telemetry conventions.
    /// </remarks>
    public static class LlmPhase
    {
        /// <summary>Player dialogue-options generation.</summary>
        public const string DialogueOptions = "dialogue_options";

        /// <summary>
        /// RETIRED — no longer emitted (#1125/#1129). The standalone "delivery"
        /// creative LLM call was collapsed into the deterministic
        /// <see cref="Pinder.Core.Conversation.DeliveryOverlay"/> commit step:
        /// the avatar GM now returns full, sendable candidate lines, so there is
        /// no second "delivery" LLM call to label. This constant is RETAINED (not
        /// renamed/removed) only so historical audit/cost rows that reference the
        /// <c>"delivery"</c> phase string still render with a typed label —
        /// mirroring the #827 MatchupAnalysis/MatchupSummary precedent below.
        /// Phase strings are a public decorator contract; do not re-purpose this
        /// value for a new LLM phase.
        /// </summary>
        public const string Delivery = "delivery";

        /// <summary>Steering question for the player.</summary>
        public const string Steering = "steering";

        /// <summary>Datee reply to the delivered player message.</summary>
        public const string DateeResponse = "datee_response";

        /// <summary>Optional narrative beat emitted when datee interest changes.</summary>
        public const string InterestChangeBeat = "interest_change_beat";

        /// <summary>Horniness overlay rewrite of a delivered message.</summary>
        public const string HorninessOverlay = "horniness_overlay";

        /// <summary>Shadow-stat corruption rewrite of a delivered message.</summary>
        public const string ShadowCorruption = "shadow_corruption";

        /// <summary>
        /// Trap overlay rewrite of a delivered message (#371). Fires only on
        /// turns 2 and 3 of an active trap (the activation turn's taint is the
        /// roll-modification, not a separate overlay). Adds a `Trap (X)`
        /// text-diff layer when the rewrite changes the message.
        /// </summary>
        public const string TrapOverlay = "trap_overlay";

        // #827: MatchupAnalysis ("matchup_analysis") and MatchupSummary
        // ("matchup_summary") were removed in setup-trim phase 1. Existing
        // historical audit/cost rows referencing those phase strings are
        // rendered as untyped strings, which is acceptable for historical
        // data per the ticket. Do not re-introduce these constants.

        /// <summary>Session-setup psychological-stake generation.</summary>
        public const string PsychologicalStake = "psychological_stake";

        /// <summary>
        /// Same-turn callback-phrase strip pass (issue #339). No LLM call —
        /// purely a regex post-process that runs alongside the other text
        /// transform layers — but the constant is reserved here so any
        /// future LLM-driven variant lands under the same phase id and
        /// snapshot tooling has a stable label.
        /// </summary>
        public const string CallbackStrip = "callback_strip";

        /// <summary>
        /// Session-setup outfit description generation (issue #333).
        /// One LLM call per session that produces the brief paragraph
        /// describing what each character is wearing for the turn-0
        /// scene-setting entry.
        /// </summary>
        public const string OutfitDescription = "outfit_description";

        /// <summary>
        /// Session-setup dramatic-arc generation (issue #821).
        /// One LLM call per session that produces a 3-5 sentence
        /// narrative arc (setup, escalation, turning point, resolution)
        /// appended to the datee system prompt as soft guardrails.
        /// </summary>
        public const string DramaticArc = "dramatic_arc";

        /// <summary>
        /// Session-setup background-story generation (issue #820).
        /// One LLM call per character at creation time that weaves
        /// assembled background fragments into a 3-5 sentence cohesive
        /// narrative stored on-disk and surfaced on the Character Sheet.
        /// </summary>
        public const string BackgroundStory = "background_story";

        /// <summary>Phase could not be determined (decorators may use this when no phase was supplied).</summary>
        public const string Unknown = "unknown";
    }
}
