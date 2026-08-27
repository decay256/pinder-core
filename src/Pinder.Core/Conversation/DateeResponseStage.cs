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
    internal struct DateeResponseStageResult
    {
        public DateeResponse DateeResponse { get; }
        public double ResponseDelayMinutes { get; }
        public string DateeMessage { get; }

        public DateeResponseStageResult(DateeResponse dateeResponse, double responseDelayMinutes, string dateeMessage)
        {
            DateeResponse = dateeResponse ?? throw new ArgumentNullException(nameof(dateeResponse));
            ResponseDelayMinutes = responseDelayMinutes;
            DateeMessage = dateeMessage ?? throw new ArgumentNullException(nameof(dateeMessage));
        }
    }

    internal class DateeResponseStage
    {
        private readonly ILlmAdapter _llm;
        private readonly Action<OperationalDiagnosticEvent>? _onDiagnostic;
        private readonly GameRunAgentJournalContext? _agentJournalContext;

        public DateeResponseStage(
            ILlmAdapter llm,
            Action<OperationalDiagnosticEvent>? onDiagnostic = null,
            GameRunAgentJournalContext? agentJournalContext = null)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _onDiagnostic = onDiagnostic;
            _agentJournalContext = agentJournalContext;
        }

        public async Task<DateeResponseStageResult> ExecuteAsync(
            GameSessionState state,
            RollStageResult rollStage,
            DeliveryStageResult deliveryStage,
            CharacterProfile player,
            CharacterProfile datee,
            System.IProgress<TurnProgressEvent>? progress,
            CancellationToken ct,
            InterestState? finalInterestAfterState = null,
            CharacterEmotionalStatus? playerEmotionalStatus = null,
            CharacterEmotionalStatus? dateeEmotionalStatus = null)
        {
            int finalInterestAfter = state.Interest.Current;
            InterestState resolvedFinalInterestAfterState =
                finalInterestAfterState ?? new InterestMeter(finalInterestAfter).GetState();
            playerEmotionalStatus ??= CharacterEmotionalStatusResolver.Resolve(player, 0, 0);
            dateeEmotionalStatus ??= CharacterEmotionalStatusResolver.Resolve(datee, 0, 0);

            // Compute response delay
            double responseDelayMinutes = datee.Timing.ComputeDelay(finalInterestAfter, rollStage.ResolveDice);

            // Generate datee response
            var dateeTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(state.Traps);

            Dictionary<ShadowStatType, int>? dateeShadowThresholds = null;
            if (state.DateeShadows != null)
            {
                dateeShadowThresholds = new Dictionary<ShadowStatType, int>();
                foreach (ShadowStatType shadow in Enum.GetValues(typeof(ShadowStatType)))
                {
                    dateeShadowThresholds[shadow] = state.DateeShadows.GetEffectiveShadow(shadow);
                }
            }

            string dateeArchetypeDirective = datee.ActiveArchetype?.Directive;

            var dateeContext = new DateeContext(
                dateePrompt: datee.AssembledSystemPrompt,
                conversationHistory: TurnOrchestratorHelpers.BuildHistoryForLlmContext(state),
                dateeLastMessage: GameSessionHelpers.GetLastDateeMessage(state.History, datee.DisplayName),
                activeTraps: GameSessionHelpers.GetActiveTrapNames(state.Traps),
                currentInterest: finalInterestAfter,
                playerDeliveredMessage: deliveryStage.DeliveredMessage,
                interestBefore: rollStage.InterestBefore,
                interestAfter: finalInterestAfter,
                responseDelayMinutes: responseDelayMinutes,
                activeTrapInstructions: dateeTrapInstructions,
                playerName: player.DisplayName,
                dateeName: datee.DisplayName,
                currentTurn: state.TurnNumber,
                shadowThresholds: dateeShadowThresholds,
                deliveryTier: rollStage.RollResult.Tier,
                activeArchetypeDirective: dateeArchetypeDirective,
                // #1123 strict bleed isolation: the datee session sees ONLY the
                // avatar's public dating-app card, never the avatar's full
                // private system prompt.
                playerAvatarCard: GameSessionHelpers.BuildPublicProfileCard(player),
                horninessOverlayApplied: deliveryStage.HorninessCheckResult.OverlayApplied,
                horninessTier: deliveryStage.HorninessCheckResult.Tier,
                resolvedTarget: null,
                cognitiveSubtext: null,
                interestBeforeState: rollStage.StateBefore,
                interestAfterState: resolvedFinalInterestAfterState,
                emotionalTurnEvent: new DateeEmotionalTurnEvent(
                    rollStage.RollResult.Stat,
                    RollOutcomeIntensityContract.FromRollResult(rollStage.RollResult),
                    datee.PsychiatricDiagnosis),
                agentJournalContext: _agentJournalContext,
                dateeTextingStyle: datee.TextingStyleFragment,
                playerHungerForIntimacy: playerEmotionalStatus.HungerForIntimacy,
                playerTerrorOfRejection: playerEmotionalStatus.TerrorOfRejection,
                dateeHungerForIntimacy: dateeEmotionalStatus.HungerForIntimacy,
                dateeTerrorOfRejection: dateeEmotionalStatus.TerrorOfRejection,
                previousAcceptedEmotionalDirections: state.DateeEmotionalDirectionHistory,
                dateeReactionTarget: state.CurrentDateeReactionTarget,
                cognitiveSubtextFact: state.CurrentDateeCognitiveSubtextFact,
                recipientCharacterId: datee.CharacterId,
                onDiagnostic: _onDiagnostic);

            progress?.Report(new TurnProgressEvent(TurnProgressStage.DateeResponseStarted));

            string callId = OperationalDiagnostics.CreateCallId();
            OperationalDiagnostics.Emit(
                _onDiagnostic,
                new OperationalDiagnosticEvent(
                    "DateeResponseStage",
                    "DateeResponseStarted",
                    OperationalDiagnosticSeverity.Info,
                    "Datee response operation started.",
                    operationKind: OperationalDiagnosticOperationKind.DateeResponse,
                    phaseCode: LlmPhase.OpponentResponse,
                    lifecycle: OperationalDiagnosticLifecycle.Start,
                    callId: callId,
                    correlationHints: new Dictionary<string, string>
                    {
                        ["turn"] = state.TurnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    }));

            DateeResponse dateeResponse;
            StatefulDateeResult? sessionResult = null;
            try
            {
                if (_llm is Pinder.Core.Interfaces.ISessionStatefulLlmAdapter sessionLlm
                    && sessionLlm.SupportsConversationSessions)
                {
                    sessionResult = await sessionLlm.GetDateeResponseAsync(
                        dateeContext,
                        new List<ConversationMessage>(state.DateeHistory),
                        new List<ConversationMessage>(state.AvatarHistory),
                        state.DateeSessionSnapshot,
                        state.AvatarSessionSnapshot,
                        ct).ConfigureAwait(false);
                    if (sessionResult == null)
                        throw new InvalidOperationException("LLM adapter returned null session datee result");
                    dateeResponse = sessionResult.Response;
                    if (dateeResponse == null)
                        throw new InvalidOperationException("LLM adapter returned null datee response");
                    if (sessionResult.DateeSessionSnapshot == null)
                        throw new InvalidOperationException("Session adapter omitted the DATEE session snapshot.");
                    if (sessionResult.AvatarSessionSnapshot == null)
                        throw new InvalidOperationException("Session adapter omitted the avatar session snapshot.");
                }
                else if (_llm is Pinder.Core.Interfaces.IStatefulLlmAdapter statefulLlm)
                {
                    var statefulResult = await statefulLlm.GetDateeResponseAsync(
                        dateeContext,
                        new List<ConversationMessage>(state.DateeHistory),
                        ct).ConfigureAwait(false);
                    if (statefulResult == null)
                        throw new InvalidOperationException("LLM adapter returned null stateful datee result");
                    dateeResponse = statefulResult.Response;
                    if (dateeResponse == null)
                        throw new InvalidOperationException("LLM adapter returned null datee response");
                }
                else
                {
                    dateeResponse = await _llm.GetDateeResponseAsync(dateeContext, ct).ConfigureAwait(false);
                    if (dateeResponse == null)
                        throw new InvalidOperationException("LLM adapter returned null datee response");
                }

                OperationalDiagnostics.EmitSucceededTerminal(
                    _onDiagnostic,
                    "DateeResponseStage",
                    "DateeResponseSucceeded",
                    "Datee response operation succeeded.",
                    OperationalDiagnosticOperationKind.DateeResponse,
                    LlmPhase.OpponentResponse,
                    callId,
                    state.TurnNumber);
            }
            catch (OperationCanceledException ex)
            {
                OperationalDiagnostics.EmitCancelledTerminal(
                    _onDiagnostic,
                    "DateeResponseStage",
                    "DateeResponseCancelled",
                    "Datee response operation was cancelled.",
                    ex,
                    OperationalDiagnosticOperationKind.DateeResponse,
                    LlmPhase.OpponentResponse,
                    callId,
                    state.TurnNumber);
                throw;
            }
            catch (Exception ex)
            {
                OperationalDiagnostics.EmitFailedTerminal(
                    _onDiagnostic,
                    "DateeResponseStage",
                    "DateeResponseFailed",
                    "Datee response operation failed.",
                    ex,
                    OperationalDiagnosticOperationKind.DateeResponse,
                    LlmPhase.OpponentResponse,
                    callId,
                    state.TurnNumber);
                throw;
            }

            state.DateeHistory.Add(ConversationMessage.User(deliveryStage.DeliveredMessage));
            state.DateeHistory.Add(ConversationMessage.Assistant(dateeResponse.MessageText));
            CharacterEmotionalDirection? acceptedDirection = dateeResponse.EmotionalReactionDebug?.Direction;
            if (acceptedDirection != null)
            {
                state.RecordAcceptedDateeEmotionalDirection(
                    CharacterEmotionalDirectionSummary.FromDirection(state.TurnNumber, acceptedDirection));
            }
            if (sessionResult != null)
            {
                state.AvatarHistory.Add(ConversationMessage.Assistant(deliveryStage.DeliveredMessage));
                state.AvatarHistory.Add(ConversationMessage.User(dateeResponse.MessageText));
                state.DateeSessionSnapshot = sessionResult.DateeSessionSnapshot;
                state.AvatarSessionSnapshot = sessionResult.AvatarSessionSnapshot;
            }

            string dateeMessage = dateeResponse.MessageText;
            progress?.Report(new TurnProgressEvent(TurnProgressStage.DateeResponseCompleted, dateeMessage));

            if (state.CurrentDateeReactionTarget != null)
            {
                var target = state.CurrentDateeReactionTarget.ResolvedTarget;
                if (target.Registry == EmotionStemSelectionRules.BackstoryRegistry)
                    state.DateeSpentBackstoryIndices.Add(target.Index);
                else if (target.Registry == EmotionStemSelectionRules.StakeRegistry)
                    state.DateeSpentStakeIndices.Add(target.Index);
            }

            return new DateeResponseStageResult(dateeResponse, responseDelayMinutes, dateeMessage);
        }
    }
}
