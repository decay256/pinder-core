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

        // ── §3.3/§3.4 — Delivery instructions (REMOVED, #1125/#1138) ─────────
        //
        // SuccessDeliveryInstruction / BuildSuccessDeliveryInstruction and
        // FailureDeliveryInstruction were only ever consumed by
        // SessionDocumentBuilder.BuildDeliveryPrompt(Ex), the creative
        // delivery-prompt formatter. #1125 collapsed delivery into a
        // deterministic, non-LLM overlay/commit step (DeliveryOverlay), and
        // #1138 removed the prompt builders, so these instructions are fully
        // dead and have been removed. #1153 then removed the last vestige —
        // the dead DeliveryRules class + delivery_rules parse path — entirely.

        // ── §3.5 — Datee response instruction ────────────────────────────

        public static string DateeResponseInstruction => GetCatalogString("datee-response-instruction");

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

        // ── Per-tier datee reaction guidance ─────────────────────────────

        internal static string DateeReactionFumble => GetCatalogString("datee-reaction-fumble");

        internal static string DateeReactionMisfire => GetCatalogString("datee-reaction-misfire");

        internal static string DateeReactionTropeTrap => GetCatalogString("datee-reaction-trope-trap");

        internal static string DateeReactionCatastrophe => GetCatalogString("datee-reaction-catastrophe");

        internal static string DateeReactionLegendary => GetCatalogString("datee-reaction-legendary");

        internal static string DateeHorninessReactionBelowThreshold => GetCatalogString("datee-horniness-reaction-below-threshold");

        internal static string DateeHorninessReactionHighInterest => GetCatalogString("datee-horniness-reaction-high-interest");

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

        // EngineDeliveryBlock (yaml key "engine-delivery-block") REMOVED, #1126.
        // It was the [ENGINE — DELIVERY] block consumed only by the creative
        // delivery-prompt builder (BuildDeliveryPrompt), which #1125/#1138
        // deleted when delivery collapsed into the deterministic, non-LLM
        // DeliveryOverlay. No live builder appended it, so the property and its
        // yaml entry are both gone. (failure-delivery-instruction yaml key was
        // likewise removed — its C# property FailureDeliveryInstruction was
        // already deleted in #1138, leaving the yaml entry orphaned.)

        internal static string EngineDateeBlock => GetCatalogString("engine-datee-block");
    }
}
