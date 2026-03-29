namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Result of a combo detection check. Contains the combo name and its bonus.
    /// </summary>
    public sealed class ComboResult
    {
        /// <summary>Display name of the combo (e.g. "The Setup", "The Recovery").</summary>
        public string Name { get; }

        /// <summary>
        /// Interest bonus to add to the turn's interest delta.
        /// 0 for The Triple (which gives a roll bonus instead).
        /// </summary>
        public int InterestBonus { get; }

        /// <summary>
        /// True only for The Triple — signals that next turn gets +1 roll bonus.
        /// </summary>
        public bool IsTriple { get; }

        public ComboResult(string name, int interestBonus, bool isTriple)
        {
            Name = name ?? throw new System.ArgumentNullException(nameof(name));
            InterestBonus = interestBonus;
            IsTriple = isTriple;
        }
    }
}
