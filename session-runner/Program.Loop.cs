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

public class GameLoopResult
{
    public int Turn { get; set; }
    public GameOutcome? FinalOutcome { get; set; }
    public int Interest { get; set; }
    public int Momentum { get; set; }
    public StatType? LastStatUsed { get; set; }
    public StatType? SecondLastStatUsed { get; set; }

    public List<(string Sender, string Text)> ConversationHistory { get; set; } = new List<(string Sender, string Text)>();
    public List<List<TextDiffSnapshot>> PerTurnTextDiffs { get; set; } = new List<List<TextDiffSnapshot>>();
    public List<StatType> StatsUsedHistory { get; set; } = new List<StatType>();
    public List<bool> HighestPctHistory { get; set; } = new List<bool>();

    public int CharmUsageCount { get; set; }
    public bool CharmMadnessTriggered { get; set; }
    public int SaUsageCount { get; set; }
    public bool SaOverthinkingTriggered { get; set; }
    public int RizzCumulativeFailureCount { get; set; }

    public List<(StatType Stat, bool Succeeded)> ComboHistoryForSnapshot { get; set; } = new List<(StatType Stat, bool Succeeded)>();
}

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
            for (int i = 0; i < turnStart.Options.Length; i++) {
                var opt = turnStart.Options[i];
                int mod = setup.SableStats.GetEffective(opt.Stat);
                int dc = setup.BrickStats.GetDefenceDC(opt.Stat);
                int need = dc - mod; // base stat only — bonuses added via displayBonus
                int displayBonus = 0;
                var bonusAnnotations = new List<string>();
                if (setup.P1LevelBonus > 0) { displayBonus += setup.P1LevelBonus; bonusAnnotations.Add($"+{setup.P1LevelBonus} Lv"); }
                int mBonus = loopResult.Momentum >= 5 ? 3 : loopResult.Momentum >= 3 ? 2 : 0;
                if (mBonus > 0) { displayBonus += mBonus; bonusAnnotations.Add($"+{mBonus} momentum"); }
                if (opt.HasTellBonus) { displayBonus += 2; bonusAnnotations.Add("+2 tell"); }
                if (snap.TripleBonusActive) { displayBonus += 1; bonusAnnotations.Add("+1 Triple"); }
                if (opt.CallbackTurnNumber.HasValue)
                {
                    int cbBonus = CallbackBonus.Compute(snap.TurnNumber, opt.CallbackTurnNumber.Value);
                    if (cbBonus > 0) { displayBonus += cbBonus; bonusAnnotations.Add($"+{cbBonus} callback"); }
                }
                int effectiveNeed = need - displayBonus;
                int pct = effectiveNeed >= 20 ? 5 : Math.Max(0, Math.Min(100, (21-effectiveNeed)*5));
                string pctAnnotation = bonusAnnotations.Count > 0 ? $" ({string.Join(", ", bonusAnnotations)})" : "";
                string riskColor = RiskLabel(effectiveNeed);
                int riskBonus = effectiveNeed <= 7 ? 1 : effectiveNeed <= 11 ? 2 : effectiveNeed <= 15 ? 3 : effectiveNeed <= 19 ? 5 : 10;
                string riskBonusTag = $" [+{riskBonus}i★]";
                var badges = new List<string>();
                if (opt.HasTellBonus)               badges.Add("📖 Tell (+2 bonus)");
                if (opt.ComboName != null)           badges.Add($"⭐ Combo: {opt.ComboName} ({PlaytestFormatter.GetComboRewardSummary(opt.ComboName)})");
                if (opt.CallbackTurnNumber.HasValue)
                {
                    int cbTurn = opt.CallbackTurnNumber.Value;
                    int cbBadgeBonus = CallbackBonus.Compute(snap.TurnNumber, cbTurn);
                    string cbTurnLabel = cbTurn == 0 ? "opener" : $"turn {cbTurn}";
                    badges.Add($"🔗 +{cbBadgeBonus} (refs {cbTurnLabel})");
                }
                if (opt.HasWeaknessWindow)           badges.Add("🎯 Window (+DC reduction)");
                var shadowCtx = new ShadowHintContext
                {
                    StatsUsedHistory = loopResult.StatsUsedHistory,
                    HighestPctHistory = loopResult.HighestPctHistory,
                    CurrentInterest = loopResult.Interest,
                    CharmUsageCount = loopResult.CharmUsageCount,
                    CharmMadnessTriggered = loopResult.CharmMadnessTriggered,
                    SaUsageCount = loopResult.SaUsageCount,
                    SaOverthinkingTriggered = loopResult.SaOverthinkingTriggered,
                    RizzCumulativeFailureCount = loopResult.RizzCumulativeFailureCount,
                    CurrentOptions = turnStart.Options,
                    PlayerStats = setup.SableStats,
                    OpponentStats = setup.BrickStats,
                    PlayerLevelBonus = setup.P1LevelBonus,
                    HonestyAvailable = Array.Exists(turnStart.Options, o => o.Stat == StatType.Honesty)
                };
                var shadowHints = ShadowHintComputer.ComputeShadowHints(opt, shadowCtx);
                badges.AddRange(shadowHints);
                if (loopResult.LastStatUsed.HasValue && loopResult.SecondLastStatUsed.HasValue
                    && loopResult.LastStatUsed.Value == loopResult.SecondLastStatUsed.Value
                    && opt.Stat != loopResult.LastStatUsed.Value)
                    badges.Add("✨ breaks streak");
                string badgeStr = badges.Count > 0 ? " | " + string.Join(", ", badges) : "";
                Console.WriteLine($"**{letters[i]})** {StatLabel(opt.Stat)} {mod:+#;-#;0} | {pct}%{pctAnnotation} {riskColor}{riskBonusTag}{badgeStr}");
                
                if (opt.ComboName != null)
                {
                    Console.WriteLine($"> *{opt.ComboName}: {PlaytestFormatter.GetComboSequenceDescription(opt.ComboName)}*");
                }

                if (!string.IsNullOrEmpty(opt.IntendedText) && opt.IntendedText != "...")
                    Console.WriteLine($"> \"{opt.IntendedText}\"");
                Console.WriteLine();
            }

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

            var roll = result.Roll;
            string rollMod = $"{roll.StatModifier:+#;-#;0}";
            string lvPart = roll.LevelBonus > 0 ? $"+Lv({roll.LevelBonus:+#;-#;0})" : "";
            var _extParts = new System.Text.StringBuilder();
            if (result.TripleBonusApplied > 0)    _extParts.Append($"+Triple(+{result.TripleBonusApplied})");
            if (result.TellReadBonus > 0)          _extParts.Append($"+Tell(+{result.TellReadBonus})");
            if (result.CallbackBonusApplied > 0)   _extParts.Append($"+Callback(+{result.CallbackBonusApplied})");
            string extBonusPart = _extParts.ToString();
            string rollResult;
            if (roll.IsNatTwenty)     rollResult = "NAT 20 ⭐ — always succeeds";
            else if (roll.IsNatOne)   rollResult = "NAT 1 💀 — always fails";
            else if (roll.Tier == FailureTier.Success) rollResult = $"SUCCESS";
            else                      rollResult = roll.Tier.ToString().ToUpperInvariant();

            string marginText;
            if (roll.FinalTotal >= roll.DC)
            {
                if (roll.IsNatOne)
                {
                    marginText = $"Total beat DC by {roll.FinalTotal - roll.DC} — but NAT 1 💀 always fails";
                    rollResult = "";
                }
                else
                {
                    marginText = $"Beat by {roll.FinalTotal - roll.DC}";
                }
            }
            else
            {
                marginText = $"Miss by {roll.DC - roll.FinalTotal}";
            }

            string arrowResult = string.IsNullOrEmpty(rollResult) ? "" : $" → {rollResult}";
            Console.WriteLine($"**🎲 Roll:** d20({roll.UsedDieRoll})+{StatLabel(chosen.Stat)}({rollMod}){lvPart}{extBonusPart} = **{roll.FinalTotal}** vs DC {roll.DC} → **{marginText}{arrowResult}**");

            string rollExplanation = GetRollExplanation(roll);
            if (!string.IsNullOrEmpty(rollExplanation))
                Console.WriteLine($"> 📋 *{rollExplanation}*");

            if (roll.ActivatedTrap != null)
            {
                var trap = roll.ActivatedTrap;
                string effectDesc = trap.Effect switch
                {
                    TrapEffect.Disadvantage        => $"disadvantage on {trap.Stat} rolls",
                    TrapEffect.StatPenalty         => $"-{trap.EffectValue} to {trap.Stat} rolls",
                    TrapEffect.OpponentDCIncrease  => $"opponent DC +{trap.EffectValue}",
                    _                              => trap.Effect.ToString()
                };
                int dur = trap.DurationTurns;
                string turnWord = dur == 1 ? "turn" : "turns";
                Console.WriteLine($"> 🪤 *Trap activated: {trap.Id} [{trap.Stat}] — {effectDesc} for {dur} {turnWord} (clear with {trap.ClearMethod})*");
            }
            Console.WriteLine();

            if (result.TripleBonusApplied > 0)
                Console.WriteLine($"> *⚡ Combo: The Triple — +{result.TripleBonusApplied} to this roll*");
            if (result.ComboTriggered != null)
            {
                Console.WriteLine($"> *⭐ {result.ComboTriggered} combo fires!*");
                Console.WriteLine($"> *{PlaytestFormatter.GetComboSequenceDescription(result.ComboTriggered)}*");
                Console.WriteLine($"> *{PlaytestFormatter.GetComboRewardSummary(result.ComboTriggered)}*");
            }
            if (result.TellReadBonus > 0)      Console.WriteLine($"> *📖 Tell read! +{result.TellReadBonus}*");
            Console.WriteLine();

            if (result.Steering != null && result.Steering.SteeringAttempted)
            {
                int steeringTotal = result.Steering.SteeringRoll + result.Steering.SteeringMod;
                if (result.Steering.SteeringSucceeded)
                {
                    Console.WriteLine($"> 🧭 Steering roll: d20({result.Steering.SteeringRoll}) + {result.Steering.SteeringMod} = {steeringTotal} vs DC {result.Steering.SteeringDC} → SUCCESS");
                    Console.WriteLine($"> *{setup.Player1} adds:* \"{result.Steering.SteeringQuestion}\"");
                }
                else
                {
                    Console.WriteLine($"> 🧭 Steering roll: d20({result.Steering.SteeringRoll}) + {result.Steering.SteeringMod} = {steeringTotal} vs DC {result.Steering.SteeringDC} → MISS");
                }
                Console.WriteLine();
            }

            if (result.HorninessCheck != null && result.HorninessCheck.DC > 0)
            {
                var hc = result.HorninessCheck;
                string hcResult = hc.IsMiss
                    ? $"MISS ({hc.Tier}){(hc.OverlayApplied ? " — overlay applied" : "")}"
                    : "OK";
                Console.WriteLine($"> 🌶️ Horniness check: d20({hc.Roll}) + {hc.Modifier} = {hc.Total} vs DC {hc.DC} → {hcResult}");
                Console.WriteLine();
            }

            if (result.ShadowCheck != null && result.ShadowCheck.CheckPerformed)
            {
                var sc = result.ShadowCheck;
                string scResult = sc.IsMiss
                    ? $"MISS ({sc.Tier}){(sc.OverlayApplied ? " — corruption applied" : "")}"
                    : "OK";
                Console.WriteLine($"> ⚫ Shadow check ({sc.Shadow}): d20({sc.Roll}) + 0 = {sc.Roll} vs DC {sc.DC} → {scResult}");
                if (sc.OverlayApplied)
                    Console.WriteLine($"  ↳ Shadow override ({sc.Shadow} {sc.Tier}): success forced to fail");
                Console.WriteLine();
            }

            Console.WriteLine($"**📨 {setup.Player1} sends:**");

            if (result.TextDiffs == null || result.TextDiffs.Count == 0)
            {
                PrintQuoted(result.DeliveredMessage);
            }
            else
            {
                string intended = chosen.IntendedText ?? "";
                string intendedDisplay = string.IsNullOrWhiteSpace(intended) || intended == "..." ? "..." : $"\"{intended}\"";
                PrintQuoted("**Intended:** " + intendedDisplay);
                Console.WriteLine();

                foreach (var diff in result.TextDiffs)
                {
                    string rendered = RenderDiff(diff);
                    PrintQuoted($"**Diff ({diff.LayerName}):** \"{rendered}\"");
                    Console.WriteLine();
                }
            }
            Console.WriteLine();

            if (!string.IsNullOrEmpty(result.DeliveredMessage))
            {
                loopResult.ConversationHistory.Add((setup.Player1, result.DeliveredMessage));
                loopResult.PerTurnTextDiffs.Add(
                    (result.TextDiffs ?? Array.Empty<Pinder.Core.Text.TextDiff>())
                        .Select(d => new TextDiffSnapshot
                        {
                            Layer = d.LayerName,
                            Before = d.Before,
                            After = d.After,
                            Spans = d.Spans.Select(s => new TextDiffSpanSnapshot
                            {
                                Type = s.Type.ToString(),
                                Text = s.Text,
                            }).ToList(),
                        })
                        .ToList());
            }
            lastOpponentMsg = result.OpponentMessage ?? "";
            if (!string.IsNullOrEmpty(lastOpponentMsg))
                loopResult.ConversationHistory.Add((setup.Player2, lastOpponentMsg));
            Console.WriteLine($"**📩 {setup.Player2} replies:**");
            PrintQuoted(result.OpponentMessage);
            Console.WriteLine();

            int newInterest = result.StateAfter.Interest;
            int delta = result.InterestDelta;
            string deltaStr = delta >= 0 ? $"+{delta}" : delta.ToString();
            Console.WriteLine("---");
            Console.WriteLine();
            Console.WriteLine("```");
            Console.WriteLine($"Interest: {InterestBar(newInterest)}  {newInterest}/25  ({deltaStr})");

            if (delta != 0 && result.Roll != null)
            {
                var parts = new List<string>();
                if (result.Roll.IsSuccess)
                {
                    string baseSign = result.BaseInterestDelta >= 0 ? "+" : "";
                    parts.Add($"Roll success {baseSign}{result.BaseInterestDelta}");
                }
                else
                {
                    string tierName = result.Roll.Tier.ToString();
                    parts.Add($"{tierName} miss {result.BaseInterestDelta}");
                }

                if (result.RiskBonusDelta != 0)
                {
                    string riskSign = result.RiskBonusDelta >= 0 ? "+" : "";
                    parts.Add($"Risk bonus ({result.RiskTier}) {riskSign}{result.RiskBonusDelta}");
                }

                if (result.ComboBonusDelta != 0 && result.ComboTriggered != null)
                {
                    string comboSign = result.ComboBonusDelta >= 0 ? "+" : "";
                    string comboExpl = PlaytestFormatter.GetComboBreakdownExplanation(result.ComboTriggered);
                    parts.Add($"Combo: {result.ComboTriggered} {comboSign}{result.ComboBonusDelta} ({comboExpl})");
                }

                if (parts.Count > 0)
                    Console.WriteLine($"  ↳ {string.Join(" | ", parts)}");
            }
            if (result.HorninessInterestPenalty != 0)
            {
                int penaltyAfter = result.HorninessInterestBefore + result.HorninessInterestPenalty;
                Console.WriteLine($"  ↳ Horniness penalty: turn gain halved (interest {result.HorninessInterestBefore} → {penaltyAfter})");
            }
            if (result.ShadowGrowthEvents?.Count > 0)
            {
                var enrichedShadow = new List<string>();
                foreach (var se in result.ShadowGrowthEvents)
                    enrichedShadow.Add(PlaytestFormatter.EnrichShadowEvent(se));
                Console.WriteLine($"📊 Shadow: {string.Join(" | ", enrichedShadow)}");
                foreach (var shadowEvent in result.ShadowGrowthEvents)
                {
                    foreach (ShadowStatType sType in Enum.GetValues(typeof(ShadowStatType)))
                    {
                        if (shadowEvent.Contains(sType.ToString()))
                        {
                            int sv = setup.SableShadows.GetEffectiveShadow(sType);
                            string paired = GetPairedStat(sType);
                            if (sv == 6) Console.WriteLine($"> ⚠️ *Threshold 6: {sType} now taints {paired} dialogue.*");
                            else if (sv == 12) Console.WriteLine($"> ⚠️ *Threshold 12: {sType} now penalizes {paired} rolls.*");
                            else if (sv == 18) Console.WriteLine($"> ⚠️ *Threshold 18: {sType} may override your {paired} choices.*");
                        }
                    }
                }
            }
            if (result.StateAfter.ActiveTrapDetails.Length > 0)
            {
                foreach (var trap in result.StateAfter.ActiveTrapDetails)
                {
                    Console.WriteLine($"🪤 Trap: {trap.Name} [{trap.Stat}] — {trap.TurnsRemaining} turn{(trap.TurnsRemaining != 1 ? "s" : "")} remaining — {trap.PenaltyDescription} (activated by {trap.Stat} check failure)");
                }
            }
            else
            {
                Console.WriteLine("Active Traps: none");
                var activatedTrap = result.Roll?.ActivatedTrap;
                if (activatedTrap != null && activatedTrap.DurationTurns <= 1)
                {
                    Console.WriteLine($"  ↳ ({activatedTrap.Id} [{activatedTrap.Stat}] was active this turn — expired after 1 turn)");
                }
            }
            if (result.StateAfter.MomentumStreak >= 3)
            {
                int mBonus = result.StateAfter.MomentumStreak >= 5 ? 3 : 2;
                string momExpl = PlaytestFormatter.GetMomentumExplanation(result.StateAfter.MomentumStreak);
                Console.WriteLine($"⚡ Momentum: {result.StateAfter.MomentumStreak}-streak → +{mBonus} bonus ({momExpl})");
            }
            else
            {
                Console.WriteLine($"Momentum: {result.StateAfter.MomentumStreak} win{(result.StateAfter.MomentumStreak != 1 ? "s" : "")}");
            }
            Console.WriteLine("```");

            {
                InterestState stateBefore = snap.State;
                InterestState stateAfter = result.StateAfter.State;
                int newI = result.StateAfter.Interest;
                string tierRange = GetInterestTierRange(stateAfter);
                string stateDesc = GetInterestStateDescription(stateAfter);
                if (stateBefore != stateAfter)
                {
                    Console.WriteLine($"💡 Interest: {newI} — **{stateAfter}** ({tierRange}: {stateDesc})");
                }
                else if (loopResult.Turn == 1)
                {
                    Console.WriteLine($"💡 Interest: {newI} — {stateAfter} ({tierRange}: {stateDesc})");
                }
            }

            loopResult.Interest = newInterest;
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
