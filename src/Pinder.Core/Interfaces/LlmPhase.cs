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

        /// <summary>Player message delivery (final transformation of the chosen option).</summary>
        public const string Delivery = "delivery";

        /// <summary>Steering question for the player.</summary>
        public const string Steering = "steering";

        /// <summary>Opponent reply to the delivered player message.</summary>
        public const string OpponentResponse = "opponent_response";

        /// <summary>Optional narrative beat emitted when opponent interest changes.</summary>
        public const string InterestChangeBeat = "interest_change_beat";

        /// <summary>Horniness overlay rewrite of a delivered message.</summary>
        public const string HorninessOverlay = "horniness_overlay";

        /// <summary>Shadow-stat corruption rewrite of a delivered message.</summary>
        public const string ShadowCorruption = "shadow_corruption";

        /// <summary>Session-setup matchup analysis.</summary>
        public const string MatchupAnalysis = "matchup_analysis";

        /// <summary>Session-setup psychological-stake generation.</summary>
        public const string PsychologicalStake = "psychological_stake";

        /// <summary>Phase could not be determined (decorators may use this when no phase was supplied).</summary>
        public const string Unknown = "unknown";
    }
}
