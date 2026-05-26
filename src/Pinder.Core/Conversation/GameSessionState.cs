using System;
using System.Collections.Generic;
using Pinder.Core.Characters;
using Pinder.Core.I18n;
using Pinder.Core.Interfaces;
using Pinder.Core.Rolls;
using Pinder.Core.Stats;
using Pinder.Core.Progression;
using Pinder.Core.Traps;
using Pinder.Core.Text;

namespace Pinder.Core.Conversation
{
    public sealed class GameSessionState
    {
        public InterestMeter Interest { get; internal set; } = new InterestMeter();
        public TrapState Traps { get; internal set; } = new TrapState();
        public List<(string Sender, string Text)> History { get; internal set; } = new List<(string Sender, string Text)>();
        public string OpponentOutfitDescription { get; internal set; } = string.Empty;
        public List<ConversationMessage> OpponentHistory { get; internal set; } = new List<ConversationMessage>();
        public SessionShadowTracker? PlayerShadows { get; internal set; }
        public SessionShadowTracker? OpponentShadows { get; internal set; }
        public ComboTracker ComboTracker { get; internal set; } = new ComboTracker();
        public List<CallbackOpportunity> Topics { get; internal set; } = new List<CallbackOpportunity>();
        public int RizzCumulativeFailureCount { get; internal set; }
        public int MomentumStreak { get; internal set; }
        public int PendingMomentumBonus { get; internal set; }
        public int TurnNumber { get; internal set; }
        public bool Ended { get; internal set; }
        public GameOutcome? Outcome { get; internal set; }
        public XpLedger XpLedger { get; internal set; } = new XpLedger();
        public WeaknessWindow? ActiveWeakness { get; internal set; }
        public Tell? ActiveTell { get; internal set; }
        public int SessionHorniness { get; internal set; }
        public int HorninessRoll { get; internal set; }
        public int HorninessTimeModifier { get; internal set; }
        public bool PendingCritAdvantage { get; internal set; }
        public StatType? LastStatUsed { get; internal set; }
        public HashSet<StatType>? ShadowDisadvantagedStats { get; internal set; }
        public Dictionary<ShadowStatType, int>? CurrentShadowThresholds { get; internal set; }
        public DialogueOption[]? CurrentOptions { get; internal set; }
        public bool CurrentHasAdvantage { get; internal set; }
        public bool CurrentHasDisadvantage { get; internal set; }
        public Pinder.Core.Rolls.PerOptionDicePool[]? CurrentDicePools { get; internal set; }
        public Pinder.Core.Rolls.PerOptionDicePool? InjectedNextPool { get; internal set; }

        public GameSessionState()
        {
        }

        public GameSessionState Clone()
        {
            var clone = new GameSessionState();
            clone.Interest = Interest.Clone();
            clone.Traps = Traps.Clone();
            clone.History = new List<(string Sender, string Text)>(History);
            clone.OpponentOutfitDescription = OpponentOutfitDescription;
            clone.OpponentHistory = new List<ConversationMessage>(OpponentHistory);
            clone.PlayerShadows = PlayerShadows?.Clone();
            clone.OpponentShadows = OpponentShadows?.Clone();
            clone.ComboTracker = ComboTracker.Clone();
            clone.Topics = new List<CallbackOpportunity>(Topics);
            clone.RizzCumulativeFailureCount = RizzCumulativeFailureCount;
            clone.MomentumStreak = MomentumStreak;
            clone.PendingMomentumBonus = PendingMomentumBonus;
            clone.TurnNumber = TurnNumber;
            clone.Ended = Ended;
            clone.Outcome = Outcome;
            clone.XpLedger = XpLedger.Clone();
            clone.ActiveWeakness = ActiveWeakness;
            clone.ActiveTell = ActiveTell;
            clone.SessionHorniness = SessionHorniness;
            clone.HorninessRoll = HorninessRoll;
            clone.HorninessTimeModifier = HorninessTimeModifier;
            clone.PendingCritAdvantage = PendingCritAdvantage;
            clone.LastStatUsed = LastStatUsed;
            clone.ShadowDisadvantagedStats = ShadowDisadvantagedStats != null
                ? new HashSet<StatType>(ShadowDisadvantagedStats)
                : null;
            clone.CurrentShadowThresholds = CurrentShadowThresholds != null
                ? new Dictionary<ShadowStatType, int>(CurrentShadowThresholds)
                : null;
            clone.CurrentOptions = CurrentOptions != null
                ? (DialogueOption[])CurrentOptions.Clone()
                : null;
            clone.CurrentHasAdvantage = CurrentHasAdvantage;
            clone.CurrentHasDisadvantage = CurrentHasDisadvantage;
            clone.CurrentDicePools = CurrentDicePools != null
                ? (Pinder.Core.Rolls.PerOptionDicePool[])CurrentDicePools.Clone()
                : null;
            clone.InjectedNextPool = InjectedNextPool;
            return clone;
        }

        public void AdoptStateFrom(GameSessionState src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));

            Interest = src.Interest.Clone();
            Traps = src.Traps.Clone();
            History.Clear(); History.AddRange(src.History);
            OpponentHistory.Clear(); OpponentHistory.AddRange(src.OpponentHistory);
            ComboTracker = src.ComboTracker.Clone();
            Topics.Clear(); Topics.AddRange(src.Topics);
            XpLedger = src.XpLedger.Clone();
            PlayerShadows = src.PlayerShadows?.Clone();
            OpponentShadows = src.OpponentShadows?.Clone();

            MomentumStreak = src.MomentumStreak;
            PendingMomentumBonus = src.PendingMomentumBonus;
            TurnNumber = src.TurnNumber;
            Ended = src.Ended;
            Outcome = src.Outcome;
            RizzCumulativeFailureCount = src.RizzCumulativeFailureCount;
            HorninessRoll = src.HorninessRoll;
            HorninessTimeModifier = src.HorninessTimeModifier;
            SessionHorniness = src.SessionHorniness;
            PendingCritAdvantage = src.PendingCritAdvantage;
            LastStatUsed = src.LastStatUsed;
            ActiveWeakness = src.ActiveWeakness;
            ActiveTell = src.ActiveTell;
            ShadowDisadvantagedStats = src.ShadowDisadvantagedStats != null
                ? new HashSet<StatType>(src.ShadowDisadvantagedStats)
                : null;
            CurrentShadowThresholds = src.CurrentShadowThresholds != null
                ? new Dictionary<ShadowStatType, int>(src.CurrentShadowThresholds)
                : null;

            CurrentOptions = src.CurrentOptions != null
                ? (DialogueOption[])src.CurrentOptions.Clone()
                : null;
            CurrentHasAdvantage = src.CurrentHasAdvantage;
            CurrentHasDisadvantage = src.CurrentHasDisadvantage;
            CurrentDicePools = src.CurrentDicePools != null
                ? (Pinder.Core.Rolls.PerOptionDicePool[])src.CurrentDicePools.Clone()
                : null;
            InjectedNextPool = src.InjectedNextPool;
        }

        public void RestoreFromSnapshot(ResimulateData data, ITrapRegistry trapRegistry)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (trapRegistry == null) throw new ArgumentNullException(nameof(trapRegistry));

            int interestDelta = data.TargetInterest - Interest.Current;
            if (interestDelta != 0)
                Interest.Apply(interestDelta);

            if (PlayerShadows != null && data.ShadowValues != null)
                PlayerShadows.RestoreFromSnapshot(data.ShadowValues);

            MomentumStreak = data.MomentumStreak;

            Traps.ClearAll();
            if (data.ActiveTraps != null)
            {
                foreach (var (statName, turnsRemaining) in data.ActiveTraps)
                {
                    if (Enum.TryParse<StatType>(statName, ignoreCase: true, out var stat))
                    {
                        var definition = trapRegistry.GetTrap(stat);
                        if (definition != null)
                            Traps.Activate(definition, turnsRemaining);
                    }
                }
            }

            History.Clear();
            if (data.ConversationHistory != null)
                History.AddRange(data.ConversationHistory);

            OpponentHistory.Clear();
            if (data.OpponentHistory != null)
            {
                foreach (var (role, content) in data.OpponentHistory)
                {
                    if (string.IsNullOrEmpty(role)) continue;
                    try
                    {
                        OpponentHistory.Add(new ConversationMessage(role, content ?? string.Empty));
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }

            TurnNumber = data.TurnNumber;

            ComboTracker.RestoreFromSnapshot(
                data.ComboHistory ?? new List<(string StatName, bool Succeeded)>(),
                data.PendingTripleBonus);

            RizzCumulativeFailureCount = data.RizzCumulativeFailureCount;
        }
    }
}
