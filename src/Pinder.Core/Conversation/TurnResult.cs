using System;
using System.Collections.Generic;
using Pinder.Core.Rolls;

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

        /// <summary>The opponent's response message.</summary>
        public string OpponentMessage { get; }

        /// <summary>Narrative beat text if an interest threshold was crossed, null otherwise.</summary>
        public string? NarrativeBeat { get; }

        /// <summary>Net interest delta applied this turn (includes momentum).</summary>
        public int InterestDelta { get; }

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
        /// Weakness window detected in the opponent's response this turn, if any.
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
        /// Roll bonus applied from a previous Triple combo (+1). 0 if no Triple bonus was consumed this turn.
        /// </summary>
        public int TripleBonusApplied { get; }

        public TurnResult(
            RollResult roll,
            string deliveredMessage,
            string opponentMessage,
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
            int tripleBonusApplied = 0)
        {
            Roll = roll ?? throw new ArgumentNullException(nameof(roll));
            DeliveredMessage = deliveredMessage ?? throw new ArgumentNullException(nameof(deliveredMessage));
            OpponentMessage = opponentMessage ?? throw new ArgumentNullException(nameof(opponentMessage));
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
        }
    }
}
