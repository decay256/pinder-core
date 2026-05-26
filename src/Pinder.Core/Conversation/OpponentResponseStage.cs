using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;

namespace Pinder.Core.Conversation
{
    internal struct OpponentResponseStageResult
    {
        public OpponentResponse OpponentResponse { get; }
        public double ResponseDelayMinutes { get; }
        public string OpponentMessage { get; }

        public OpponentResponseStageResult(OpponentResponse opponentResponse, double responseDelayMinutes, string opponentMessage)
        {
            OpponentResponse = opponentResponse ?? throw new ArgumentNullException(nameof(opponentResponse));
            ResponseDelayMinutes = responseDelayMinutes;
            OpponentMessage = opponentMessage ?? throw new ArgumentNullException(nameof(opponentMessage));
        }
    }

    internal class OpponentResponseStage
    {
        private readonly ILlmAdapter _llm;

        public OpponentResponseStage(ILlmAdapter llm)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        }

        public async Task<OpponentResponseStageResult> ExecuteAsync(
            GameSessionState state,
            RollStageResult rollStage,
            DeliveryStageResult deliveryStage,
            CharacterProfile player,
            CharacterProfile opponent,
            System.IProgress<TurnProgressEvent>? progress,
            CancellationToken ct)
        {
            // Compute response delay
            double responseDelayMinutes = opponent.Timing.ComputeDelay(state.Interest.Current, rollStage.ResolveDice);

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
                conversationHistory: TurnOrchestrator.BuildHistoryForLlmContext(state),
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
            if (_llm is Pinder.Core.Interfaces.IStatefulLlmAdapter statefulLlm)
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
                opponentResponse = await _llm.GetOpponentResponseAsync(opponentContext, ct).ConfigureAwait(false);
                if (opponentResponse == null)
                    throw new InvalidOperationException("LLM adapter returned null opponent response");
            }

            string opponentMessage = opponentResponse.MessageText;
            progress?.Report(new TurnProgressEvent(TurnProgressStage.OpponentResponseCompleted, opponentMessage));

            return new OpponentResponseStageResult(opponentResponse, responseDelayMinutes, opponentMessage);
        }
    }
}
