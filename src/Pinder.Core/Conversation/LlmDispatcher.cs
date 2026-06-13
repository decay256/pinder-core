using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinder.Core.Interfaces;
using Pinder.Core.Stats;
using Pinder.Core.Text;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Decouples and orchestrates parallel speculative LLM overlay/corruption calls.
    /// </summary>
    public static class LlmDispatcher
    {
        /// <summary>
        /// Dispatches speculative Trap and Shadow overlay calls, handles strip/noop
        /// logic, and returns the final message and whether shadow overlay was applied.
        /// </summary>
        /// <param name="wasteTracker">
        /// #1041 (Tier C): Optional <see cref="SpeculativeWasteTracker"/> that
        /// monitors shadow-call waste (when trap fires and shadow result is discarded)
        /// and adapts dispatch mode to avoid repeated wasted token consumption.
        /// Pass <c>null</c> to always use parallel speculative dispatch (legacy behaviour).
        /// </param>
        public static async Task<(string FinalMessage, bool ShadowOverlayApplied)> DispatchSpeculativeCallsAsync(
            ILlmAdapter llm,
            string deliveredMessage,
            // Trap parameters
            bool runTrap,
            string trapInstruction,
            string trapDisplayName,
            string dateeCtxForTrap,
            // Shadow parameters
            bool runShadow,
            string corruptionInstruction,
            ShadowStatType shadowType,
            // Shared parameters
            string? playerArchetypeDirectiveForDelivery,
            List<TextDiff> textDiffs,
            Action<TextLayerNoopEvent>? onTextLayerNoop,
            int turnNumber,
            IProgress<TurnProgressEvent>? progress,
            CancellationToken ct,
            SpeculativeWasteTracker? wasteTracker = null)
        {
            ct.ThrowIfCancellationRequested();

            if (!runTrap && !runShadow)
            {
                return (deliveredMessage, false);
            }

            Task<string>? trapTask = null;
            Task<string>? shadowTask = null;

            if (runTrap)
            {
                trapTask = Task.Run(async () =>
                {
                    progress?.Report(new TurnProgressEvent(TurnProgressStage.TrapOverlayStarted));
                    string rawTrapOutput = await llm.ApplyTrapOverlayAsync(
                        deliveredMessage, trapInstruction, trapDisplayName, dateeCtxForTrap, playerArchetypeDirectiveForDelivery, ct)
                        .ConfigureAwait(false);
                    progress?.Report(new TurnProgressEvent(TurnProgressStage.TrapOverlayCompleted, rawTrapOutput));
                    return rawTrapOutput;
                }, ct);
            }

            if (runShadow)
            {
                shadowTask = Task.Run(async () =>
                {
                    progress?.Report(new TurnProgressEvent(TurnProgressStage.ShadowCorruptionStarted));
                    string rawShadowOutput = await llm.ApplyShadowCorruptionAsync(
                        deliveredMessage, corruptionInstruction, shadowType, playerArchetypeDirectiveForDelivery, ct).ConfigureAwait(false);
                    progress?.Report(new TurnProgressEvent(TurnProgressStage.ShadowCorruptionCompleted, rawShadowOutput));
                    return rawShadowOutput;
                }, ct);
            }

            // #1041 (Tier C): When both Trap and Shadow are requested, check whether
            // the waste tracker recommends sequential dispatch. In sequential mode we
            // run Trap first; if it fires (changes the message) we skip the speculative
            // shadow task entirely and re-run shadow on the trap result, eliminating
            // the wasted parallel call. If Trap does not fire, shadow's speculative
            // result is valid and we proceed as in parallel mode.
            bool useParallel = (wasteTracker == null || wasteTracker.ShouldRunParallel);

            if (runTrap && runShadow && !useParallel)
            {
                // Sequential mode: run Trap, check if it changed the message,
                // then run Shadow on whichever message is current.
                string rawTrapResult = await trapTask!.ConfigureAwait(false);
                string trapResult = TextSanitizer.Sanitize(rawTrapResult, MetaPrefixStripper.LayerName, textDiffs);
                if (trapResult != deliveredMessage)
                {
                    var trapSpans = WordDiff.Compute(deliveredMessage, trapResult);
                    textDiffs.Add(new TextDiff(
                        $"Trap ({trapDisplayName})", trapSpans, deliveredMessage, trapResult));
                }
                else
                {
                    EmitTextLayerNoop(onTextLayerNoop, turnNumber, $"Trap ({trapDisplayName})", deliveredMessage, trapResult);
                }

                // Shadow always runs on the (possibly trap-modified) message — no waste.
                progress?.Report(new TurnProgressEvent(TurnProgressStage.ShadowCorruptionStarted));
                string rawShadowResult = await llm.ApplyShadowCorruptionAsync(
                    trapResult, corruptionInstruction, shadowType,
                    playerArchetypeDirectiveForDelivery, ct).ConfigureAwait(false);
                progress?.Report(new TurnProgressEvent(TurnProgressStage.ShadowCorruptionCompleted, rawShadowResult));

                string sanitizedShadow = TextSanitizer.Sanitize(rawShadowResult, MetaPrefixStripper.LayerName, textDiffs);
                bool shadowApplied = sanitizedShadow != trapResult;
                if (shadowApplied)
                {
                    var shadowSpans = WordDiff.Compute(trapResult, sanitizedShadow);
                    textDiffs.Add(new TextDiff($"Shadow ({shadowType})", shadowSpans, trapResult, sanitizedShadow));
                }
                else
                {
                    EmitTextLayerNoop(onTextLayerNoop, turnNumber, $"Shadow ({shadowType})", trapResult, sanitizedShadow);
                }
                wasteTracker.RecordNonWaste();
                return (sanitizedShadow, shadowApplied);
            }

            if (trapTask != null && shadowTask != null)
            {
                // Run concurrently (speculatively parallel)
                await Task.WhenAll(trapTask, shadowTask).ConfigureAwait(false);
                string rawTrapResult = await trapTask.ConfigureAwait(false);
                string rawShadowResult = await shadowTask.ConfigureAwait(false);

                // 1. Process Trap Output
                string trapResult = TextSanitizer.Sanitize(rawTrapResult, MetaPrefixStripper.LayerName, textDiffs);

                if (trapResult != deliveredMessage)
                {
                    var trapSpans = WordDiff.Compute(deliveredMessage, trapResult);
                    textDiffs.Add(new TextDiff(
                        $"Trap ({trapDisplayName})", trapSpans, deliveredMessage, trapResult));
                }
                else
                {
                    EmitTextLayerNoop(onTextLayerNoop, turnNumber, $"Trap ({trapDisplayName})", deliveredMessage, trapResult);
                }

                // 2. Process Shadow Output
                // If trap had a change, then the speculative shadow output on the original deliveredMessage is stale.
                // In that case, we re-run shadow corruption sequentially on the trap result.
                // #1041 (Tier C): record waste outcome so the tracker can adapt future dispatch mode.
                bool trapChanged = trapResult != deliveredMessage;
                if (!trapChanged)
                {
                    string shadowResult = TextSanitizer.Sanitize(rawShadowResult, MetaPrefixStripper.LayerName, textDiffs);
                    bool applied = shadowResult != trapResult;

                    if (applied)
                    {
                        var shadowSpans = WordDiff.Compute(trapResult, shadowResult);
                        textDiffs.Add(new TextDiff($"Shadow ({shadowType})", shadowSpans, trapResult, shadowResult));
                    }
                    else
                    {
                        EmitTextLayerNoop(onTextLayerNoop, turnNumber, $"Shadow ({shadowType})", trapResult, shadowResult);
                    }

                    wasteTracker?.RecordNonWaste();
                    return (shadowResult, applied);
                }
                else
                {
                    // Speculative shadow result is stale — record waste and re-run sequentially.
                    wasteTracker?.RecordWaste();
                    progress?.Report(new TurnProgressEvent(TurnProgressStage.ShadowCorruptionStarted));
                    string rawReRunShadowOutput = await llm.ApplyShadowCorruptionAsync(
                        trapResult, corruptionInstruction, shadowType, playerArchetypeDirectiveForDelivery, ct).ConfigureAwait(false);
                    progress?.Report(new TurnProgressEvent(TurnProgressStage.ShadowCorruptionCompleted, rawReRunShadowOutput));

                    string finalShadowResult = TextSanitizer.Sanitize(rawReRunShadowOutput, MetaPrefixStripper.LayerName, textDiffs);
                    bool applied = finalShadowResult != trapResult;

                    if (applied)
                    {
                        var shadowSpans = WordDiff.Compute(trapResult, finalShadowResult);
                        textDiffs.Add(new TextDiff($"Shadow ({shadowType})", shadowSpans, trapResult, finalShadowResult));
                    }
                    else
                    {
                        EmitTextLayerNoop(onTextLayerNoop, turnNumber, $"Shadow ({shadowType})", trapResult, finalShadowResult);
                    }

                    return (finalShadowResult, applied);
                }
            }
            else if (trapTask != null)
            {
                string rawTrapResult = await trapTask.ConfigureAwait(false);
                string trapResult = TextSanitizer.Sanitize(rawTrapResult, MetaPrefixStripper.LayerName, textDiffs);

                if (trapResult != deliveredMessage)
                {
                    var trapSpans = WordDiff.Compute(deliveredMessage, trapResult);
                    textDiffs.Add(new TextDiff(
                        $"Trap ({trapDisplayName})", trapSpans, deliveredMessage, trapResult));
                }
                else
                {
                    EmitTextLayerNoop(onTextLayerNoop, turnNumber, $"Trap ({trapDisplayName})", deliveredMessage, trapResult);
                }

                return (trapResult, false);
            }
            else if (shadowTask != null)
            {
                string rawShadowResult = await shadowTask.ConfigureAwait(false);
                string shadowResult = TextSanitizer.Sanitize(rawShadowResult, MetaPrefixStripper.LayerName, textDiffs);
                bool applied = shadowResult != deliveredMessage;

                if (applied)
                {
                    var shadowSpans = WordDiff.Compute(deliveredMessage, shadowResult);
                    textDiffs.Add(new TextDiff($"Shadow ({shadowType})", shadowSpans, deliveredMessage, shadowResult));
                }
                else
                {
                    EmitTextLayerNoop(onTextLayerNoop, turnNumber, $"Shadow ({shadowType})", deliveredMessage, shadowResult);
                }

                return (shadowResult, applied);
            }

            return (deliveredMessage, false);
        }

        private static void EmitTextLayerNoop(Action<TextLayerNoopEvent>? onTextLayerNoop, int turnNumber, string layer, string beforeText, string afterText)
        {
            if (onTextLayerNoop == null) return;
            try
            {
                string beforeHash = ComputeStableHash(beforeText);
                string afterHash = ComputeStableHash(afterText);
                onTextLayerNoop(new TextLayerNoopEvent(turnNumber, layer, beforeHash, afterHash));
            }
            catch
            {
                // Diagnostic-only path — swallow
            }
        }

        private static string ComputeStableHash(string? text)
        {
            if (text == null) return "";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                var sb = new System.Text.StringBuilder(16);
                for (int i = 0; i < Math.Min(8, bytes.Length); i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
