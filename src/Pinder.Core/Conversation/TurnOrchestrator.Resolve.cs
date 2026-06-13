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
            CharacterProfile datee,
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
                datee);

            // Execute Delivery/Overlay Stage
            var deliveryStage = await _deliveryStage.ExecuteAsync(
                state,
                rollStage.ChosenOption,
                rollStage.RollResult,
                player,
                datee,
                progress,
                rollStage.InterestDelta,
                ct).ConfigureAwait(false);

            // Centrally apply proposed state mutations from DeliveryStage
            if (deliveryStage.ShadowCorrection != 0)
            {
                // #1095: A shadow trap caps the positive interest delta at 1; it is NOT a
                // turn failure. Apply the (signed) truncation adjustment to interest, but do
                // NOT override the roll verdict to Miss — the verdict/momentum stay SUCCESS.
                // (Previously this called ApplyFinalOverride(Miss, tier), demoting the turn.)
                state.Interest.Apply(deliveryStage.ShadowCorrection);
            }
            if (deliveryStage.HorninessInterestPenalty != 0)
            {
                state.Interest.Apply(deliveryStage.HorninessInterestPenalty);
            }

            int interestDelta = deliveryStage.FinalInterestDelta;

            // 9. Check interest threshold crossing → narrative beat
            string? narrativeBeat = null;
            if (rollStage.StateBefore != rollStage.StateAfter)
            {
                narrativeBeat = $"*** Interest state changed to {rollStage.StateAfter} ***";
            }

            // Datee Response and Turn Assembly
            state.History.Add((player.DisplayName, deliveryStage.DeliveredMessage));

            // Execute Datee Response Stage
            var dateeStageResult = await _dateeResponseStage.ExecuteAsync(
                state,
                rollStage,
                deliveryStage,
                player,
                datee,
                progress,
                ct).ConfigureAwait(false);

            var dateeResponse = dateeStageResult.DateeResponse;
            string dateeMessage = dateeStageResult.DateeMessage;

            state.ActiveWeakness = dateeResponse.WeaknessWindow;
            state.ActiveTell = dateeResponse.DetectedTell;

            state.History.Add((datee.DisplayName, dateeMessage));

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

            var stateSnapshot = TurnOrchestratorHelpers.CreateSnapshot(state, _rules);

            return new TurnResult(
                roll: rollStage.RollResult,
                deliveredMessage: deliveryStage.DeliveredMessage,
                dateeMessage: dateeMessage,
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
                detectedWindow: dateeResponse.WeaknessWindow,
                steering: deliveryStage.SteeringResult,
                horninessCheck: deliveryStage.HorninessCheckResult,
                tripleBonusApplied: rollStage.TripleBonusApplied,
                horninessInterestPenalty: deliveryStage.HorninessInterestPenalty,
                horninessInterestBefore: deliveryStage.HorninessInterestBefore,
                textDiffs: deliveryStage.TextDiffs.Count > 0 ? deliveryStage.TextDiffs : null,
                shadowCheck: deliveryStage.ShadowCheckResult,
                trapClearedDisplayName: rollStage.TrapClearedDisplayName,
                shadowInterestDelta: deliveryStage.ShadowCorrection,
                delayPenalty: 0);
        }
    }
}
