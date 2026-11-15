using System;
using System.Threading.Tasks;
using Pinder.Core.Conversation;
using Pinder.Core.Stats;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Picks uniformly among the offered options with its own seeded generator. It stands in
    /// for a human player whose choice nobody can predict, so a writing pipeline can be tested
    /// against many different choice paths (the tone lab's live mode uses it). Selected with
    /// <c>--agent random</c>; the seed comes from <c>--seed</c>.
    /// </summary>
    public sealed class RandomPlayerAgent : IPlayerAgent
    {
        private readonly Random _rng;

        public RandomPlayerAgent(int seed)
        {
            _rng = new Random(seed);
        }

        public string LastExplanation { get; private set; } = "";

        public ScoringMode ScoringMode => ScoringMode.Heuristic;
        public string MechanicsSource => "heuristic:RandomPlayerAgent seeded uniform choice";

        public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
        {
            if (turn == null) throw new ArgumentNullException(nameof(turn));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (turn.Options.Length == 0)
                throw new InvalidOperationException("No options available");

            var options = turn.Options;
            var scores = new OptionScore[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                StatType stat = options[i].Stat;
                int need = context.DateeStats.GetDefenceDC(stat) - context.PlayerStats.GetEffective(stat);
                float successChance = Math.Max(0.0f, Math.Min(1.0f, (21.0f - need) / 20.0f));
                scores[i] = new OptionScore(
                    optionIndex: i,
                    score: 0.0f,
                    successChance: successChance,
                    expectedInterestGain: 0.0f,
                    bonusesApplied: Array.Empty<string>());
            }

            int pick = _rng.Next(options.Length);
            LastExplanation = $"random pick {pick + 1} of {options.Length}";
            return Task.FromResult(new PlayerDecision(pick, LastExplanation, scores));
        }
    }
}
