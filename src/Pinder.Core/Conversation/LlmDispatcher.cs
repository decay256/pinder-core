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
        /// Dispatches speculative Trap and Shadow overlay calls in parallel, handles strip/noop logic, and returns the final message and whether shadow overlay was applied.
        /// </summary>
        public static async Task<(string FinalMessage, bool ShadowOverlayApplied)> DispatchSpeculativeCallsAsync(
            ILlmAdapter llm,
            string deliveredMessage,
            // Trap parameters
            bool runTrap,
            string trapInstruction,
            string trapDisplayName,
            string opponentCtxForTrap,
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
            CancellationToken ct)
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
                        deliveredMessage, trapInstruction, trapDisplayName, opponentCtxForTrap, playerArchetypeDirectiveForDelivery, ct)
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

            if (trapTask != null && shadowTask != null)
            {
                // Run concurrently (speculatively parallel)
                await Task.WhenAll(trapTask, shadowTask).ConfigureAwait(false);
                string rawTrapResult = await trapTask.ConfigureAwait(false);
                string rawShadowResult = await shadowTask.ConfigureAwait(false);

                // 1. Process Trap Output
                string sanitizedTrapResult = MetaPrefixStripper.Strip(rawTrapResult);
                if (sanitizedTrapResult != rawTrapResult)
                {
                    var stripSpans = WordDiff.Compute(rawTrapResult, sanitizedTrapResult);
                    textDiffs.Add(new TextDiff(
                        MetaPrefixStripper.LayerName, stripSpans,
                        rawTrapResult, sanitizedTrapResult));
                }

                string trapResult = sanitizedTrapResult;

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
                if (trapResult == deliveredMessage)
                {
                    string sanitizedShadowResult = MetaPrefixStripper.Strip(rawShadowResult);
                    if (sanitizedShadowResult != rawShadowResult)
                    {
                        var stripSpans = WordDiff.Compute(rawShadowResult, sanitizedShadowResult);
                        textDiffs.Add(new TextDiff(
                            MetaPrefixStripper.LayerName, stripSpans,
                            rawShadowResult, sanitizedShadowResult));
                    }

                    string shadowResult = sanitizedShadowResult;
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

                    return (shadowResult, applied);
                }
                else
                {
                    // Re-run sequentially on the trap result for accuracy
                    progress?.Report(new TurnProgressEvent(TurnProgressStage.ShadowCorruptionStarted));
                    string rawReRunShadowOutput = await llm.ApplyShadowCorruptionAsync(
                        trapResult, corruptionInstruction, shadowType, playerArchetypeDirectiveForDelivery, ct).ConfigureAwait(false);
                    progress?.Report(new TurnProgressEvent(TurnProgressStage.ShadowCorruptionCompleted, rawReRunShadowOutput));

                    string sanitizedShadowOutput = MetaPrefixStripper.Strip(rawReRunShadowOutput);
                    if (sanitizedShadowOutput != rawReRunShadowOutput)
                    {
                        var stripSpans = WordDiff.Compute(rawReRunShadowOutput, sanitizedShadowOutput);
                        textDiffs.Add(new TextDiff(
                            MetaPrefixStripper.LayerName, stripSpans,
                            rawReRunShadowOutput, sanitizedShadowOutput));
                    }

                    string finalShadowResult = sanitizedShadowOutput;
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
                string sanitizedTrapResult = MetaPrefixStripper.Strip(rawTrapResult);
                if (sanitizedTrapResult != rawTrapResult)
                {
                    var stripSpans = WordDiff.Compute(rawTrapResult, sanitizedTrapResult);
                    textDiffs.Add(new TextDiff(
                        MetaPrefixStripper.LayerName, stripSpans,
                        rawTrapResult, sanitizedTrapResult));
                }

                string trapResult = sanitizedTrapResult;

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
                string sanitizedShadowResult = MetaPrefixStripper.Strip(rawShadowResult);
                if (sanitizedShadowResult != rawShadowResult)
                {
                    var stripSpans = WordDiff.Compute(rawShadowResult, sanitizedShadowResult);
                    textDiffs.Add(new TextDiff(
                        MetaPrefixStripper.LayerName, stripSpans,
                        rawShadowResult, sanitizedShadowResult));
                }

                string shadowResult = sanitizedShadowResult;
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
