using System;
using System.Collections.Generic;
using System.Linq;
using Pinder.Core.Characters;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Represents the target selected for an emotion stem revelation.
    /// </summary>
    public struct ResolvedRevelationTarget 
    {
        public string Registry; // "BACKSTORY" or "STAKE"
        public int Index;
        public string Field;    // "BIO_LIE" / "TRAGIC_REALITY" / "STAKE_LINE"
        public string Manner;   // "CURATED" / "PRE_EMPTIVE" / "SINCERE" / "LEAKING" / "CURATED_BUFFER" / "DEFENSIVE_EVASION" / "INTIMATE_BREAKTHROUGH" / "TRAUMATIC_LEAKAGE"
        public string StemText;
        public string TransitionStyle;
    }

    /// <summary>
    /// Represents stats for a participant in the conversation.
    /// </summary>
    public class ParticipantStats 
    {
        public int BaseHFI { get; set; }
        public int BaseTOR { get; set; }
        public int WinStreak { get; set; }
        public int LossStreak { get; set; }
    }

    /// <summary>
    /// Holds the state of the conversation used to resolve an emotion stem.
    /// </summary>
    public class ConversationState
    {
        public int TurnCount { get; set; }
        public int InterestScore { get; set; }
        public string? PreviousPhase { get; set; }
        public List<string> ActiveTraps { get; set; } = new List<string>();
        public HashSet<int> SpentBackstoryIndices { get; set; } = new HashSet<int>();
        public HashSet<int> SpentStakeIndices { get; set; } = new HashSet<int>();
        public ParticipantStats PlayerStats { get; set; } = new ParticipantStats();
        public ParticipantStats DateeStats { get; set; } = new ParticipantStats();
        public int PreviousResolvedIndex { get; set; }
    }

    public sealed class EmotionStemSelectionRules
    {
        public const int DeterministicSeedSalt = 42;
        public const string MacroPhase1 = "MacroPhase1";
        public const string MacroPhase2 = "MacroPhase2";
        public const string MacroPhase3 = "MacroPhase3";
        public const string BackstoryRegistry = "BACKSTORY";
        public const string StakeRegistry = "STAKE";
        public const int BackstoryRegistrySize = 20;
        public const int StakeRegistrySize = 15;

        public static EmotionStemSelectionRules Default { get; } = new EmotionStemSelectionRules();

        public int MacroPhase2TurnThreshold { get; }
        public int MacroPhase2InterestThreshold { get; }
        public int MacroPhase3TurnThreshold { get; }
        public int MacroPhase3InterestThreshold { get; }
        public int MacroPhase1To2HysteresisMinTurn { get; }
        public int MacroPhase1To2HysteresisMaxTurn { get; }
        public int MacroPhase1To2HysteresisMinInterest { get; }
        public int MacroPhase1To2HysteresisMaxInterest { get; }
        public int MacroPhase2FavoredBackstoryIndex { get; }
        public int MacroPhase3FavoredStakeIndex { get; }
        public double FavoredTargetWeight { get; }
        public int StatMin { get; }
        public int StatMax { get; }
        public int StatMidpoint { get; }
        public int ActiveTrapStatAdjustment { get; }

        public EmotionStemSelectionRules(
            int macroPhase2TurnThreshold = 4,
            int macroPhase2InterestThreshold = 40,
            int macroPhase3TurnThreshold = 8,
            int macroPhase3InterestThreshold = 70,
            int macroPhase1To2HysteresisMinTurn = 4,
            int macroPhase1To2HysteresisMaxTurn = 5,
            int macroPhase1To2HysteresisMinInterest = 40,
            int macroPhase1To2HysteresisMaxInterest = 55,
            int macroPhase2FavoredBackstoryIndex = 13,
            int macroPhase3FavoredStakeIndex = 14,
            double favoredTargetWeight = 5.0,
            int statMin = 0,
            int statMax = 20,
            int statMidpoint = 10,
            int activeTrapStatAdjustment = 5)
        {
            MacroPhase2TurnThreshold = macroPhase2TurnThreshold;
            MacroPhase2InterestThreshold = macroPhase2InterestThreshold;
            MacroPhase3TurnThreshold = macroPhase3TurnThreshold;
            MacroPhase3InterestThreshold = macroPhase3InterestThreshold;
            MacroPhase1To2HysteresisMinTurn = macroPhase1To2HysteresisMinTurn;
            MacroPhase1To2HysteresisMaxTurn = macroPhase1To2HysteresisMaxTurn;
            MacroPhase1To2HysteresisMinInterest = macroPhase1To2HysteresisMinInterest;
            MacroPhase1To2HysteresisMaxInterest = macroPhase1To2HysteresisMaxInterest;
            MacroPhase2FavoredBackstoryIndex = macroPhase2FavoredBackstoryIndex;
            MacroPhase3FavoredStakeIndex = macroPhase3FavoredStakeIndex;
            FavoredTargetWeight = favoredTargetWeight;
            StatMin = statMin;
            StatMax = statMax;
            StatMidpoint = statMidpoint;
            ActiveTrapStatAdjustment = activeTrapStatAdjustment;
        }
    }

    /// <summary>
    /// Selects an emotion stem and determines the appropriate posture manner 
    /// based on conversation state, HFI/TOR statistics, and active traps.
    /// Manages the 20 Backstory Fields, 15 Psychological Stakes, and Crack and Slip philosophy
    /// where character cracking/slipping controls are bounded strictly.
    /// </summary>
    public class EmotionStemSelector
    {
        private readonly Random _random;
        private readonly EmotionStemSelectionRules _rules;

        public EmotionStemSelector(int rngSeed, EmotionStemSelectionRules? rules = null)
        {
            _random = new Random(rngSeed);
            _rules = rules ?? EmotionStemSelectionRules.Default;
        }

        public ResolvedRevelationTarget Resolve(ConversationState state)
        {
            // 1. Derive Phase
            string currentPhase = EmotionStemSelectionRules.MacroPhase1;
            if (state.TurnCount >= _rules.MacroPhase3TurnThreshold ||
                state.InterestScore >= _rules.MacroPhase3InterestThreshold)
            {
                currentPhase = EmotionStemSelectionRules.MacroPhase3;
            }
            else if (state.TurnCount >= _rules.MacroPhase2TurnThreshold ||
                state.InterestScore >= _rules.MacroPhase2InterestThreshold)
            {
                currentPhase = EmotionStemSelectionRules.MacroPhase2;
            }

            // Apply Hysteresis
            if (state.PreviousPhase == EmotionStemSelectionRules.MacroPhase1 &&
                currentPhase == EmotionStemSelectionRules.MacroPhase2)
            {
                bool inHysteresisTurn =
                    state.TurnCount >= _rules.MacroPhase1To2HysteresisMinTurn &&
                    state.TurnCount <= _rules.MacroPhase1To2HysteresisMaxTurn;
                bool inHysteresisInterest =
                    state.InterestScore >= _rules.MacroPhase1To2HysteresisMinInterest &&
                    state.InterestScore <= _rules.MacroPhase1To2HysteresisMaxInterest;
                if (inHysteresisTurn || inHysteresisInterest)
                {
                    currentPhase = EmotionStemSelectionRules.MacroPhase1;
                }
            }

            string registry = currentPhase == EmotionStemSelectionRules.MacroPhase3
                ? EmotionStemSelectionRules.StakeRegistry
                : EmotionStemSelectionRules.BackstoryRegistry;

            // 2. Filter Registry and apply Affinity Scorer
            var availableIndices = new List<int>();
            var spentIndices = registry == EmotionStemSelectionRules.BackstoryRegistry
                ? state.SpentBackstoryIndices
                : state.SpentStakeIndices;
            
            // Bounds limit based on registry (20 Backstory Fields, 15 Psychological Stakes, Crack and Slip philosophy)
            int maxLimit = registry == EmotionStemSelectionRules.BackstoryRegistry
                ? EmotionStemSelectionRules.BackstoryRegistrySize
                : EmotionStemSelectionRules.StakeRegistrySize;
            for (int i = 0; i < maxLimit; i++)
            {
                if (!spentIndices.Contains(i))
                {
                    availableIndices.Add(i);
                }
            }

            int selectedIndex;
            if (availableIndices.Count == 0)
            {
                // Spent Exhaustion fallback
                selectedIndex = state.PreviousResolvedIndex;
            }
            else
            {
                double totalWeight = 0;
                var weights = new Dictionary<int, double>();
                foreach (var index in availableIndices)
                {
                    double weight = 1.0;
                    if (currentPhase == EmotionStemSelectionRules.MacroPhase2 &&
                        index == _rules.MacroPhase2FavoredBackstoryIndex)
                    {
                        weight = _rules.FavoredTargetWeight;
                    }
                    else if (currentPhase == EmotionStemSelectionRules.MacroPhase3 &&
                        index == _rules.MacroPhase3FavoredStakeIndex)
                    {
                        weight = _rules.FavoredTargetWeight;
                    }
                    weights[index] = weight;
                    totalWeight += weight;
                }

                double roll = _random.NextDouble() * totalWeight;
                double cumulative = 0;
                selectedIndex = availableIndices.Last(); // fallback to last
                foreach (var kvp in weights)
                {
                    cumulative += kvp.Value;
                    if (roll < cumulative)
                    {
                        selectedIndex = kvp.Key;
                        break;
                    }
                }

                if (selectedIndex < 0 || selectedIndex >= maxLimit)
                {
                    selectedIndex = Math.Max(0, Math.Min(maxLimit - 1, selectedIndex));
                }
            }

            // 3. Symmetrical Quadrant (HFI/TOR)
            int playerHfi = ClampStat(state.PlayerStats?.BaseHFI ?? 0);
            int playerTor = ClampStat(state.PlayerStats?.BaseTOR ?? 0);
            int dateeHfi = ClampStat(state.DateeStats?.BaseHFI ?? 0);
            int dateeTor = ClampStat(state.DateeStats?.BaseTOR ?? 0);

            int avgHfi = (playerHfi + dateeHfi) / 2;
            int avgTor = (playerTor + dateeTor) / 2;

            if (state.ActiveTraps != null && state.ActiveTraps.Count > 0)
            {
                avgHfi = ClampStat(avgHfi - _rules.ActiveTrapStatAdjustment);
                avgTor = ClampStat(avgTor + _rules.ActiveTrapStatAdjustment);
            }

            // Map HFI/TOR to quadrant:
            // Q1 (HFI < 10, TOR < 10) -> "CURATED_BUFFER"
            // Q2 (HFI < 10, TOR >= 10) -> "DEFENSIVE_EVASION"
            // Q3 (HFI >= 10, TOR >= 10) -> "INTIMATE_BREAKTHROUGH"
            // Q4 (HFI >= 10, TOR < 10) -> "TRAUMATIC_LEAKAGE"
            string manner;
            if (avgHfi < _rules.StatMidpoint && avgTor < _rules.StatMidpoint)
                manner = "CURATED_BUFFER";
            else if (avgHfi < _rules.StatMidpoint && avgTor >= _rules.StatMidpoint)
                manner = "DEFENSIVE_EVASION";
            else if (avgHfi >= _rules.StatMidpoint && avgTor >= _rules.StatMidpoint)
                manner = "INTIMATE_BREAKTHROUGH";
            else
                manner = "TRAUMATIC_LEAKAGE";

            // Active Traps Override
            if (state.ActiveTraps != null && state.ActiveTraps.Count > 0)
            {
                manner = "DEFENSIVE_EVASION"; // Force override if traps are present
            }

            return new ResolvedRevelationTarget
            {
                Registry = registry,
                Index = selectedIndex,
                Manner = manner,
                Field = (currentPhase == EmotionStemSelectionRules.MacroPhase1) ? "BIO_LIE" :
                        (currentPhase == EmotionStemSelectionRules.MacroPhase2) ? "TRAGIC_REALITY" : "STAKE_LINE",
                StemText = string.Empty,
                TransitionStyle = ResolveTransitionStyle(manner)
            };
        }

        private int ClampStat(int value)
        {
            return Math.Max(_rules.StatMin, Math.Min(_rules.StatMax, value));
        }

        public static ResolvedRevelationTarget Hydrate(
            ResolvedRevelationTarget target,
            IReadOnlyDictionary<string, BackstoryFact>? backstory,
            IReadOnlyList<string>? stakeLines)
        {
            if (target.Registry == "BACKSTORY")
            {
                if (target.Index < 0 || target.Index >= BackstoryValidator.RequiredCategories.Count)
                    throw new InvalidOperationException($"Backstory target index {target.Index} is out of range.");

                string category = BackstoryValidator.RequiredCategories[target.Index];
                if (backstory == null || !backstory.TryGetValue(category, out var fact) || fact == null)
                    throw new InvalidOperationException($"Backstory target '{category}' is missing from the player profile.");

                target.StemText = target.Field == "BIO_LIE" ? fact.BioLie : fact.TragicReality;
            }
            else if (target.Registry == "STAKE")
            {
                if (stakeLines == null || target.Index < 0 || target.Index >= stakeLines.Count)
                    throw new InvalidOperationException($"Stake target index {target.Index} is missing from the player profile.");
                target.StemText = stakeLines[target.Index];
            }
            else
            {
                throw new InvalidOperationException($"Unknown revelation registry '{target.Registry}'.");
            }

            if (string.IsNullOrWhiteSpace(target.StemText))
                throw new InvalidOperationException($"Resolved {target.Registry} target {target.Index} contains no text.");
            return target;
        }

        private static string ResolveTransitionStyle(string manner) => manner switch
        {
            "CURATED_BUFFER" => "Keep the disclosure controlled and carefully buffered, revealing only enough to invite a response.",
            "DEFENSIVE_EVASION" => "Approach the disclosure guardedly: acknowledge it, then deflect without making the transition feel abrupt.",
            "INTIMATE_BREAKTHROUGH" => "Let the disclosure arrive as a sincere, unexpectedly intimate breakthrough.",
            "TRAUMATIC_LEAKAGE" => "Let the disclosure slip out involuntarily, with emotion showing before the speaker can contain it.",
            _ => "Move into the disclosure naturally and keep it emotionally consistent with the conversation."
        };
    }
}
