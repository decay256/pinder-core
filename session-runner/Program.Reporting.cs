using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.LlmAdapters;
using Pinder.LlmAdapters.Anthropic;
using Pinder.SessionRunner;

partial class Program
{
    internal static void ReportAndShutdown(GameSetupResult setup, GameLoopResult loopResult)
    {
        // ── session summary ───────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("---");
        Console.WriteLine();
        Console.WriteLine($"## Session Summary  [{DateTime.UtcNow.ToString("HH:mm:ss")} UTC]");
        Console.WriteLine();
        bool isCutoff = loopResult.FinalOutcome == null;
        string outcomeIcon = loopResult.FinalOutcome == GameOutcome.DateSecured ? "✅" :
                             loopResult.FinalOutcome == GameOutcome.Unmatched  ? "💀" :
                             isCutoff ? "⏸️" : "👻";
        string outcomeLabel = isCutoff ? $"Incomplete ({loopResult.Turn}/{setup.MaxTurns} turns)" : loopResult.FinalOutcome.ToString()!;
        Console.WriteLine($"**{outcomeIcon} {outcomeLabel} | Interest: {loopResult.Interest}/25 | Total XP: {setup.Session.TotalXpEarned}**");

        if (isCutoff)
        {
            // Compute interest state from current interest value
            InterestState currentState = loopResult.Interest <= 0  ? InterestState.Unmatched :
                                         loopResult.Interest <= 4  ? InterestState.Bored :
                                         loopResult.Interest <= 9  ? InterestState.Lukewarm :
                                         loopResult.Interest <= 15 ? InterestState.Interested :
                                         loopResult.Interest <= 20 ? InterestState.VeryIntoIt :
                                         loopResult.Interest <= 24 ? InterestState.AlmostThere :
                                                                     InterestState.DateSecured;
            string projection = OutcomeProjector.Project(
                loopResult.Interest, loopResult.Momentum, loopResult.Turn, setup.MaxTurns, currentState);
            Console.WriteLine();
            Console.WriteLine($"Projected: {projection}");
        }
        Console.WriteLine();

        // ── shadow delta table ────────────────────────────────────────────
        Console.WriteLine("## Shadow Changes This Session");
        Console.WriteLine("| Shadow | Start | End | Delta |");
        Console.WriteLine("|---|---|---|---|");
        foreach (ShadowStatType shadowType in Enum.GetValues(typeof(ShadowStatType)))
        {
            int start = setup.SableStats.GetShadow(shadowType);
            int end = setup.SableShadows.GetEffectiveShadow(shadowType);
            int shadowDelta = setup.SableShadows.GetDelta(shadowType);
            string deltaFmt = shadowDelta > 0 ? $"+{shadowDelta}" : shadowDelta.ToString();
            Console.WriteLine($"| {shadowType} | {start} | {end} | {deltaFmt} |");
        }
        Console.WriteLine();

        // ── token audit table ─────────────────────────────────────────────
        {
            var allStats = new List<CallSummaryStat>();
            if (setup.Agent is LlmPlayerAgent llmAgent)
            {
                allStats.AddRange(llmAgent.GetTokenStats());
            }
            if (allStats.Count > 0)
            {
                // Sort by turn, then adapter calls before player pick
                allStats.Sort((a, b) =>
                {
                    int tc = a.Turn.CompareTo(b.Turn);
                    if (tc != 0) return tc;
                    bool aIsPlayer = a.Type == "llm-player-pick";
                    bool bIsPlayer = b.Type == "llm-player-pick";
                    if (aIsPlayer == bIsPlayer) return 0;
                    return aIsPlayer ? 1 : -1;
                });

                Console.WriteLine();
                Console.WriteLine("## Token Audit");
                Console.WriteLine("| Turn | Call | Input | Output | Cache Read | Cache Write |");
                Console.WriteLine("|------|------|-------|--------|------------|-------------|" );
                foreach (var stat in allStats)
                    Console.WriteLine($"| {stat.Turn} | {stat.Type} | {stat.InputTokens} | {stat.OutputTokens} | {stat.CacheReadInputTokens} | {stat.CacheCreationInputTokens} |");

                int totalInput      = allStats.Sum(s => s.InputTokens);
                int totalOutput     = allStats.Sum(s => s.OutputTokens);
                int totalCacheRead  = allStats.Sum(s => s.CacheReadInputTokens);
                int totalCacheWrite = allStats.Sum(s => s.CacheCreationInputTokens);
                Console.WriteLine($"| **Total** | | **{totalInput}** | **{totalOutput}** | **{totalCacheRead}** | **{totalCacheWrite}** |");
                Console.WriteLine();
            }
        }

        (setup.Llm as IDisposable)?.Dispose();

        Console.SetOut(setup.Tee._console);
        WritePlaytestLog(setup.Buffer.ToString(), setup.Player1, setup.Player2, setup.PlaytestDir, setup.SessionNumber);

        // Write assumption log for resimulations
        if (setup.IsResimulation && setup.PlaytestDir != null && setup.AssumptionLog.Count > 0)
            WriteAssumptionLog(setup.PlaytestDir, setup.ResimulateSlug!, setup.AssumptionLog);

        if (setup.PlaytestDir != null) SessionFileCounter.ReleaseLock(setup.PlaytestDir, setup.SessionNumber);
    }
}
