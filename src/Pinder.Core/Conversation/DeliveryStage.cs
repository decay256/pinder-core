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
        private readonly int _maxDeliveryWords;

        public DeliveryStage(
            ILlmAdapter llm,
            IRuleResolver? rules,
            SteeringEngine steeringEngine,
            HorninessEngine horninessEngine,
            ShadowCheckEngine shadowCheckEngine,
            object? statDeliveryInstructions,
            Action<TextLayerNoopEvent>? onTextLayerNoop,
            int maxDeliveryWords)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _rules = rules;
            _steeringEngine = steeringEngine ?? throw new ArgumentNullException(nameof(steeringEngine));
            _horninessEngine = horninessEngine ?? throw new ArgumentNullException(nameof(horninessEngine));
            _shadowCheckEngine = shadowCheckEngine ?? throw new ArgumentNullException(nameof(shadowCheckEngine));
            _statDeliveryInstructions = statDeliveryInstructions;
            _onTextLayerNoop = onTextLayerNoop;
            _maxDeliveryWords = maxDeliveryWords;
        }

        public async Task<DeliveryStageResult> ExecuteAsync(
            GameSessionState state,
            DialogueOption chosenOption,
            RollResult rollResult,
            CharacterProfile player,
            CharacterProfile datee,
            System.IProgress<TurnProgressEvent>? progress,
            int interestDelta,
            CancellationToken ct)
        {
            var textDiffs = new List<TextDiff>();

            // 10b. Steering roll
            string originalIntendedText = chosenOption.IntendedText ?? "";
            progress?.Report(new TurnProgressEvent(TurnProgressStage.SteeringStarted));
            SteeringRollResult steeringResult = await _steeringEngine.AttemptSteeringRollAsync(
                originalIntendedText, player, datee, _llm, TurnOrchestratorHelpers.BuildHistoryForLlmContext(state), ct).ConfigureAwait(false);
            progress?.Report(new TurnProgressEvent(
                TurnProgressStage.SteeringCompleted,
                steeringResult.SteeringSucceeded ? steeringResult.SteeringQuestion : null));

            // #1125 — the picked option now carries the FULL sendable line
            // (avatar GM emits final candidate lines; no second "delivery" LLM
            // call expands a gist). Steering, when it fires, appends its
            // question to that full line; this stays the pre-overlay "picked
            // line".
            string pickedLine = originalIntendedText;
            if (steeringResult.SteeringSucceeded && steeringResult.SteeringQuestion != null)
            {
                pickedLine = originalIntendedText.Length == 0
                    ? steeringResult.SteeringQuestion
                    : originalIntendedText.TrimEnd() + " " + steeringResult.SteeringQuestion;

                if (pickedLine != originalIntendedText
                    && !string.IsNullOrEmpty(originalIntendedText)
                    && originalIntendedText != "...")
                {
                    var steeringSpans = WordDiff.Compute(originalIntendedText, pickedLine);
                    textDiffs.Add(new TextDiff("Steering", steeringSpans, originalIntendedText, pickedLine));
                }
            }

            string playerArchetypeDirectiveForDelivery = player.ActiveArchetype?.Directive;
            // #1125: the stat-specific failure instruction and trap/beatDcBy
            // inputs that previously fed the creative delivery PROMPT are gone —
            // there is no delivery LLM call. Failure flavour now reaches the
            // datee via DateeContext.DeliveryTier (set in DateeResponseStage),
            // and the actual text degradation is deterministic (DeliveryOverlay)
            // below. Trap/shadow taint still fire as their own overlays further
            // down (LlmDispatcher), unchanged.

            // #1125 — "DELIVERY" is now a NON-LLM commit/overlay step. Instead of
            // a creative LLM call that expanded a gist and degraded it per the
            // roll, the picked full line is committed verbatim on success and
            // degraded deterministically on a failure tier (parity with the old
            // delivery-LLM degradation, but pure and reproducible). No
            // `delivery`/`DeliverMessageAsync` LLM call fires in the turn, and
            // the avatar session history is NOT written here — option-generation
            // and this commit overlay are ephemeral; only the committed line is
            // persisted (by TurnOrchestrator), preserving the clean-history rule.
            progress?.Report(new TurnProgressEvent(TurnProgressStage.DeliveryStarted));
            string deliveredMessage = DeliveryOverlay.Apply(pickedLine, rollResult.Tier, rollResult.MissMargin);
            progress?.Report(new TurnProgressEvent(TurnProgressStage.DeliveryCompleted, deliveredMessage));

            // #902 / SANITIZATION-INVARIANTS-MUST-RUN-AFTER-EACH-STAGE: meta-prefix
            // strip still runs after the (now deterministic) delivery stage, so a
            // labelling artifact carried in the picked option line is stripped
            // before downstream overlays. Emits a TextDiff layer when it fires.
            deliveredMessage = TextSanitizer.Sanitize(deliveredMessage, MetaPrefixStripper.LayerName, textDiffs);

            // Tier modifier diff: the deterministic degrade vs the picked line.
            if (deliveredMessage != pickedLine
                && !string.IsNullOrEmpty(pickedLine)
                && pickedLine != "...")
            {
                string layerLabel = rollResult.IsNatTwenty ? "Nat 20" :
                                    rollResult.IsNatOne    ? "Nat 1"  :
                                    rollResult.Tier == Rolls.FailureTier.Success ? "Strong success" :
                                    rollResult.Tier.ToString();
                var tierSpans = WordDiff.Compute(pickedLine, deliveredMessage);
                textDiffs.Add(new TextDiff(layerLabel, tierSpans, pickedLine, deliveredMessage));
            }

            // ---- Speculative Overlay Dispatcher ----
            bool runTrap = state.Traps.HasActive && rollResult.ActivatedTrap == null;
            string trapInstruction = "";
            string trapDisplayName = "";
            string dateeCtxForTrap = "";

            if (runTrap)
            {
                var activeTrap = state.Traps.Active!;
                trapInstruction = activeTrap.Definition.LlmInstruction;
                trapDisplayName = activeTrap.Definition.DisplayName;
                dateeCtxForTrap = TurnOrchestratorHelpers.BuildDateeContext(datee);

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
                dateeCtxForTrap,
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
                string dateeCtx = TurnOrchestratorHelpers.BuildDateeContext(datee);

                // AC-B2: Add remaining budget hint to Horniness overlay instruction if tight.
                int currentWords = deliveredMessage.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                int remainingBudget = Math.Max(0, _maxDeliveryWords - currentWords);
                string finalInstruction = horninessOverlayInstruction;
                if (remainingBudget < 25)
                {
                    finalInstruction += $"\nLength constraint: Keep it extremely brief (max {remainingBudget} words). Do not append long sentences.";
                }

                progress?.Report(new TurnProgressEvent(TurnProgressStage.HorninessOverlayStarted));
                string rawHorninessOutput = await _llm.ApplyHorninessOverlayAsync(deliveredMessage, finalInstruction, dateeCtx, playerArchetypeDirectiveForDelivery, ct).ConfigureAwait(false);
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
