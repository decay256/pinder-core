using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Accepted private emotional-director output attached to a turn for trusted
    /// diagnostic projections. This is never part of semantic conversation history.
    /// </summary>
    public sealed class EmotionalReactionDebugInfo
    {
        public EmotionalReactionDebugInfo(
            string primaryEmotion,
            string intensity,
            string underlyingFeeling,
            string interpretation,
            string impulse,
            string restraint,
            string responsePosture,
            string? compiledPromptInstruction = null)
        {
            PrimaryEmotion = primaryEmotion ?? throw new ArgumentNullException(nameof(primaryEmotion));
            Intensity = intensity ?? throw new ArgumentNullException(nameof(intensity));
            UnderlyingFeeling = underlyingFeeling ?? throw new ArgumentNullException(nameof(underlyingFeeling));
            Interpretation = interpretation ?? throw new ArgumentNullException(nameof(interpretation));
            Impulse = impulse ?? throw new ArgumentNullException(nameof(impulse));
            Restraint = restraint ?? throw new ArgumentNullException(nameof(restraint));
            ResponsePosture = responsePosture ?? throw new ArgumentNullException(nameof(responsePosture));
            CompiledPromptInstruction = compiledPromptInstruction;
        }

        public string PrimaryEmotion { get; }
        public string Intensity { get; }
        public string UnderlyingFeeling { get; }
        public string Interpretation { get; }
        public string Impulse { get; }
        public string Restraint { get; }
        public string ResponsePosture { get; }
        public string? CompiledPromptInstruction { get; }
    }
}
