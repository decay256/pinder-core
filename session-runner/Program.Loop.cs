using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Pinder.Core.Characters;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Traps;
using Pinder.Core.Text;
using Pinder.LlmAdapters;
using Pinder.SessionRunner;
using Pinder.SessionRunner.Snapshot;

partial class Program
{
    internal static async Task<GameLoopResult> RunGameLoopAsync(GameSetupResult setup, string[] args)
    {
        var loopResult = new GameLoopResult();

        // ── Initialize loop variables ────────────────────────────────────
        loopResult.Turn = setup.IsResimulation ? setup.FromTurn : 0;
        loopResult.FinalOutcome = null;
        loopResult.LastStatUsed = null;
        loopResult.SecondLastStatUsed = null;

        TurnSnapshot? resimTurnSnap = null;
        if (setup.IsResimulation)
        {
            var snapOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            string turnSnapPath = Path.Combine(setup.PlaytestDir!, $"{setup.ResimulateSlug}.turn-{setup.FromTurn:D2}.snap.json");
            resimTurnSnap = JsonSerializer.Deserialize<TurnSnapshot>(File.ReadAllText(turnSnapPath), snapOpts);
            resimTurnSnap = ValidateAndPatchTurnSnapshot(resimTurnSnap!, setup.AssumptionLog);
        }

        loopResult.ConversationHistory = setup.IsResimulation && resimTurnSnap != null
            ? resimTurnSnap.ConversationHistory.Select(e => (e.Sender, e.Text)).ToList()
            : new List<(string Sender, string Text)>();

        loopResult.PerTurnTextDiffs = setup.IsResimulation && resimTurnSnap != null
            ? resimTurnSnap.ConversationHistory
                .Where(e => e.Sender == setup.Player1)
                .Select(e => e.TextDiffs ?? new List<TextDiffSnapshot>())
                .ToList()
            : new List<List<TextDiffSnapshot>>();

        string lastOpponentMsg = setup.IsResimulation && resimTurnSnap != null
            ? (resimTurnSnap.ConversationHistory.LastOrDefault(e => e.Sender == setup.Player2)?.Text ?? "")
            : "";

        // Shadow hint tracking state
        loopResult.StatsUsedHistory = setup.IsResimulation && resimTurnSnap != null
            ? resimTurnSnap.StatsUsedHistory
                .Select(s => { Enum.TryParse<StatType>(s, out var st); return st; })
                .ToList()
            : new List<StatType>();

        loopResult.HighestPctHistory = setup.IsResimulation && resimTurnSnap != null
            ? new List<bool>(resimTurnSnap.HighestPctHistory)
            : new List<bool>();

        loopResult.CharmUsageCount = setup.IsResimulation && resimTurnSnap != null ? resimTurnSnap.CharmUsageCount : 0;
        loopResult.CharmMadnessTriggered = setup.IsResimulation && resimTurnSnap != null && resimTurnSnap.CharmMadnessTriggered;
        loopResult.SaUsageCount = setup.IsResimulation && resimTurnSnap != null ? resimTurnSnap.SaUsageCount : 0;
        loopResult.SaOverthinkingTriggered = setup.IsResimulation && resimTurnSnap != null && resimTurnSnap.SaOverthinkingTriggered;
        loopResult.RizzCumulativeFailureCount = setup.IsResimulation && resimTurnSnap != null ? resimTurnSnap.RizzCumulativeFailureCount : 0;

        loopResult.ComboHistoryForSnapshot = setup.IsResimulation && resimTurnSnap != null
            ? resimTurnSnap.ComboHistory
                .Select(e => { Enum.TryParse<StatType>(e.Stat, out var s); return (s, e.Succeeded); })
                .ToList()
            : new List<(StatType Stat, bool Succeeded)>();

        loopResult.Interest = setup.Interest;
        loopResult.Momentum = setup.Momentum;

        while (loopResult.Turn < setup.MaxTurns)
        {
            loopResult.Turn++;
            TurnStart turnStart;
            try { turnStart = await setup.Session.StartTurnAsync(); }
            catch (GameEndedException ex) { loopResult.FinalOutcome = ex.Outcome; break; }

            var snap = turnStart.State;
            loopResult.Interest = snap.Interest;
            loopResult.Momentum = snap.MomentumStreak;

            Console.WriteLine();
            Console.WriteLine($"---");
            Console.WriteLine();
            Console.WriteLine($"## ═══ TURN {loopResult.Turn} ═══  [{DateTime.UtcNow.ToString("HH:mm:ss")} UTC]");
            if (!string.IsNullOrEmpty(lastOpponentMsg))
                Console.WriteLine($"**Responding to {setup.Player2}'s T{loopResult.Turn-1} reply**");
            else
                Console.WriteLine($"**{setup.Player1}'s opener | {setup.Player2}'s bio**");
            Console.WriteLine();

            if (!string.IsNullOrEmpty(lastOpponentMsg)) {
                Console.WriteLine($"**{setup.Player2}:** *\"{lastOpponentMsg}\"*");
                Console.WriteLine();
            }
            var statusParts = new List<string>();
            if (snap.ActiveTrapNames.Length > 0)
                statusParts.Add($"Traps: {string.Join(", ", snap.ActiveTrapNames)}");
            if (loopResult.Momentum >= 3)
                statusParts.Add($"Momentum +{(loopResult.Momentum>=5?3:loopResult.Momentum>=4?2:2)} ACTIVE");
            else if (loopResult.Momentum > 0)
                statusParts.Add($"Momentum: {loopResult.Momentum} win streak");
            if (statusParts.Count > 0) { Console.WriteLine(string.Join(" | ", statusParts)); Console.WriteLine(); }
            char[] letters = { 'A', 'B', 'C', 'D' };
            PrintTurnOptions(turnStart, setup, loopResult);

            var currentShadowValues = new Dictionary<ShadowStatType, int>();
            foreach (ShadowStatType shadowType in Enum.GetValues(typeof(ShadowStatType)))
                currentShadowValues[shadowType] = setup.SableShadows.GetEffectiveShadow(shadowType);

            var agentContext = new PlayerAgentContext(
                playerStats: setup.SableStats,
                opponentStats: setup.BrickStats,
                currentInterest: snap.Interest,
                interestState: snap.State,
                momentumStreak: snap.MomentumStreak,
                activeTrapNames: snap.ActiveTrapNames,
                sessionHorniness: setup.Session.SessionHorniness,
                shadowValues: currentShadowValues,
                turnNumber: snap.TurnNumber,
                playerSystemPrompt: setup.Sable.AssembledSystemPrompt,
                playerName: setup.Player1,
                opponentName: setup.Player2,
                recentHistory: loopResult.ConversationHistory.Count > 0 ? loopResult.ConversationHistory.AsReadOnly() : null,
                playerLevelBonus: setup.P1LevelBonus);
            var decision = await setup.Agent.DecideAsync(turnStart, agentContext);
            int pick = decision.OptionIndex;
            var chosen = turnStart.Options[pick];
            Console.WriteLine($"**► Player picks: {letters[pick]} ({StatLabel(chosen.Stat)})**");
            Console.WriteLine();
            Console.WriteLine(PlaytestFormatter.FormatReasoningBlock(decision, setup.Agent.GetType().Name));
            Console.WriteLine(PlaytestFormatter.FormatScoreTable(decision, turnStart.Options));
            Console.WriteLine();

            TurnResult result;
            try { result = await setup.Session.ResolveTurnAsync(pick); }
            catch (GameEndedException ex) { loopResult.FinalOutcome = ex.Outcome; break; }

            PrintTurnResultRollAndChecks(result, setup, chosen);
            PrintMessagesInterestAndShadow(result, setup, chosen, ref lastOpponentMsg, loopResult);
            PrintTrapsAndInterestChange(result, snap, setup, loopResult, result.StateAfter.Interest);

            loopResult.Interest = result.StateAfter.Interest;
            loopResult.Momentum = result.StateAfter.MomentumStreak;

            loopResult.SecondLastStatUsed = loopResult.LastStatUsed;
            loopResult.LastStatUsed = chosen.Stat;

            {
                int chosenMargin = setup.SableStats.GetEffective(chosen.Stat) + setup.P1LevelBonus
                                   - setup.BrickStats.GetDefenceDC(chosen.Stat);
                bool isHighest = true;
                for (int oi = 0; oi < turnStart.Options.Length; oi++)
                {
                    int margin = setup.SableStats.GetEffective(turnStart.Options[oi].Stat) + setup.P1LevelBonus
                                 - setup.BrickStats.GetDefenceDC(turnStart.Options[oi].Stat);
                    if (margin > chosenMargin) { isHighest = false; break; }
                }
                loopResult.HighestPctHistory.Add(isHighest);
            }
            loopResult.StatsUsedHistory.Add(chosen.Stat);
            if (chosen.Stat == StatType.Charm)
            {
                loopResult.CharmUsageCount++;
                if (loopResult.CharmUsageCount == 3 && !loopResult.CharmMadnessTriggered)
                    loopResult.CharmMadnessTriggered = true;
            }
            if (chosen.Stat == StatType.SelfAwareness)
            {
                loopResult.SaUsageCount++;
                if (loopResult.SaUsageCount == 3 && !loopResult.SaOverthinkingTriggered)
                    loopResult.SaOverthinkingTriggered = true;
            }
            if (chosen.Stat == StatType.Rizz && result.Roll != null && !result.Roll.IsSuccess)
                loopResult.RizzCumulativeFailureCount++;

            loopResult.ComboHistoryForSnapshot.Add((chosen.Stat, result.Roll?.IsSuccess ?? false));

            // ── Write turn snapshot ────────────────────────────────
            if (setup.PlaytestDir != null)
            {
                TellSnapshot? tellSnap = null;
                var tellOption = Array.Find(turnStart.Options, o => o.HasTellBonus);
                if (tellOption != null)
                    tellSnap = new TellSnapshot { Stat = StatLabel(tellOption.Stat), Description = "detected" };

                var turnSnap = BuildTurnSnapshot(
                    loopResult.Turn, result, setup.SableShadows,
                    loopResult.StatsUsedHistory, loopResult.HighestPctHistory,
                    loopResult.CharmUsageCount, loopResult.CharmMadnessTriggered,
                    loopResult.SaUsageCount, loopResult.SaOverthinkingTriggered,
                    loopResult.RizzCumulativeFailureCount,
                    loopResult.ConversationHistory,
                    loopResult.ComboHistoryForSnapshot,
                    tellSnap,
                    loopResult.PerTurnTextDiffs,
                    setup.Session.OpponentHistory,
                    playerSender: setup.Player1,
                    i18nCatalog: setup.SnapshotI18nCatalog,
                    opponentDefenseSnapshot: turnStart.OpponentDefenseSnapshot,
                    weaknessDcReduction: turnStart.WeaknessDcReduction);

                string turnSnapPath = Path.Combine(setup.PlaytestDir, $"{setup.SessionSlug}.turn-{loopResult.Turn:D2}.snap.json");
                File.WriteAllText(turnSnapPath, JsonSerializer.Serialize(turnSnap, new JsonSerializerOptions { WriteIndented = true }));
            }

            if (result.NarrativeBeat != null) { Console.WriteLine(); Console.WriteLine($"{result.NarrativeBeat}"); }
            if (result.IsGameOver) { loopResult.FinalOutcome = result.Outcome; break; }
        }

        return loopResult;
    }
}
