using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Trusted diagnostic projection of the emotional subsystem for any
    /// character. Direction is shared; surrounding context is optional because
    /// each communication path owns different transition inputs.
    /// </summary>
    public sealed class CharacterEmotionalDebugInfo
    {
        public CharacterEmotionalDebugInfo(
            int hungerForIntimacy,
            int terrorOfRejection,
            CharacterEmotionalDirection? direction = null,
            string? cognitiveSubtext = null,
            string? transitionTarget = null,
            string? transitionStyle = null,
            string? compiledPromptInstruction = null)
        {
            Direction = direction;
            HungerForIntimacy = hungerForIntimacy;
            TerrorOfRejection = terrorOfRejection;
            CognitiveSubtext = cognitiveSubtext;
            TransitionTarget = transitionTarget;
            TransitionStyle = transitionStyle;
            CompiledPromptInstruction = compiledPromptInstruction;
        }

        public CharacterEmotionalDirection? Direction { get; }
        public int HungerForIntimacy { get; }
        public int TerrorOfRejection { get; }
        public string? CognitiveSubtext { get; }
        public string? TransitionTarget { get; }
        public string? TransitionStyle { get; }
        public string? CompiledPromptInstruction { get; }

        public CharacterEmotionalDebugInfo WithStatus(
            int hungerForIntimacy,
            int terrorOfRejection)
        {
            return new CharacterEmotionalDebugInfo(
                hungerForIntimacy,
                terrorOfRejection,
                Direction,
                CognitiveSubtext,
                TransitionTarget,
                TransitionStyle,
                CompiledPromptInstruction);
        }
    }
}
