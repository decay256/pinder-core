using System;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Minimal IPlayerAgent that replicates the original BestOption logic:
    /// picks the option with the highest effective stat modifier.
    /// Serves as a baseline agent until ScoringPlayerAgent (#347) is available.
    /// </summary>
    public sealed class HighestModAgent : IPlayerAgent
    {
        public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (turn.Options.Length == 0)
                throw new InvalidOperationException("No options available");

            var options = turn.Options;
            var scores = new OptionScore[options.Length];
            int bestIndex = 0;
            float bestScore = float.MinValue;

            for (int i = 0; i < options.Length; i++)
            {
                StatType stat = options[i].Stat;
                int mod = context.PlayerStats.GetEffective(stat);
                int dc = context.OpponentStats.GetDefenceDC(stat);
                int need = dc - mod;
                // need is the minimum d20 roll required to succeed
                // successChance = (21 - need) / 20, clamped to [0, 1]
                float successChance = Math.Max(0.0f, Math.Min(1.0f, (21.0f - need) / 20.0f));

                // Simple score: effective modifier (higher = better)
                float score = mod;

                scores[i] = new OptionScore(
                    optionIndex: i,
                    score: score,
                    successChance: successChance,
                    expectedInterestGain: 0.0f, // not computed for baseline agent
                    bonusesApplied: Array.Empty<string>());

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            string reasoning = $"{StatLabel(options[bestIndex].Stat)} has highest effective modifier ({context.PlayerStats.GetEffective(options[bestIndex].Stat):+#;-#;0}).";

            var decision = new PlayerDecision(bestIndex, reasoning, scores);
            return Task.FromResult(decision);
        }

        private static string StatLabel(StatType s)
        {
            switch (s)
            {
                case StatType.Charm: return "Charm";
                case StatType.Rizz: return "Rizz";
                case StatType.Honesty: return "Honesty";
                case StatType.Chaos: return "Chaos";
                case StatType.Wit: return "Wit";
                case StatType.SelfAwareness: return "SA";
                default: return s.ToString();
            }
        }
    }
}
