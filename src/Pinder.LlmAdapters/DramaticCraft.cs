using System.Text;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Configurable dramatic craft rules governing emotional investment, tension, and payoff.
    /// </summary>
    public sealed class DramaticCraft
    {
        public string Goal { get; }
        public string OpponentWant { get; }
        public string RevelationBudget { get; }
        public string DirectnessDial { get; }
        public string FailureCost { get; }
        public string EarningTheClose { get; }

        public DramaticCraft(string goal, string opponentWant, string revelationBudget,
            string directnessDial, string failureCost, string earningTheClose)
        {
            Goal = goal ?? "";
            OpponentWant = opponentWant ?? "";
            RevelationBudget = revelationBudget ?? "";
            DirectnessDial = directnessDial ?? "";
            FailureCost = failureCost ?? "";
            EarningTheClose = earningTheClose ?? "";
        }

        public string BuildSection()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(Goal)) { sb.AppendLine("DRAMATIC GOAL"); sb.AppendLine(Goal.TrimEnd()); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(OpponentWant)) { sb.AppendLine("OPPONENT'S WANT"); sb.AppendLine(OpponentWant.TrimEnd()); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(RevelationBudget)) { sb.AppendLine("REVELATION BUDGET"); sb.AppendLine(RevelationBudget.TrimEnd()); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(DirectnessDial)) { sb.AppendLine("DIRECTNESS CALIBRATION"); sb.AppendLine(DirectnessDial.TrimEnd()); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(FailureCost)) { sb.AppendLine("FAILURE COST"); sb.AppendLine(FailureCost.TrimEnd()); sb.AppendLine(); }
            if (!string.IsNullOrWhiteSpace(EarningTheClose)) { sb.AppendLine("EARNING THE CLOSE"); sb.AppendLine(EarningTheClose.TrimEnd()); }
            return sb.ToString().TrimEnd();
        }
    }
}
