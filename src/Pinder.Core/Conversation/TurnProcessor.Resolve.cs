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
    internal static partial class TurnProcessor
    {
        internal static async Task<TurnResult> ResolveTurnAsync(
            GameSessionState state,
            int optionIndex,
            CharacterProfile player,
            CharacterProfile opponent,
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry trapRegistry,
            IRuleResolver? rules,
            IConsequenceCatalog? consequenceCatalog,
            ShadowGrowthEvaluator? shadowGrowthEvaluator,
            SessionXpRecorder xpRecorder,
            SteeringEngine steeringEngine,
            HorninessEngine horninessEngine,
            ShadowCheckEngine shadowCheckEngine,
            System.IProgress<TurnProgressEvent>? progress,
            CancellationToken ct)
        {
            return await ResolveTurnAsync(
                state,
                optionIndex,
                player,
                opponent,
                llm,
                dice,
                trapRegistry,
                rules,
                consequenceCatalog,
                shadowGrowthEvaluator,
                xpRecorder,
                steeringEngine,
                horninessEngine,
                shadowCheckEngine,
                progress,
                statDeliveryInstructions: null,
                onTextLayerNoop: null,
                globalDcBias: 0,
                ct).ConfigureAwait(false);
        }

        internal static async Task<TurnResult> ResolveTurnAsync(
            GameSessionState state,
            int optionIndex,
            CharacterProfile player,
            CharacterProfile opponent,
            ILlmAdapter llm,
            IDiceRoller dice,
            ITrapRegistry trapRegistry,
            IRuleResolver? rules,
            IConsequenceCatalog? consequenceCatalog,
            ShadowGrowthEvaluator? shadowGrowthEvaluator,
            SessionXpRecorder xpRecorder,
            SteeringEngine steeringEngine,
            HorninessEngine horninessEngine,
            ShadowCheckEngine shadowCheckEngine,
            System.IProgress<TurnProgressEvent>? progress,
            object? statDeliveryInstructions,
            Action<TextLayerNoopEvent>? onTextLayerNoop,
            int globalDcBias,
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
            var rollStage = ExecuteRollStage(
                state,
                optionIndex,
                player,
                opponent,
                dice,
                trapRegistry,
                rules,
                shadowGrowthEvaluator,
                xpRecorder,
                globalDcBias);

            // Execute Delivery/Overlay Stage
            var deliveryStage = await ExecuteDeliveryStageAsync(
                state,
                rollStage.ChosenOption,
                rollStage.RollResult,
                player,
                opponent,
                llm,
                rules,
                steeringEngine,
                horninessEngine,
                shadowCheckEngine,
                statDeliveryInstructions,
                onTextLayerNoop,
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

            // 10. Compute response delay
            double responseDelayMinutes = opponent.Timing.ComputeDelay(state.Interest.Current, rollStage.ResolveDice);

            // Opponent Response and Turn Assembly
            state.History.Add((player.DisplayName, deliveryStage.DeliveredMessage));

            // Generate opponent response
            var opponentTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(state.Traps);

            Dictionary<ShadowStatType, int>? opponentShadowThresholds = null;
            if (state.OpponentShadows != null)
            {
                opponentShadowThresholds = new Dictionary<ShadowStatType, int>();
                foreach (ShadowStatType shadow in Enum.GetValues(typeof(ShadowStatType)))
                {
                    opponentShadowThresholds[shadow] = state.OpponentShadows.GetEffectiveShadow(shadow);
                }
            }

            string opponentArchetypeDirective = opponent.ActiveArchetype?.Directive;

            var opponentContext = new OpponentContext(
                playerPrompt: player.AssembledSystemPrompt,
                opponentPrompt: opponent.AssembledSystemPrompt,
                conversationHistory: BuildHistoryForLlmContext(state),
                opponentLastMessage: GameSessionHelpers.GetLastOpponentMessage(state.History, opponent.DisplayName),
                activeTraps: GameSessionHelpers.GetActiveTrapNames(state.Traps),
                currentInterest: state.Interest.Current,
                playerDeliveredMessage: deliveryStage.DeliveredMessage,
                interestBefore: rollStage.InterestBefore,
                interestAfter: rollStage.InterestAfter,
                responseDelayMinutes: responseDelayMinutes,
                activeTrapInstructions: opponentTrapInstructions,
                playerName: player.DisplayName,
                opponentName: opponent.DisplayName,
                currentTurn: state.TurnNumber,
                shadowThresholds: opponentShadowThresholds,
                deliveryTier: rollStage.RollResult.Tier,
                activeArchetypeDirective: opponentArchetypeDirective);

            progress?.Report(new TurnProgressEvent(TurnProgressStage.OpponentResponseStarted));

            OpponentResponse opponentResponse;
            if (llm is Pinder.Core.Interfaces.IStatefulLlmAdapter statefulLlm)
            {
                var statefulResult = await statefulLlm.GetOpponentResponseAsync(
                    opponentContext,
                    state.OpponentHistory,
                    ct).ConfigureAwait(false);
                if (statefulResult == null)
                    throw new InvalidOperationException("LLM adapter returned null stateful opponent result");
                opponentResponse = statefulResult.Response;
                if (opponentResponse == null)
                    throw new InvalidOperationException("LLM adapter returned null opponent response");
                if (statefulResult.NewHistoryEntries != null)
                {
                    foreach (var entry in statefulResult.NewHistoryEntries)
                    {
                        if (entry != null)
                            state.OpponentHistory.Add(entry);
                    }
                }
            }
            else
            {
                opponentResponse = await llm.GetOpponentResponseAsync(opponentContext, ct).ConfigureAwait(false);
                if (opponentResponse == null)
                    throw new InvalidOperationException("LLM adapter returned null opponent response");
            }

            string opponentMessage = opponentResponse.MessageText;
            progress?.Report(new TurnProgressEvent(TurnProgressStage.OpponentResponseCompleted, opponentMessage));

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

            var stateSnapshot = CreateSnapshot(state, rules);

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
