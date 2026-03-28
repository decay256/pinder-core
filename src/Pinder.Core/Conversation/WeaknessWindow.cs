using Pinder.Core.Stats;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// A temporary weakness window opened by the opponent's response.
    /// Reduces the DC for a specific defending stat.
    /// Stub type — feature PRs will flesh out usage.
    /// </summary>
    public sealed class WeaknessWindow
    {
        /// <summary>The defending stat whose DC is reduced.</summary>
        public StatType DefendingStat { get; }

        /// <summary>How much the DC is reduced by.</summary>
        public int DcReduction { get; }

        public WeaknessWindow(StatType defendingStat, int dcReduction)
        {
            DefendingStat = defendingStat;
            DcReduction = dcReduction;
        }
    }
}
