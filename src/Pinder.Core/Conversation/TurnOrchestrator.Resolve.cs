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

            // Authorization is a transaction precondition. Denial must occur before
            // roll, delivery, retries, provider calls, or mutation of the working state.
            RoleFactAccessGuard.RequireAdmitted(
                state.CurrentDateeReactionTarget?.Fact,
                datee.CharacterId,
                ConversationParticipantRole.Datee,
                _onDiagnostic,
                _agentJournalContext,
                state.TurnNumber,
                OperationalDiagnosticOperationKind.DateeResponse);
            RoleFactAccessGuard.RequireAdmitted(
                state.CurrentDateeCognitiveSubtextFact,
                datee.CharacterId,
                ConversationParticipantRole.Datee,
                _onDiagnostic,
                _agentJournalContext,
                state.TurnNumber,
                OperationalDiagnosticOperationKind.DateeResponse);

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
            InterestState finalInterestAfterState = TurnOrchestratorHelpers.ResolveInterestState(
                state,
                _rules,
                _onRuleResolution);
            CharacterEmotionalStatus playerStatus = CharacterEmotionalStatusResolver.Resolve(
                player, _hungerForIntimacy, _terrorOfRejection);
            CharacterEmotionalStatus dateeStatus = CharacterEmotionalStatusResolver.Resolve(
                datee, _hungerForIntimacy, _terrorOfRejection);

            // 9. Check interest threshold crossing → narrative beat
            string? narrativeBeat = null;
            if (rollStage.StateBefore != finalInterestAfterState)
            {
                narrativeBeat = $"*** Interest state changed to {finalInterestAfterState} ***";
            }

            // Execute Datee Response Stage. DateeContext.ConversationHistory is
            // prior completed exchanges only; the current event is carried via
            // PlayerDeliveredMessage until the DATEE reply succeeds.
            var dateeStageResult = await _dateeResponseStage.ExecuteAsync(
                state,
                rollStage,
                deliveryStage,
                player,
                datee,
                progress,
                ct,
                finalInterestAfterState,
                playerStatus,
                dateeStatus).ConfigureAwait(false);

            var dateeResponse = dateeStageResult.DateeResponse;
            string dateeMessage = dateeStageResult.DateeMessage;
            AcceptedDateeResponsePlanState? acceptedPlanState =
                dateeResponse.EmotionalReactionDebug?.ResponsePlanState;
            CharacterEmotionalDirection? acceptedDirection =
                dateeResponse.EmotionalReactionDebug?.Direction;
            if (acceptedPlanState != null && acceptedDirection == null)
                throw new InvalidOperationException("datee_response_replay.emotional_direction.required");
            DateeResponseReplayState? responseReplayState = acceptedPlanState == null
                ? null
                : new DateeResponseReplayState(
                    responseTurn: state.TurnNumber,
                    postTurnNumber: state.TurnNumber + 1,
                    deliveredMessage: deliveryStage.DeliveredMessage,
                    acceptedDateeMessage: dateeMessage,
                    responseDelayMinutes: dateeStageResult.ResponseDelayMinutes,
                    interestBefore: rollStage.InterestBefore,
                    interestBeforeState: rollStage.StateBefore,
                    interestAfterState: finalInterestAfterState,
                    deliveryTier: rollStage.RollResult.Tier,
                    rollStat: rollStage.RollResult.Stat,
                    outcomeIntensity: RollOutcomeIntensityContract.FromRollResult(rollStage.RollResult),
                    horninessOverlayApplied: deliveryStage.HorninessCheckResult.OverlayApplied,
                    horninessTier: deliveryStage.HorninessCheckResult.Tier,
                    acceptedEmotionalDirection: CharacterEmotionalDirectionSummary.FromDirection(
                        state.TurnNumber,
                        acceptedDirection!),
                    activeTrapIds: GameSessionHelpers.GetActiveTrapNames(state.Traps),
                    activeTrapInstructions: GameSessionHelpers.GetActiveTrapInstructions(state.Traps)
                        ?? Array.Empty<string>());
            if (ShouldSpendAvatarTarget(optionIndex, state.CurrentOptions.Length)
                && state.CurrentAvatarRevelationTarget != null)
                MarkTargetSpent(state.CurrentAvatarRevelationTarget.ResolvedTarget, state.AvatarSpentBackstoryIndices, state.AvatarSpentStakeIndices);

            state.ActiveWeakness = dateeResponse.WeaknessWindow != null 
                ? new WeaknessWindow(dateeResponse.WeaknessWindow.DefendingStat, dateeResponse.WeaknessWindow.DcReduction * 2) 
                : null;
            state.ActiveTell = dateeResponse.DetectedTell;

            state.History.Add((player.DisplayName, deliveryStage.DeliveredMessage));
            state.History.Add((datee.DisplayName, dateeMessage));

            state.Traps.AdvanceTurn();

            state.TurnNumber++;
            state.LastDateeResponseReplayState = responseReplayState;

            state.CurrentOptions = null;
            state.CurrentDicePools = null;

            if (rollStage.RollResult.IsSuccess && rollStage.BaseInterestDelta < 0)
                throw new InvariantViolationException(
                    $"#942 invariant violated on turn {state.TurnNumber}: roll.IsSuccess=true " +
                    $"but baseInterestDelta={rollStage.BaseInterestDelta} (expected ≥0). " +
                    "SuccessScale cannot produce a negative delta for a success roll. " +
                    "This indicates a phantom turn produced from a pre-corrupted session state.");

            var stateSnapshot = TurnOrchestratorHelpers.CreateSnapshot(state, _rules);

            int playerHfi = playerStatus.HungerForIntimacy;
            int playerTor = playerStatus.TerrorOfRejection;
            int dateeHfi = dateeStatus.HungerForIntimacy;
            int dateeTor = dateeStatus.TerrorOfRejection;
            CharacterEmotionalDebugInfo? dateeEmotionalDebug =
                dateeResponse.EmotionalReactionDebug?.WithStatus(dateeHfi, dateeTor);

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
                tellReadMessage: rollStage.TellBonus > 0 ? $"📖 You read the moment. +{rollStage.TellBonus} bonus." : null,
                xpEarned: rollStage.TurnXpEarned,
                xpBreakdown: rollStage.TurnXpEvents,
                baseInterestDelta: rollStage.BaseInterestDelta,
                riskBonusDelta: rollStage.RiskBonusDelta,
                riskTier: rollStage.RollResult.RiskTier,
                comboBonusDelta: rollStage.ComboBonusDelta,
                detectedWindow: dateeResponse.WeaknessWindow != null 
                    ? new WeaknessWindow(dateeResponse.WeaknessWindow.DefendingStat, dateeResponse.WeaknessWindow.DcReduction * 2) 
                    : null,
                steering: deliveryStage.SteeringResult,
                horninessCheck: deliveryStage.HorninessCheckResult,
                tripleBonusApplied: rollStage.TripleBonusApplied,
                horninessInterestPenalty: deliveryStage.HorninessInterestPenalty,
                horninessInterestBefore: deliveryStage.HorninessInterestBefore,
                textDiffs: deliveryStage.TextDiffs.Count > 0 ? deliveryStage.TextDiffs : null,
                shadowCheck: deliveryStage.ShadowCheckResult,
                trapClearedDisplayName: rollStage.TrapClearedDisplayName,
                shadowInterestDelta: deliveryStage.ShadowCorrection,
                activeTrapInterestPenalty: rollStage.ActiveTrapInterestPenalty,
                activeTrapInterestBefore: rollStage.ActiveTrapInterestBefore,
                activeTrapInterestPenaltyPercent: rollStage.ActiveTrapInterestPenaltyPercent,
                resolvedTarget: state.CurrentAvatarRevelationTarget?.ResolvedTarget,
                cognitiveSubtext: state.CurrentDateeCognitiveSubtext,
                hungerForIntimacy: playerHfi,
                terrorOfRejection: playerTor,
                dateeHungerForIntimacy: dateeHfi,
                dateeTerrorOfRejection: dateeTor,
                emotionalReactionDebug: dateeEmotionalDebug);
        }

        internal static bool ShouldSpendAvatarTarget(int optionIndex, int optionCount)
            => optionCount > 0 && optionIndex == optionCount - 1;

        private static void MarkTargetSpent(ResolvedRevelationTarget target, HashSet<int> spentBackstory, HashSet<int> spentStake)
        {
            if (target.Registry == EmotionStemSelectionRules.BackstoryRegistry)
                spentBackstory.Add(target.Index);
            else if (target.Registry == EmotionStemSelectionRules.StakeRegistry)
                spentStake.Add(target.Index);
        }
    }
}
