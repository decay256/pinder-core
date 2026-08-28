using System;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;

namespace Pinder.Core.Conversation
{
    internal partial class TurnOrchestrator
    {
        /// <summary>
        /// Replays only the accepted DATEE performance against an explicitly
        /// rewound response snapshot. Resolution state is already post-turn and
        /// is not applied again.
        /// </summary>
        internal async Task<DateeResponseReplayResult> ReplayDateeResponseAsync(
            GameSessionState state,
            CharacterProfile player,
            CharacterProfile datee,
            IProgress<TurnProgressEvent>? progress,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            DateeResponseReplayState replay = state.LastDateeResponseReplayState
                ?? throw new InvalidOperationException("datee_response_replay.execution_state.required");
            AcceptedDateeResponsePlanState acceptedPlan = state.LastAcceptedDateeResponsePlanState
                ?? throw new InvalidOperationException("datee_response_replay.accepted_plan.required");
            replay.ValidateAgainst(acceptedPlan);
            if (state.TurnNumber != replay.PostTurnNumber)
                throw new InvalidOperationException("datee_response_replay.post_turn.identity.mismatch");
            if (state.PendingDateeResponsePlanReplay == null)
                throw new InvalidOperationException("datee_response_replay.selection.required");

            var rollStage = new RollStageResult
            {
                InterestBefore = replay.InterestBefore,
                StateBefore = replay.InterestBeforeState,
            };
            var deliveryStage = new DeliveryStageResult
            {
                DeliveredMessage = replay.DeliveredMessage,
                HorninessCheckResult = HorninessCheckResult.NotPerformed,
            };
            CharacterEmotionalStatus playerStatus = CharacterEmotionalStatusResolver.Resolve(
                player, _hungerForIntimacy, _terrorOfRejection);
            CharacterEmotionalStatus dateeStatus = CharacterEmotionalStatusResolver.Resolve(
                datee, _hungerForIntimacy, _terrorOfRejection);

            DateeResponseStageResult response = await _dateeResponseStage.ExecuteAsync(
                    state,
                    rollStage,
                    deliveryStage,
                    player,
                    datee,
                    progress,
                    ct,
                    replay.InterestAfterState,
                    playerStatus,
                    dateeStatus,
                    replay)
                .ConfigureAwait(false);

            state.ActiveWeakness = response.DateeResponse.WeaknessWindow != null
                ? new WeaknessWindow(
                    response.DateeResponse.WeaknessWindow.DefendingStat,
                    response.DateeResponse.WeaknessWindow.DcReduction * 2)
                : null;
            state.ActiveTell = response.DateeResponse.DetectedTell;
            state.History.Add((player.DisplayName, replay.DeliveredMessage));
            state.History.Add((datee.DisplayName, response.DateeMessage));
            state.LastDateeResponseReplayState = replay.WithAcceptedDateeMessage(response.DateeMessage);

            return new DateeResponseReplayResult(
                response.DateeMessage,
                TurnOrchestratorHelpers.CreateSnapshot(state, _rules));
        }
    }
}
