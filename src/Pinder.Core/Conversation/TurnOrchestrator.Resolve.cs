using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.I18n;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Progression;
using Pinder.Core.Traps;

namespace Pinder.Core.Conversation
{
    internal partial class TurnOrchestrator
    {
        internal async Task<TurnResult> ResolveTurnAsync(
            GameSessionState state,
            int optionIndex,
            CharacterProfile player,
            CharacterProfile opponent,
            System.IProgress<TurnProgressEvent>? progress,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (state.Ended)
                throw new GameEndedException(state.Outcome!.Value);

            if (state.CurrentOptions == null)
                throw new InvalidOperationException("Must call StartTurnAsync before ResolveTurnAsync.");

            if (optionIndex < 0 || optionIndex >= state.CurrentOptions.Length)
                throw new ArgumentOutOfRangeException(nameof(optionIndex),
                    $"Option index {optionIndex} is out of range. Valid range: 0–{state.CurrentOptions.Length - 1}.");

            // Execute Roll Stage
            var rollStage = _rollResolutionStage.Execute(
                state,
                optionIndex,
                player,
                opponent);

            // Execute Delivery/Overlay Stage
            var deliveryStage = await _deliveryStage.ExecuteAsync(
                state,
                rollStage.ChosenOption,
                rollStage.RollResult,
                player,
                opponent,
                progress,
                rollStage.InterestDelta,
                ct).ConfigureAwait(false);

            int interestDelta = deliveryStage.FinalInterestDelta;

            // 9. Check interest threshold crossing → narrative beat
            string? narrativeBeat = null;
            if (rollStage.StateBefore != rollStage.StateAfter)
            {
                narrativeBeat = $"*** Interest state changed to {rollStage.StateAfter} ***";
            }

            // Opponent Response and Turn Assembly
            state.History.Add((player.DisplayName, deliveryStage.DeliveredMessage));

            // Execute Opponent Response Stage
            var opponentStageResult = await _opponentResponseStage.ExecuteAsync(
                state,
                rollStage,
                deliveryStage,
                player,
                opponent,
                progress,
                ct).ConfigureAwait(false);

            var opponentResponse = opponentStageResult.OpponentResponse;
            string opponentMessage = opponentStageResult.OpponentMessage;

            state.ActiveWeakness = opponentResponse.WeaknessWindow;
            state.ActiveTell = opponentResponse.DetectedTell;

            state.History.Add((opponent.DisplayName, opponentMessage));

            state.Traps.AdvanceTurn();

            state.TurnNumber++;

            state.CurrentOptions = null;
            state.CurrentDicePools = null;

            if (rollStage.RollResult.IsSuccess && rollStage.BaseInterestDelta < 0)
                throw new InvariantViolationException(
                    $"#942 invariant violated on turn {state.TurnNumber}: roll.IsSuccess=true " +
                    $"but baseInterestDelta={rollStage.BaseInterestDelta} (expected ≥0). " +
                    "SuccessScale cannot produce a negative delta for a success roll. " +
                    "This indicates a phantom turn produced from a pre-corrupted session state.");

            var stateSnapshot = CreateSnapshot(state, _rules);

            return new TurnResult(
                roll: rollStage.RollResult,
                deliveredMessage: deliveryStage.DeliveredMessage,
                opponentMessage: opponentMessage,
                narrativeBeat: narrativeBeat,
                interestDelta: interestDelta,
                stateAfter: stateSnapshot,
                isGameOver: rollStage.IsGameOver,
                outcome: rollStage.Outcome,
                shadowGrowthEvents: rollStage.ShadowGrowthEvents,
                comboTriggered: rollStage.ComboTriggered,
                callbackBonusApplied: rollStage.CallbackBonus,
                tellReadBonus: rollStage.TellBonus,
                tellReadMessage: rollStage.TellBonus > 0 ? "📖 You read the moment. +2 bonus." : null,
                xpEarned: rollStage.TurnXpEarned,
                baseInterestDelta: rollStage.BaseInterestDelta,
                riskBonusDelta: rollStage.RiskBonusDelta,
                riskTier: rollStage.RollResult.RiskTier,
                comboBonusDelta: rollStage.ComboBonusDelta,
                detectedWindow: opponentResponse.WeaknessWindow,
                steering: deliveryStage.SteeringResult,
                horninessCheck: deliveryStage.HorninessCheckResult,
                tripleBonusApplied: rollStage.TripleBonusApplied,
                horninessInterestPenalty: deliveryStage.HorninessInterestPenalty,
                horninessInterestBefore: deliveryStage.HorninessInterestBefore,
                textDiffs: deliveryStage.TextDiffs.Count > 0 ? deliveryStage.TextDiffs : null,
                shadowCheck: deliveryStage.ShadowCheckResult,
                trapClearedDisplayName: rollStage.TrapClearedDisplayName);
        }
    }
}
