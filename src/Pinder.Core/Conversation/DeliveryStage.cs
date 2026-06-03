using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.Text;

namespace Pinder.Core.Conversation
{
    internal struct DeliveryStageResult
    {
        public string DeliveredMessage { get; set; }
        public List<TextDiff> TextDiffs { get; set; }
        public SteeringRollResult SteeringResult { get; set; }
        public ShadowCheckResult ShadowCheckResult { get; set; }
        public HorninessCheckResult HorninessCheckResult { get; set; }
        public int HorninessInterestPenalty { get; set; }
        public int HorninessInterestBefore { get; set; }
        public int FinalInterestDelta { get; set; }
        public int ShadowCorrection { get; set; }
    }

    internal class DeliveryStage
    {
        private readonly ILlmAdapter _llm;
        private readonly IRuleResolver? _rules;
        private readonly SteeringEngine _steeringEngine;
        private readonly HorninessEngine _horninessEngine;
        private readonly ShadowCheckEngine _shadowCheckEngine;
        private readonly object? _statDeliveryInstructions;
        private readonly Action<TextLayerNoopEvent>? _onTextLayerNoop;

        public DeliveryStage(
            ILlmAdapter llm,
            IRuleResolver? rules,
            SteeringEngine steeringEngine,
            HorninessEngine horninessEngine,
            ShadowCheckEngine shadowCheckEngine,
            object? statDeliveryInstructions,
            Action<TextLayerNoopEvent>? onTextLayerNoop)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _rules = rules;
            _steeringEngine = steeringEngine ?? throw new ArgumentNullException(nameof(steeringEngine));
            _horninessEngine = horninessEngine ?? throw new ArgumentNullException(nameof(horninessEngine));
            _shadowCheckEngine = shadowCheckEngine ?? throw new ArgumentNullException(nameof(shadowCheckEngine));
            _statDeliveryInstructions = statDeliveryInstructions;
            _onTextLayerNoop = onTextLayerNoop;
        }

        public async Task<DeliveryStageResult> ExecuteAsync(
            GameSessionState state,
            DialogueOption chosenOption,
            RollResult rollResult,
            CharacterProfile player,
            CharacterProfile opponent,
            System.IProgress<TurnProgressEvent>? progress,
            int interestDelta,
            CancellationToken ct)
        {
            var textDiffs = new List<TextDiff>();

            // 6. Deliver message via LLM
            var deliveryTrapNames = GameSessionHelpers.GetActiveTrapNames(state.Traps);
            var deliveryTrapInstructions = GameSessionHelpers.GetActiveTrapInstructions(state.Traps);

            int beatDcBy = rollResult.IsSuccess ? rollResult.FinalTotal - rollResult.DC : 0;

            // Resolve stat-specific failure instruction when the roll failed (#695)
            string? statFailureInstruction = null;
            if (!rollResult.IsSuccess && _statDeliveryInstructions != null)
            {
                statFailureInstruction = HorninessEngine.GetStatFailureInstruction(
                    _statDeliveryInstructions, chosenOption.Stat, rollResult.Tier);
            }

            // 10b. Steering roll
            string originalIntendedText = chosenOption.IntendedText ?? "";
            progress?.Report(new TurnProgressEvent(TurnProgressStage.SteeringStarted));
            SteeringRollResult steeringResult = await _steeringEngine.AttemptSteeringRollAsync(
                originalIntendedText, player, opponent, _llm, TurnOrchestratorHelpers.BuildHistoryForLlmContext(state), ct).ConfigureAwait(false);
            progress?.Report(new TurnProgressEvent(
                TurnProgressStage.SteeringCompleted,
                steeringResult.SteeringSucceeded ? steeringResult.SteeringQuestion : null));

            string intendedTextForDelivery = originalIntendedText;
            DialogueOption deliveryOption = chosenOption;
            if (steeringResult.SteeringSucceeded && steeringResult.SteeringQuestion != null)
            {
                intendedTextForDelivery = originalIntendedText.Length == 0
                    ? steeringResult.SteeringQuestion
                    : originalIntendedText.TrimEnd() + " " + steeringResult.SteeringQuestion;

                if (intendedTextForDelivery != originalIntendedText
                    && !string.IsNullOrEmpty(originalIntendedText)
                    && originalIntendedText != "...")
                {
                    var steeringSpans = WordDiff.Compute(originalIntendedText, intendedTextForDelivery);
                    textDiffs.Add(new TextDiff("Steering", steeringSpans, originalIntendedText, intendedTextForDelivery));
                }

                deliveryOption = new DialogueOption(
                    chosenOption.Stat,
                    intendedTextForDelivery,
                    chosenOption.CallbackTurnNumber,
                    chosenOption.ComboName,
                    chosenOption.HasTellBonus,
                    chosenOption.HasWeaknessWindow,
                    chosenOption.IsUnhingedReplacement);
            }

            string playerArchetypeDirectiveForDelivery = player.ActiveArchetype?.Directive;

            var deliveryContext = new DeliveryContext(
                playerPrompt: player.AssembledSystemPrompt,
                opponentPrompt: opponent.AssembledSystemPrompt,
                conversationHistory: TurnOrchestratorHelpers.BuildHistoryForLlmContext(state),
                opponentLastMessage: GameSessionHelpers.GetLastOpponentMessage(state.History, opponent.DisplayName),
                chosenOption: deliveryOption,
                outcome: rollResult.Tier,
                beatDcBy: beatDcBy,
                activeTraps: deliveryTrapNames,
                activeTrapInstructions: deliveryTrapInstructions,
                playerName: player.DisplayName,
                opponentName: opponent.DisplayName,
                currentTurn: state.TurnNumber,
                shadowThresholds: state.CurrentShadowThresholds,
                isNat20: rollResult.IsNatTwenty,
                statFailureInstruction: statFailureInstruction,
                activeArchetypeDirective: playerArchetypeDirectiveForDelivery);

            progress?.Report(new TurnProgressEvent(TurnProgressStage.DeliveryStarted));
            string deliveredMessage = await _llm.DeliverMessageAsync(deliveryContext, ct).ConfigureAwait(false);
            progress?.Report(new TurnProgressEvent(TurnProgressStage.DeliveryCompleted, deliveredMessage));

            // #902: Meta-prefix strip immediately after delivery LLM call.
            deliveredMessage = TextSanitizer.Sanitize(deliveredMessage, MetaPrefixStripper.LayerName, textDiffs);

            // Tier modifier diff
            if (deliveredMessage != intendedTextForDelivery
                && !string.IsNullOrEmpty(intendedTextForDelivery)
                && intendedTextForDelivery != "...")
            {
                string layerLabel = rollResult.IsNatTwenty ? "Nat 20" :
                                    rollResult.IsNatOne    ? "Nat 1"  :
                                    rollResult.Tier == Rolls.FailureTier.Success ? "Strong success" :
                                    rollResult.Tier.ToString();
                var tierSpans = WordDiff.Compute(intendedTextForDelivery, deliveredMessage);
                textDiffs.Add(new TextDiff(layerLabel, tierSpans, intendedTextForDelivery, deliveredMessage));
            }

            // ---- Speculative Overlay Dispatcher ----
            bool runTrap = state.Traps.HasActive && rollResult.ActivatedTrap == null;
            string trapInstruction = "";
            string trapDisplayName = "";
            string opponentCtxForTrap = "";

            if (runTrap)
            {
                var activeTrap = state.Traps.Active!;
                trapInstruction = activeTrap.Definition.LlmInstruction;
                trapDisplayName = activeTrap.Definition.DisplayName;
                opponentCtxForTrap = TurnOrchestratorHelpers.BuildOpponentContext(opponent);

                if (string.IsNullOrWhiteSpace(trapInstruction)
                    || string.IsNullOrEmpty(deliveredMessage)
                    || deliveredMessage == "...")
                {
                    runTrap = false;
                }
            }

            // #755: Shadow check
            ShadowStatType? pairedShadow = TurnOrchestratorHelpers.GetPairedShadow(chosenOption.Stat);
            ShadowCheckResult shadowCheckResult = ShadowCheckResult.NotPerformed;

            bool runShadow = false;
            string corruptionInstruction = "";
            int shadowRoll = 0;
            int shadowDC = 0;
            bool shadowMiss = false;
            FailureTier shadowTier = FailureTier.Success;
            RollCheckResult? rawShadowCheck = null;

            if (pairedShadow.HasValue && state.PlayerShadows != null)
            {
                int shadowValue = state.PlayerShadows.GetEffectiveShadow(pairedShadow.Value);
                if (shadowValue > 0)
                {
                    var rawShadowResult = _shadowCheckEngine.Check(pairedShadow.Value, shadowValue);
                    shadowRoll = rawShadowResult.Roll;
                    shadowDC   = rawShadowResult.DC;
                    shadowMiss = rawShadowResult.IsMiss;
                    rawShadowCheck = rawShadowResult.Check;

                    if (shadowMiss)
                    {
                        shadowTier = rawShadowResult.Tier;
                        string? instruction = HorninessEngine.GetShadowCorruptionInstruction(
                            _statDeliveryInstructions, pairedShadow.Value, shadowTier);

                        if (instruction != null)
                        {
                            runShadow = true;
                            corruptionInstruction = instruction;
                        }
                    }
                }
            }

            // Dispatch speculative LLM calls in parallel
            var dispatchResult = await LlmDispatcher.DispatchSpeculativeCallsAsync(
                _llm,
                deliveredMessage,
                runTrap,
                trapInstruction,
                trapDisplayName,
                opponentCtxForTrap,
                runShadow,
                corruptionInstruction,
                pairedShadow ?? ShadowStatType.Dread,
                playerArchetypeDirectiveForDelivery,
                textDiffs,
                _onTextLayerNoop,
                state.TurnNumber,
                progress,
                ct,
                state.SpeculativeWasteTracker).ConfigureAwait(false);

            deliveredMessage = dispatchResult.FinalMessage;
            bool shadowOverlayApplied = dispatchResult.ShadowOverlayApplied;

            int shadowCorrection = 0;
            if (pairedShadow.HasValue && state.PlayerShadows != null)
            {
                int shadowValue = state.PlayerShadows.GetEffectiveShadow(pairedShadow.Value);
                if (shadowValue > 0)
                {
                    if (shadowMiss)
                    {
                        if (shadowOverlayApplied && rollResult.IsSuccess)
                        {
                            // #1095: A shadow trap (success roll + paired-shadow MISS with overlay)
                            // no longer demotes the turn to a forced FAILURE. Instead it TRUNCATES
                            // the positive interest delta to a maximum of 1 ("tainted, capped").
                            // The roll verdict stays SUCCESS (no ApplyFinalOverride to Miss in
                            // TurnOrchestrator), momentum keeps incrementing, and success-gated
                            // downstream effects stay on the success path. The DATEE reacts
                            // neutrally / slightly negatively, not with a hard-rejection failure beat.
                            // shadowCorrection stays the signed adjustment so the central
                            // state.Interest.Apply(ShadowCorrection) remains correct.
                            int truncated = Math.Min(interestDelta, 1);
                            shadowCorrection = truncated - interestDelta;
                            interestDelta = truncated;
                        }

                        shadowCheckResult = new ShadowCheckResult(
                            true, pairedShadow.Value, shadowRoll, shadowDC, true, shadowTier, shadowOverlayApplied,
                            rawShadowCheck);
                    }
                    else
                    {
                        shadowCheckResult = new ShadowCheckResult(
                            true, pairedShadow.Value, shadowRoll, shadowDC, false, FailureTier.Success, false,
                            rawShadowCheck);
                    }
                }
            }

            string? horninessOverlayInstruction;
            HorninessCheckResult horninessCheckResult;
            (horninessCheckResult, horninessOverlayInstruction) = _horninessEngine.PeekAsync(
                state.SessionHorniness,
                state.PlayerShadows,
                _statDeliveryInstructions,
                ct);

            int horninessInterestPenalty = 0;
            int horninessInterestBefore = 0;

            // #899: Horniness TEXT OVERLAY
            if (horninessOverlayInstruction != null)
            {
                string beforeHorniness = deliveredMessage;
                string opponentCtx = TurnOrchestratorHelpers.BuildOpponentContext(opponent);
                progress?.Report(new TurnProgressEvent(TurnProgressStage.HorninessOverlayStarted));
                string rawHorninessOutput = await _llm.ApplyHorninessOverlayAsync(deliveredMessage, horninessOverlayInstruction, opponentCtx, playerArchetypeDirectiveForDelivery, ct).ConfigureAwait(false);
                progress?.Report(new TurnProgressEvent(TurnProgressStage.HorninessOverlayCompleted, rawHorninessOutput));

                string sanitizedHorninessOutput = MetaPrefixStripper.Strip(rawHorninessOutput);
                if (sanitizedHorninessOutput != rawHorninessOutput)
                {
                    var stripSpans = WordDiff.Compute(rawHorninessOutput, sanitizedHorninessOutput);
                    textDiffs.Add(new TextDiff(
                        MetaPrefixStripper.LayerName, stripSpans,
                        rawHorninessOutput, sanitizedHorninessOutput));
                }

                deliveredMessage = sanitizedHorninessOutput;

                if (deliveredMessage != beforeHorniness)
                {
                    var horninessSpans = WordDiff.Compute(beforeHorniness, deliveredMessage);
                    textDiffs.Add(new TextDiff("Horniness", horninessSpans, beforeHorniness, deliveredMessage));
                }
                else
                {
                    TurnOrchestratorHelpers.EmitTextLayerNoop(_onTextLayerNoop, state.TurnNumber, "Horniness", beforeHorniness, deliveredMessage);
                }
            }

            if (horninessCheckResult.OverlayApplied && interestDelta > 0)
            {
                horninessInterestBefore = state.Interest.Current + shadowCorrection;
                int halvedDelta = (int)Math.Floor(interestDelta / 2.0);
                int penalty = halvedDelta - interestDelta;
                horninessInterestPenalty = penalty;
                interestDelta += penalty;
            }

            // Issue #339: same-turn callback-phrase strip
            deliveredMessage = TextSanitizer.Sanitize(deliveredMessage, CallbackStripper.LayerName, textDiffs);

            // #1041 (Tier C): markdown-stripping pass for surfaces that expect plain prose.
            deliveredMessage = TextSanitizer.Sanitize(deliveredMessage, MarkdownSanitizer.LayerName, textDiffs);

            return new DeliveryStageResult
            {
                DeliveredMessage = deliveredMessage,
                TextDiffs = textDiffs,
                SteeringResult = steeringResult,
                ShadowCheckResult = shadowCheckResult,
                HorninessCheckResult = horninessCheckResult,
                HorninessInterestPenalty = horninessInterestPenalty,
                HorninessInterestBefore = horninessInterestBefore,
                FinalInterestDelta = interestDelta,
                ShadowCorrection = shadowCorrection
            };
        }
    }
}
