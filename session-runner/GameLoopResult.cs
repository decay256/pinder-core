using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;
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
