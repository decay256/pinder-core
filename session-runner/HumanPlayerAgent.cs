using System;
using System.Threading.Tasks;
using Pinder.Core.Conversation;

namespace Pinder.SessionRunner
{
    /// <summary>
    /// Interactive player agent that reads option picks from stdin.
    /// Prompts the human to type A, B, or C and waits for a valid response.
    /// </summary>
    public sealed class HumanPlayerAgent : IPlayerAgent
    {
        public Task<PlayerDecision> DecideAsync(TurnStart turn, PlayerAgentContext context)
        {
            var options = turn.Options;
            char[] letters = { 'A', 'B', 'C', 'D', 'E' };

            // Prompt on stderr so it doesn't get captured in log output
            Console.Error.WriteLine();
            Console.Error.WriteLine($">>> Your pick (A-{letters[options.Length - 1]}): ");

            int chosenIndex = -1;
            while (chosenIndex < 0)
            {
                var line = Console.ReadLine();
                if (line == null)
                {
                    // stdin closed — default to first option
                    chosenIndex = 0;
                    break;
                }

                line = line.Trim().ToUpperInvariant();
                if (line.Length == 1)
                {
                    int idx = Array.IndexOf(letters, line[0]);
                    if (idx >= 0 && idx < options.Length)
                    {
                        chosenIndex = idx;
                        break;
                    }
                }

                Console.Error.WriteLine($"  Invalid input '{line}'. Enter A, B, or C.");
            }

            // Build neutral score breakdown (no EV calculation for human picks)
            var scores = new OptionScore[options.Length];
            for (int i = 0; i < options.Length; i++)
                scores[i] = new OptionScore(
                    optionIndex: i, score: 0f,
                    successChance: 0f, expectedInterestGain: 0f,
                    bonusesApplied: Array.Empty<string>());

            return Task.FromResult(new PlayerDecision(
                chosenIndex,
                $"Human picked: {letters[chosenIndex]}",
                scores));
        }
    }
}
