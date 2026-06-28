using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// Selects an emotion stem and determines the appropriate posture manner 
    /// based on conversation state, HFI/TOR statistics, and active traps.
    /// </summary>
    public class EmotionStemSelector
    {
        private readonly Random _random;

        public EmotionStemSelector(int rngSeed) 
        {
            _random = new Random(rngSeed);
        }

        public ResolvedRevelationTarget Resolve(ConversationState state)
        {
            // 1. Derive Phase
            string currentPhase = "MacroPhase1";
            if (state.TurnCount >= 8 || state.InterestScore >= 70)
            {
                currentPhase = "MacroPhase3";
            }
            else if (state.TurnCount >= 4 || state.InterestScore >= 40)
            {
                currentPhase = "MacroPhase2";
            }

            // Apply Hysteresis
            if (state.PreviousPhase == "MacroPhase1" && currentPhase == "MacroPhase2")
            {
                bool inHysteresisTurn = state.TurnCount == 4 || state.TurnCount == 5;
                bool inHysteresisInterest = state.InterestScore >= 40 && state.InterestScore <= 55;
                if (inHysteresisTurn || inHysteresisInterest)
                {
                    currentPhase = "MacroPhase1";
                }
            }

            string registry = currentPhase == "MacroPhase1" ? "BACKSTORY" : "STAKE";

            // 2. Filter Registry and apply Affinity Scorer
            var availableIndices = new List<int>();
            var spentIndices = registry == "BACKSTORY" ? state.SpentBackstoryIndices : state.SpentStakeIndices;
            
            for (int i = 0; i <= 20; i++)
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
                    if (currentPhase == "MacroPhase2" && index == 13)
                    {
                        weight = 5.0;
                    }
                    else if (currentPhase == "MacroPhase3" && index == 19)
                    {
                        weight = 5.0;
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
            }

            // 3. Symmetrical Quadrant (HFI/TOR)
            int playerHfi = Math.Max(0, Math.Min(20, state.PlayerStats?.BaseHFI ?? 0));
            int playerTor = Math.Max(0, Math.Min(20, state.PlayerStats?.BaseTOR ?? 0));
            int dateeHfi = Math.Max(0, Math.Min(20, state.DateeStats?.BaseHFI ?? 0));
            int dateeTor = Math.Max(0, Math.Min(20, state.DateeStats?.BaseTOR ?? 0));

            int avgHfi = (playerHfi + dateeHfi) / 2;
            int avgTor = (playerTor + dateeTor) / 2;

            if (state.ActiveTraps != null && state.ActiveTraps.Count > 0)
            {
                avgHfi = Math.Max(0, Math.Min(20, avgHfi - 5));
                avgTor = Math.Max(0, Math.Min(20, avgTor + 5));
            }

            // Midpoint is 10. Map to quadrant:
            // Q1 (HFI < 10, TOR < 10) -> "CURATED_BUFFER"
            // Q2 (HFI < 10, TOR >= 10) -> "DEFENSIVE_EVASION"
            // Q3 (HFI >= 10, TOR >= 10) -> "INTIMATE_BREAKTHROUGH"
            // Q4 (HFI >= 10, TOR < 10) -> "TRAUMATIC_LEAKAGE"
            string manner;
            if (avgHfi < 10 && avgTor < 10)
                manner = "CURATED_BUFFER";
            else if (avgHfi < 10 && avgTor >= 10)
                manner = "DEFENSIVE_EVASION";
            else if (avgHfi >= 10 && avgTor >= 10)
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
                Field = registry == "BACKSTORY" ? "BIO_LIE" : "STAKE_LINE",
                StemText = "Resolved stem text",
                TransitionStyle = "Smooth"
            };
        }
    }
}
