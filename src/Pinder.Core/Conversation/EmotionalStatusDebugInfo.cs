namespace Pinder.Core.Conversation
{
    /// <summary>
    /// The HFI/TOR pair supplied to an LLM phase for trusted diagnostics.
    /// </summary>
    public sealed class EmotionalStatusDebugInfo
    {
        public EmotionalStatusDebugInfo(int hungerForIntimacy, int terrorOfRejection)
        {
            HungerForIntimacy = hungerForIntimacy;
            TerrorOfRejection = terrorOfRejection;
        }

        public int HungerForIntimacy { get; }
        public int TerrorOfRejection { get; }
    }
}
