using System;
using System.Collections.Generic;
using System.Linq;
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
        public string DateeOutfitDescription { get; internal set; } = string.Empty;
        public List<ConversationMessage> DateeHistory { get; internal set; } = new List<ConversationMessage>();
        public List<CharacterEmotionalDirectionSummary> DateeEmotionalDirectionHistory { get; internal set; } = new List<CharacterEmotionalDirectionSummary>();
        // Avatar-perspective semantic history: what the player character knows.
        // There is no separate delivery-model call; session-capable adapters use
        // this mirror for option generation and future private reasoning.
        public List<ConversationMessage> AvatarHistory { get; internal set; } = new List<ConversationMessage>();
        public LlmConversationSessionSnapshot? DateeSessionSnapshot { get; internal set; }
        public LlmConversationSessionSnapshot? AvatarSessionSnapshot { get; internal set; }
        public HashSet<int> AvatarSpentBackstoryIndices { get; internal set; } = new HashSet<int>();
        public HashSet<int> AvatarSpentStakeIndices { get; internal set; } = new HashSet<int>();
        public string? AvatarPreviousPhase { get; set; }
        public int AvatarPreviousResolvedIndex { get; set; }
        public AvatarRevelationTarget? CurrentAvatarRevelationTarget { get; set; }
        public string? CurrentAvatarCognitiveSubtext { get; set; }
        public OwnedPromptFactV1? CurrentAvatarCognitiveSubtextFact { get; set; }
        public HashSet<int> DateeSpentBackstoryIndices { get; internal set; } = new HashSet<int>();
        public HashSet<int> DateeSpentStakeIndices { get; internal set; } = new HashSet<int>();
        public string? DateePreviousPhase { get; set; }
        public int DateePreviousResolvedIndex { get; set; }
        public DateeReactionTarget? CurrentDateeReactionTarget { get; set; }
        public ResolvedRevelationTarget? LegacyCurrentResolvedTarget { get; set; }
        public string? CurrentDateeCognitiveSubtext { get; set; }
        public OwnedPromptFactV1? CurrentDateeCognitiveSubtextFact { get; set; }
        public HashSet<int> SpentBackstoryIndices { get => AvatarSpentBackstoryIndices; internal set => AvatarSpentBackstoryIndices = value; }
        public HashSet<int> SpentStakeIndices { get => AvatarSpentStakeIndices; internal set => AvatarSpentStakeIndices = value; }
        public string? PreviousPhase { get => AvatarPreviousPhase; set => AvatarPreviousPhase = value; }
        public int PreviousResolvedIndex { get => AvatarPreviousResolvedIndex; set => AvatarPreviousResolvedIndex = value; }
        public ResolvedRevelationTarget? CurrentResolvedTarget { get => CurrentAvatarRevelationTarget?.ResolvedTarget ?? LegacyCurrentResolvedTarget; set => LegacyCurrentResolvedTarget = value; }
        public string? CurrentCognitiveSubtext { get => CurrentDateeCognitiveSubtext; set => CurrentDateeCognitiveSubtext = value; }
        public SessionShadowTracker? PlayerShadows { get; internal set; }
        public SessionShadowTracker? DateeShadows { get; internal set; }
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
        public SpeculativeWasteTracker SpeculativeWasteTracker { get; internal set; } = new SpeculativeWasteTracker();

        public GameSessionState()
        {
        }

        public GameSessionState Clone()
        {
            var clone = new GameSessionState();
            clone.Interest = Interest.Clone();
            clone.Traps = Traps.Clone();
            clone.History = new List<(string Sender, string Text)>(History);
            clone.DateeOutfitDescription = DateeOutfitDescription;
            clone.DateeHistory = new List<ConversationMessage>(DateeHistory);
            clone.DateeEmotionalDirectionHistory = new List<CharacterEmotionalDirectionSummary>(DateeEmotionalDirectionHistory);
            clone.AvatarHistory = new List<ConversationMessage>(AvatarHistory);
            clone.DateeSessionSnapshot = DateeSessionSnapshot;
            clone.AvatarSessionSnapshot = AvatarSessionSnapshot;
            foreach (var idx in AvatarSpentBackstoryIndices) clone.AvatarSpentBackstoryIndices.Add(idx);
            foreach (var idx in AvatarSpentStakeIndices) clone.AvatarSpentStakeIndices.Add(idx);
            clone.AvatarPreviousPhase = AvatarPreviousPhase;
            clone.AvatarPreviousResolvedIndex = AvatarPreviousResolvedIndex;
            clone.CurrentAvatarRevelationTarget = CurrentAvatarRevelationTarget;
            clone.CurrentAvatarCognitiveSubtext = CurrentAvatarCognitiveSubtext;
            clone.CurrentAvatarCognitiveSubtextFact = CurrentAvatarCognitiveSubtextFact;
            foreach (var idx in DateeSpentBackstoryIndices) clone.DateeSpentBackstoryIndices.Add(idx);
            foreach (var idx in DateeSpentStakeIndices) clone.DateeSpentStakeIndices.Add(idx);
            clone.DateePreviousPhase = DateePreviousPhase;
            clone.DateePreviousResolvedIndex = DateePreviousResolvedIndex;
            clone.CurrentDateeReactionTarget = CurrentDateeReactionTarget;
            clone.LegacyCurrentResolvedTarget = LegacyCurrentResolvedTarget;
            clone.CurrentDateeCognitiveSubtext = CurrentDateeCognitiveSubtext;
            clone.CurrentDateeCognitiveSubtextFact = CurrentDateeCognitiveSubtextFact;
            clone.PlayerShadows = PlayerShadows?.Clone();
            clone.DateeShadows = DateeShadows?.Clone();
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
            clone.SpeculativeWasteTracker = SpeculativeWasteTracker.Clone();
            return clone;
        }

        public void AdoptStateFrom(GameSessionState src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            AdoptPreparedClone(src.Clone());
        }

        internal void AdoptPreparedClone(GameSessionState prepared)
        {
            if (prepared == null) throw new ArgumentNullException(nameof(prepared));

            Interest = prepared.Interest;
            Traps = prepared.Traps;
            History = prepared.History;
            DateeOutfitDescription = prepared.DateeOutfitDescription;
            DateeHistory = prepared.DateeHistory;
            DateeEmotionalDirectionHistory = prepared.DateeEmotionalDirectionHistory;
            AvatarHistory = prepared.AvatarHistory;
            DateeSessionSnapshot = prepared.DateeSessionSnapshot;
            AvatarSessionSnapshot = prepared.AvatarSessionSnapshot;
            AvatarSpentBackstoryIndices = prepared.AvatarSpentBackstoryIndices;
            AvatarSpentStakeIndices = prepared.AvatarSpentStakeIndices;
            AvatarPreviousPhase = prepared.AvatarPreviousPhase;
            AvatarPreviousResolvedIndex = prepared.AvatarPreviousResolvedIndex;
            CurrentAvatarRevelationTarget = prepared.CurrentAvatarRevelationTarget;
            CurrentAvatarCognitiveSubtext = prepared.CurrentAvatarCognitiveSubtext;
            CurrentAvatarCognitiveSubtextFact = prepared.CurrentAvatarCognitiveSubtextFact;
            DateeSpentBackstoryIndices = prepared.DateeSpentBackstoryIndices;
            DateeSpentStakeIndices = prepared.DateeSpentStakeIndices;
            DateePreviousPhase = prepared.DateePreviousPhase;
            DateePreviousResolvedIndex = prepared.DateePreviousResolvedIndex;
            CurrentDateeReactionTarget = prepared.CurrentDateeReactionTarget;
            LegacyCurrentResolvedTarget = prepared.LegacyCurrentResolvedTarget;
            CurrentDateeCognitiveSubtext = prepared.CurrentDateeCognitiveSubtext;
            CurrentDateeCognitiveSubtextFact = prepared.CurrentDateeCognitiveSubtextFact;
            if (PlayerShadows != null && prepared.PlayerShadows != null)
                PlayerShadows.AdoptPreparedClone(prepared.PlayerShadows);
            else
                PlayerShadows = prepared.PlayerShadows;
            if (DateeShadows != null && prepared.DateeShadows != null)
                DateeShadows.AdoptPreparedClone(prepared.DateeShadows);
            else
                DateeShadows = prepared.DateeShadows;
            ComboTracker = prepared.ComboTracker;
            Topics = prepared.Topics;
            RizzCumulativeFailureCount = prepared.RizzCumulativeFailureCount;
            MomentumStreak = prepared.MomentumStreak;
            PendingMomentumBonus = prepared.PendingMomentumBonus;
            TurnNumber = prepared.TurnNumber;
            Ended = prepared.Ended;
            Outcome = prepared.Outcome;
            XpLedger = prepared.XpLedger;
            ActiveWeakness = prepared.ActiveWeakness;
            ActiveTell = prepared.ActiveTell;
            SessionHorniness = prepared.SessionHorniness;
            HorninessRoll = prepared.HorninessRoll;
            HorninessTimeModifier = prepared.HorninessTimeModifier;
            PendingCritAdvantage = prepared.PendingCritAdvantage;
            LastStatUsed = prepared.LastStatUsed;
            ShadowDisadvantagedStats = prepared.ShadowDisadvantagedStats;
            CurrentShadowThresholds = prepared.CurrentShadowThresholds;
            CurrentOptions = prepared.CurrentOptions;
            CurrentHasAdvantage = prepared.CurrentHasAdvantage;
            CurrentHasDisadvantage = prepared.CurrentHasDisadvantage;
            CurrentDicePools = prepared.CurrentDicePools;
            InjectedNextPool = prepared.InjectedNextPool;
            SpeculativeWasteTracker = prepared.SpeculativeWasteTracker;
        }

        public void RestoreFromSnapshot(ResimulateData data, ITrapRegistry trapRegistry)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.CurrentResolvedTarget != null)
                throw new InvalidOperationException("restore.role_target.legacy_ambiguous_active_target");
            if (trapRegistry == null) throw new ArgumentNullException(nameof(trapRegistry));

            var restoredInterest = new InterestMeter(data.TargetInterest);

            Dictionary<string, int>? validatedShadowValues = null;
            if (PlayerShadows != null && data.ShadowValues != null)
            {
                validatedShadowValues = new Dictionary<string, int>(data.ShadowValues);
                var shadowValidationTracker = PlayerShadows.Clone();
                shadowValidationTracker.RestoreFromSnapshot(validatedShadowValues);
            }

            Dictionary<string, int>? validatedDateeShadowValues = null;
            if (DateeShadows != null && data.DateeShadowValues != null)
            {
                validatedDateeShadowValues = new Dictionary<string, int>(data.DateeShadowValues);
                var shadowValidationTracker = DateeShadows.Clone();
                shadowValidationTracker.RestoreFromSnapshot(validatedDateeShadowValues);
            }

            var restoredTraps = new TrapState();
            if (data.ActiveTraps != null)
            {
                foreach (var (statName, turnsRemaining) in data.ActiveTraps)
                {
                    if (Enum.TryParse<StatType>(statName, ignoreCase: true, out var stat))
                    {
                        var definition = trapRegistry.GetTrap(stat);
                        if (definition != null)
                            restoredTraps.Activate(definition, turnsRemaining);
                    }
                }
            }

            var restoredHistory = data.ConversationHistory != null
                ? new List<(string Sender, string Text)>(data.ConversationHistory)
                : new List<(string Sender, string Text)>();

            var restoredDateeHistory = data.DateeHistory != null
                ? BuildConversationHistory(data.DateeHistory, "datee")
                : new List<ConversationMessage>();

            var restoredAvatarHistory = data.AvatarHistory != null
                ? BuildConversationHistory(data.AvatarHistory, "avatar")
                : new List<ConversationMessage>();

            var restoredDirectionHistory = BuildDirectionHistory(data.DateeEmotionalDirectionHistory);

            var restoredComboTracker = new ComboTracker();
            restoredComboTracker.RestoreFromSnapshot(
                data.ComboHistory ?? new List<(string StatName, bool Succeeded)>(),
                data.PendingTripleBonus);

            if (validatedShadowValues != null)
            {
                // Keep the tracker identity shared by the session pipeline and host.
                PlayerShadows!.RestoreFromSnapshot(validatedShadowValues);
            }
            if (validatedDateeShadowValues != null)
                DateeShadows!.RestoreFromSnapshot(validatedDateeShadowValues);

            var restoredXpLedger = new XpLedger();
            if (data.XpEvents != null)
            {
                foreach (var (source, amount) in data.XpEvents)
                    restoredXpLedger.Record(source, amount);
                restoredXpLedger.DrainTurnEvents();
            }

            Interest = restoredInterest;
            MomentumStreak = data.MomentumStreak;
            Traps = restoredTraps;
            History = restoredHistory;
            DateeHistory = restoredDateeHistory;
            DateeEmotionalDirectionHistory = restoredDirectionHistory;
            AvatarHistory = restoredAvatarHistory;
            DateeSessionSnapshot = data.DateeSessionSnapshot;
            AvatarSessionSnapshot = data.AvatarSessionSnapshot;
            TurnNumber = data.TurnNumber;
            ComboTracker = restoredComboTracker;
            RizzCumulativeFailureCount = data.RizzCumulativeFailureCount;
            Topics = data.Topics != null
                ? new List<CallbackOpportunity>(data.Topics)
                : new List<CallbackOpportunity>();
            PendingMomentumBonus = data.PendingMomentumBonus;
            DateeOutfitDescription = data.DateeOutfitDescription ?? string.Empty;
            AvatarSpentBackstoryIndices = data.AvatarSpentBackstoryIndices != null
                ? new HashSet<int>(data.AvatarSpentBackstoryIndices)
                : new HashSet<int>(data.SpentBackstoryIndices ?? new HashSet<int>());
            AvatarSpentStakeIndices = data.AvatarSpentStakeIndices != null
                ? new HashSet<int>(data.AvatarSpentStakeIndices)
                : new HashSet<int>(data.SpentStakeIndices ?? new HashSet<int>());
            AvatarPreviousPhase = data.AvatarPreviousPhase ?? data.PreviousPhase;
            AvatarPreviousResolvedIndex = data.AvatarPreviousResolvedIndex != 0 ? data.AvatarPreviousResolvedIndex : data.PreviousResolvedIndex;
            CurrentAvatarRevelationTarget = data.CurrentAvatarRevelationTarget;
            CurrentAvatarCognitiveSubtext = data.CurrentAvatarCognitiveSubtext;
            CurrentAvatarCognitiveSubtextFact = data.CurrentAvatarCognitiveSubtextFact;
            DateeSpentBackstoryIndices = data.DateeSpentBackstoryIndices != null
                ? new HashSet<int>(data.DateeSpentBackstoryIndices)
                : new HashSet<int>();
            DateeSpentStakeIndices = data.DateeSpentStakeIndices != null
                ? new HashSet<int>(data.DateeSpentStakeIndices)
                : new HashSet<int>();
            DateePreviousPhase = data.DateePreviousPhase;
            DateePreviousResolvedIndex = data.DateePreviousResolvedIndex;
            CurrentDateeReactionTarget = data.CurrentDateeReactionTarget;
            LegacyCurrentResolvedTarget = null;
            CurrentDateeCognitiveSubtext = data.CurrentDateeCognitiveSubtext ?? data.CurrentCognitiveSubtext;
            CurrentDateeCognitiveSubtextFact = data.CurrentDateeCognitiveSubtextFact;
            XpLedger = restoredXpLedger;
            SessionHorniness = data.SessionHorniness;
            HorninessRoll = data.HorninessRoll;
            HorninessTimeModifier = data.HorninessTimeModifier;
            PendingCritAdvantage = data.PendingCritAdvantage;
            LastStatUsed = data.LastStatUsed;
            ActiveWeakness = data.ActiveWeakness;
            ActiveTell = data.ActiveTell;
        }

        public void RecordAcceptedDateeEmotionalDirection(CharacterEmotionalDirectionSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            DateeEmotionalDirectionHistory.Add(summary);
            while (DateeEmotionalDirectionHistory.Count > 2)
                DateeEmotionalDirectionHistory.RemoveAt(0);
        }

        private static List<CharacterEmotionalDirectionSummary> BuildDirectionHistory(
            IEnumerable<CharacterEmotionalDirectionSummary>? entries)
        {
            var restored = entries == null
                ? new List<CharacterEmotionalDirectionSummary>()
                : entries.Where(entry => entry != null).ToList();
            while (restored.Count > 2)
                restored.RemoveAt(0);
            return restored;
        }

        private static List<ConversationMessage> BuildConversationHistory(
            IEnumerable<(string Role, string Content)> entries,
            string historyKind)
        {
            var restored = new List<ConversationMessage>();
            int index = 0;
            foreach (var (role, content) in entries)
            {
                if (string.IsNullOrWhiteSpace(role))
                {
                    throw new InvalidOperationException(
                        $"Malformed persisted {historyKind} conversation history at entry {index}: role is empty.");
                }

                try
                {
                    restored.Add(new ConversationMessage(role, content ?? string.Empty));
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException(
                        $"Malformed persisted {historyKind} conversation history at entry {index}: role '{role}' is not supported.",
                        ex);
                }

                index++;
            }

            return restored;
        }
    }
}
