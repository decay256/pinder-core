namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Summary of an active trap for display purposes.
    /// </summary>
    public sealed class TrapDetail
    {
        /// <summary>Trap identifier (e.g. "Creep").</summary>
        public string Name { get; }

        /// <summary>Stat the trap is associated with (e.g. "RIZZ").</summary>
        public string Stat { get; }

        /// <summary>Turns remaining before the trap expires.</summary>
        public int TurnsRemaining { get; }

        /// <summary>Human-readable penalty description (e.g. "stat penalty -2").</summary>
        public string PenaltyDescription { get; }

        public TrapDetail(string name, string stat, int turnsRemaining, string penaltyDescription)
        {
            Name = name;
            Stat = stat;
            TurnsRemaining = turnsRemaining;
            PenaltyDescription = penaltyDescription;
        }
    }
}
