namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Emotional inputs and their exact compiled prompt instruction for trusted diagnostics.
    /// </summary>
    public sealed class EmotionalStatusDebugInfo
    {
        public EmotionalStatusDebugInfo(
            int hungerForIntimacy,
            int terrorOfRejection,
            string? cognitiveSubtext = null,
            string? transitionTarget = null,
            string? transitionStyle = null,
            string? compiledPromptInstruction = null)
        {
            HungerForIntimacy = hungerForIntimacy;
            TerrorOfRejection = terrorOfRejection;
            CognitiveSubtext = cognitiveSubtext;
            TransitionTarget = transitionTarget;
            TransitionStyle = transitionStyle;
            CompiledPromptInstruction = compiledPromptInstruction;
        }

        public int HungerForIntimacy { get; }
        public int TerrorOfRejection { get; }
        public string? CognitiveSubtext { get; }
        public string? TransitionTarget { get; }
        public string? TransitionStyle { get; }
        public string? CompiledPromptInstruction { get; }
    }
}
