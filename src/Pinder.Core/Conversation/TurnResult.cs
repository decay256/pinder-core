using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Rolls;
using Pinder.Core.Text;
using Pinder.Core.Progression;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of resolving a turn: roll outcome, messages, and updated game state.
    /// </summary>
    public sealed class TurnResult
    {
        /// <summary>The full roll result.</summary>
        public RollResult Roll { get; }

        /// <summary>The player's message text after degradation.</summary>
        public string DeliveredMessage { get; }

        /// <summary>The datee's response message.</summary>
        public string DateeMessage { get; }

        /// <summary>Narrative beat text if an interest threshold was crossed, null otherwise.</summary>
        public string? NarrativeBeat { get; }

        /// <summary>Net interest delta applied this turn (includes momentum).</summary>
        public int InterestDelta { get; }

        /// <summary>
        /// Interest delta contributed by the shadow-misfire correction (DeliveryStage).
        /// Non-zero only when a shadow overlay fired on a successful roll and forced
        /// the interest delta down to the failure scale. 0 if no shadow correction fired.
        /// </summary>
        public int ShadowInterestDelta { get; }

        /// <summary>Base interest delta from success scale or failure scale (before risk/combo bonuses).</summary>
        public int BaseInterestDelta { get; }

        /// <summary>Risk tier bonus added on success. 0 on failure.</summary>
        public int RiskBonusDelta { get; }

        /// <summary>Interest bonus from combo trigger. 0 if no combo.</summary>
        public int ComboBonusDelta { get; }

        /// <summary>Snapshot of game state after this turn.</summary>
        public GameStateSnapshot StateAfter { get; }

        /// <summary>True if the game ended this turn.</summary>
        public bool IsGameOver { get; }

        /// <summary>The outcome if the game ended, null otherwise.</summary>
        public GameOutcome? Outcome { get; }

        /// <summary>Human-readable descriptions of shadow stat growth that occurred this turn. Empty if none.</summary>
        public IReadOnlyList<string> ShadowGrowthEvents { get; }

        /// <summary>Name/identifier of the combo triggered this turn, or null if no combo fired.</summary>
        public string? ComboTriggered { get; }

        /// <summary>Callback bonus modifier applied to the roll or interest delta this turn. 0 if none.</summary>
        public int CallbackBonusApplied { get; }

        /// <summary>Tell-read bonus modifier applied this turn. 0 if no tell-read occurred.</summary>
        public int TellReadBonus { get; }

        /// <summary>Descriptive message about the tell that was read, or null if none.</summary>
        public string? TellReadMessage { get; }

        /// <summary>Risk tier of the action chosen by the player this turn.</summary>
        public RiskTier RiskTier { get; }

        /// <summary>Amount of XP earned from this turn's outcome. 0 if none.</summary>
        public int XpEarned { get; }

        /// <summary>
        /// Weakness window detected in the datee's response this turn, if any.
        /// The caller (UI) may use this to preview the next turn's opportunity.
        /// </summary>
        public WeaknessWindow? DetectedWindow { get; }

        /// <summary>
        /// Result of the steering roll this turn. Contains roll details and the
        /// appended question text if the roll succeeded.
        /// </summary>
        public SteeringRollResult Steering { get; }

        /// <summary>
        /// Result of the per-turn horniness overlay check. NotPerformed if skipped.
        /// </summary>
        public HorninessCheckResult HorninessCheck { get; }

        /// <summary>
        /// Result of the per-turn shadow check. NotPerformed if the chosen stat had no active paired shadow.
        /// </summary>
        public ShadowCheckResult ShadowCheck { get; }

        /// <summary>
        /// Roll bonus applied from a previous Triple combo (+2). 0 if no Triple bonus was consumed this turn.
        /// </summary>
        public int TripleBonusApplied { get; }

        /// <summary>
        /// Interest delta from the horniness penalty (floor(interest/2) - interestBefore), when overlay fired and interest > 0.
        /// 0 if no penalty was applied.
        /// </summary>
        public int HorninessInterestPenalty { get; }

        /// <summary>
        /// Interest value before horniness penalty, for display. 0 if no penalty fired.
        /// </summary>
        public int HorninessInterestBefore { get; }

        public int ActiveTrapInterestPenalty { get; }
        public int ActiveTrapInterestBefore { get; }
        public int ActiveTrapInterestPenaltyPercent { get; }

        /// <summary>Word-level diffs for each text transform layer that changed the message.</summary>
        public IReadOnlyList<TextDiff> TextDiffs { get; }

        /// <summary>
        /// Itemized per-source interest breakdown for this turn.
        /// Only non-zero components are included. The sum of all
        /// <see cref="InterestBreakdownItem.Delta"/> values equals
        /// <see cref="InterestDelta"/> (sum invariant).
        /// Sources: base_roll, risk_tier, combo, shadow_misfire,
        /// horniness_trope_trap, delay_penalty.
        /// </summary>
        public IReadOnlyList<InterestBreakdownItem> InterestBreakdown { get; }

        /// <summary>
        /// Itemized per-source XP breakdown for this turn.
        /// </summary>
        public IReadOnlyList<XpLedger.XpEvent> XpBreakdown { get; }

        /// <summary>
        /// Display name of the trap that was disarmed at the start of this turn
        /// by the player selecting a Self-Awareness option (issue #371). Null when
        /// no SA-disarm fired (no trap was active, or chosen option was not SA).
        /// The frontend uses this signal to show a "Trap cleared" toast/event.
        /// </summary>
        public string? TrapClearedDisplayName { get; }

        public ResolvedRevelationTarget? ResolvedTarget { get; }
        public string? CognitiveSubtext { get; }
        public int HungerForIntimacy { get; }
        public int TerrorOfRejection { get; }

        public TurnResult(
            RollResult roll,
            string deliveredMessage,
            string dateeMessage,
            string? narrativeBeat,
            int interestDelta,
            GameStateSnapshot stateAfter,
            bool isGameOver,
            GameOutcome? outcome,
            IReadOnlyList<string>? shadowGrowthEvents = null,
            string? comboTriggered = null,
            int callbackBonusApplied = 0,
            int tellReadBonus = 0,
            string? tellReadMessage = null,
            RiskTier riskTier = RiskTier.Safe,
            int xpEarned = 0,
            int baseInterestDelta = 0,
            int riskBonusDelta = 0,
            int comboBonusDelta = 0,
            WeaknessWindow? detectedWindow = null,
            SteeringRollResult steering = null,
            HorninessCheckResult horninessCheck = null,
            int tripleBonusApplied = 0,
            int horninessInterestPenalty = 0,
            int horninessInterestBefore = 0,
            IReadOnlyList<TextDiff>? textDiffs = null,
            ShadowCheckResult shadowCheck = null,
            string? trapClearedDisplayName = null,
            int shadowInterestDelta = 0,
            int delayPenalty = 0,
            int activeTrapInterestPenalty = 0,
            int activeTrapInterestBefore = 0,
            int activeTrapInterestPenaltyPercent = 0,
            ResolvedRevelationTarget? resolvedTarget = null,
            string? cognitiveSubtext = null,
            int hungerForIntimacy = 0,
            int terrorOfRejection = 0,
            IReadOnlyList<XpLedger.XpEvent>? xpBreakdown = null)
        {
            Roll = roll ?? throw new ArgumentNullException(nameof(roll));
            DeliveredMessage = deliveredMessage ?? throw new ArgumentNullException(nameof(deliveredMessage));
            DateeMessage = dateeMessage ?? throw new ArgumentNullException(nameof(dateeMessage));
            NarrativeBeat = narrativeBeat;
            InterestDelta = interestDelta;
            StateAfter = stateAfter ?? throw new ArgumentNullException(nameof(stateAfter));
            IsGameOver = isGameOver;
            Outcome = outcome;
            ShadowGrowthEvents = shadowGrowthEvents ?? Array.Empty<string>();
            ComboTriggered = comboTriggered;
            CallbackBonusApplied = callbackBonusApplied;
            TellReadBonus = tellReadBonus;
            TellReadMessage = tellReadMessage;
            RiskTier = riskTier;
            XpEarned = xpEarned;
            BaseInterestDelta = baseInterestDelta;
            RiskBonusDelta = riskBonusDelta;
            ComboBonusDelta = comboBonusDelta;
            DetectedWindow = detectedWindow;
            Steering = steering ?? SteeringRollResult.NotAttempted;
            HorninessCheck = horninessCheck ?? HorninessCheckResult.NotPerformed;
            TripleBonusApplied = tripleBonusApplied;
            HorninessInterestPenalty = horninessInterestPenalty;
            HorninessInterestBefore = horninessInterestBefore;
            TextDiffs = textDiffs ?? Array.Empty<TextDiff>();
            ShadowCheck = shadowCheck ?? ShadowCheckResult.NotPerformed;
            TrapClearedDisplayName = trapClearedDisplayName;
            ShadowInterestDelta = shadowInterestDelta;
            ActiveTrapInterestPenalty = activeTrapInterestPenalty;
            ActiveTrapInterestBefore = activeTrapInterestBefore;
            ActiveTrapInterestPenaltyPercent = activeTrapInterestPenaltyPercent;
            ResolvedTarget = resolvedTarget;
            CognitiveSubtext = cognitiveSubtext;
            HungerForIntimacy = hungerForIntimacy;
            TerrorOfRejection = terrorOfRejection;
            XpBreakdown = xpBreakdown ?? Array.Empty<XpLedger.XpEvent>();
            InterestBreakdown = BuildBreakdown(
                baseInterestDelta, riskBonusDelta, comboBonusDelta,
                shadowInterestDelta, horninessInterestPenalty, delayPenalty, activeTrapInterestPenalty);
        }

        private static IReadOnlyList<InterestBreakdownItem> BuildBreakdown(
            int baseDelta,
            int riskBonus,
            int comboBonus,
            int shadowDelta,
            int horninesspenalty,
            int delayPenalty,
            int activeTrapPenalty)
        {
            var items = new List<InterestBreakdownItem>(7);
            if (baseDelta != 0)
                items.Add(new InterestBreakdownItem("base_roll", "Base roll", baseDelta));
            if (riskBonus != 0)
                items.Add(new InterestBreakdownItem("risk_tier", "Risk tier bonus", riskBonus));
            if (comboBonus != 0)
                items.Add(new InterestBreakdownItem("combo", "Combo bonus", comboBonus));
            if (shadowDelta != 0)
                items.Add(new InterestBreakdownItem("shadow_misfire", "Shadow misfire correction", shadowDelta));
            if (horninesspenalty != 0)
                items.Add(new InterestBreakdownItem("horniness_trope_trap", "Horniness trope-trap", horninesspenalty));
            if (delayPenalty != 0)
                items.Add(new InterestBreakdownItem("delay_penalty", "Delay penalty", delayPenalty));
            if (activeTrapPenalty != 0)
                items.Add(new InterestBreakdownItem("active_trap_penalty", "Active trap penalty", activeTrapPenalty));
            return items.AsReadOnly();
        }
    }
}
