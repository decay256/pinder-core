using System;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Instruction templates loaded from <c>data/prompts/templates.yaml</c>.
    /// Each template uses {placeholder} tokens filled by SessionDocumentBuilder at call time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #871 Phase 5 (#875): all const-string prompt content has been
    /// deleted. The yaml catalog is now the sole source of truth. Every
    /// property below reads from the <see cref="Catalog"/>, which MUST be
    /// wired at startup via <see cref="PromptWiring"/>. Accessing any
    /// property without a wired catalog throws
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// </remarks>
    public static class PromptTemplates
    {
        /// <summary>
        /// The unified <see cref="PromptCatalog"/> providing yaml-sourced
        /// prompt content. Set by <c>PromptWiring.Wire()</c> at startup.
        /// After Phase 5 there is no const fallback — every property
        /// throws when the catalog is null.
        /// </summary>
        public static PromptCatalog? Catalog { get; set; }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string GetCatalogString(string key)
        {
            var catalog = Catalog
                ?? throw new InvalidOperationException(
                    "PromptTemplates.Catalog is not wired. Call PromptWiring.Wire() at startup.");
            var entry = catalog.TryGet(key)
                ?? throw new InvalidOperationException(
                    $"prompt-catalog: missing required key '{key}'. The yaml file is incomplete or missing.");
            return entry.SystemPrompt
                ?? throw new InvalidOperationException(
                    $"prompt-catalog: key '{key}' has no system_prompt. Check the yaml file.");
        }

        // ── §3.2 — Dialogue options instruction ─────────────────────────────

        public static string DialogueOptionsInstruction => GetCatalogString("dialogue-options-instruction");

        // ── §3.3 — Success delivery instruction ─────────────────────────────

        public static string SuccessDeliveryInstruction => BuildSuccessDeliveryInstruction(null);

        public static string BuildSuccessDeliveryInstruction(DeliveryRules rules)
        {
            string clean = (rules != null && !string.IsNullOrEmpty(rules.Clean))
                ? rules.Clean.TrimEnd() : GetCatalogString("default-clean");
            string strong = (rules != null && !string.IsNullOrEmpty(rules.Strong))
                ? rules.Strong.TrimEnd() : GetCatalogString("default-strong");
            string critical = (rules != null && !string.IsNullOrEmpty(rules.Critical))
                ? rules.Critical.TrimEnd() : GetCatalogString("default-critical");
            string exceptional = (rules != null && !string.IsNullOrEmpty(rules.Exceptional))
                ? rules.Exceptional.TrimEnd() : GetCatalogString("default-exceptional");
            string test = (rules != null && !string.IsNullOrEmpty(rules.Test))
                ? rules.Test.TrimEnd() : GetCatalogString("default-test");
            string registerInstruction = (rules != null && !string.IsNullOrEmpty(rules.RegisterInstruction))
                ? rules.RegisterInstruction.TrimEnd() : GetCatalogString("default-register-instruction");
            string mediumRule = (rules != null && !string.IsNullOrEmpty(rules.MediumRule))
                ? rules.MediumRule.TrimEnd() : GetCatalogString("default-medium-rule");

            return "Write as {player_name}.\n" +
                "The intended message is the player's plan. Your job is to make it land.\n" +
                "You beat the DC by {beat_dc_by}.\n" +
                "\n" +
                "YOUR TIER: {tier_instruction}\n" +
                "\n" +
                "Other tiers for reference:\n" +
                "- Clean success (margin 1-4): " + clean + "\n" +
                "- Strong success (margin 5-9): " + strong + "\n" +
                "- Critical success (margin 10-14): " + critical + "\n" +
                "- Exceptional (margin 15+): " + exceptional + "\n" +
                "- Critical success / Nat 20: legendary. One sentence can be more effective than a paragraph if it's exactly right.\n" +
                "\n" +
                test + "\n" +
                "\n" +
                "MEDIUM RULE: " + mediumRule + "\n" +
                "\n" +
                registerInstruction + " Don't explain the success.\n" +
                "HARD RULE: Do not append a new sentence or em-dash continuation to the end of the message. Do not make the message longer. Do not split a single-paragraph message into multiple paragraphs. Rewrite — do not extend.\n" +
                "Output only the message text.";
        }

        // ── §3.4 — Failure delivery instruction ─────────────────────────────

        public static string FailureDeliveryInstruction => GetCatalogString("failure-delivery-instruction");

        // ── §3.5 — Opponent response instruction ────────────────────────────

        public static string OpponentResponseInstruction => GetCatalogString("opponent-response-instruction");

        // ── §3.8 — Interest beat instruction ────────────────────────────────

        public static string InterestBeatInstruction => GetCatalogString("interest-beat-instruction");

        internal static string InterestBeatAbove15 => GetCatalogString("interest-beat-above15");

        internal static string InterestBeatBelow8 => GetCatalogString("interest-beat-below8");

        internal static string InterestBeatDateSecured => GetCatalogString("interest-beat-date-secured");

        internal static string InterestBeatUnmatched => GetCatalogString("interest-beat-unmatched");

        internal static string InterestBeatGeneric => GetCatalogString("interest-beat-generic");

        // ── Pivot directive ─────────────────────────────────────────────────

        internal static string PivotDirective => GetCatalogString("pivot-directive");

        // ── Resistance descriptors ──────────────────────────────────────────

        internal static string ResistanceActiveDisengagement => GetCatalogString("resistance-active-disengagement");

        internal static string ResistanceSkepticalInterest => GetCatalogString("resistance-skeptical-interest");

        internal static string ResistanceUnstableAgreement => GetCatalogString("resistance-unstable-agreement");

        internal static string ResistanceDeliberateApproach => GetCatalogString("resistance-deliberate-approach");

        internal static string ResistanceAlmostConvinced => GetCatalogString("resistance-almost-convinced");

        internal static string ResistanceDissolved => GetCatalogString("resistance-dissolved");

        // ── Per-tier opponent reaction guidance ─────────────────────────────

        internal static string OpponentReactionFumble => GetCatalogString("opponent-reaction-fumble");

        internal static string OpponentReactionMisfire => GetCatalogString("opponent-reaction-misfire");

        internal static string OpponentReactionTropeTrap => GetCatalogString("opponent-reaction-trope-trap");

        internal static string OpponentReactionCatastrophe => GetCatalogString("opponent-reaction-catastrophe");

        internal static string OpponentReactionLegendary => GetCatalogString("opponent-reaction-legendary");

        // ── Interest narrative bands ────────────────────────────────────────

        internal static string InterestNarrative_1_4 => GetCatalogString("interest-narrative-1-4");

        internal static string InterestNarrative_5_9 => GetCatalogString("interest-narrative-5-9");

        internal static string InterestNarrative_10_14 => GetCatalogString("interest-narrative-10-14");

        internal static string InterestNarrative_15_20 => GetCatalogString("interest-narrative-15-20");

        internal static string InterestNarrative_21_24 => GetCatalogString("interest-narrative-21-24");

        internal static string InterestNarrative_25 => GetCatalogString("interest-narrative-25");

        /// <summary>
        /// Returns the interest narrative string for a given interest level.
        /// Six configurable bands as specified in §544.
        /// </summary>
        internal static string GetInterestNarrative(int interest)
        {
            if (interest >= 25) return InterestNarrative_25;
            if (interest >= 21) return InterestNarrative_21_24;
            if (interest >= 15) return InterestNarrative_15_20;
            if (interest >= 10) return InterestNarrative_10_14;
            if (interest >= 5) return InterestNarrative_5_9;
            if (interest >= 1) return InterestNarrative_1_4;
            return "Unmatched. The conversation is over.";
        }

        // ── [ENGINE] block format templates ─────────────────────────────────

        internal static string EngineOptionsBlock => GetCatalogString("engine-options-block");

        internal static string EngineDeliveryBlock => GetCatalogString("engine-delivery-block");

        internal static string EngineOpponentBlock => GetCatalogString("engine-opponent-block");
    }
}
