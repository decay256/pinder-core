using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.Core.Tests
{
    public sealed class CharacterEmotionalDebugInfoTests
    {
        [Fact]
        public void WithStatus_PreservesSharedDirectionAndContext()
        {
            var direction = new CharacterEmotionalDirection(
                "shame",
                "strong and rising",
                "fear of exposure",
                "reads the moment as risky but meaningful",
                "risks a sincere admission",
                "resists retreating into a joke",
                "Writing from shame, risks honesty without disappearing.");
            var original = new CharacterEmotionalDebugInfo(
                0,
                0,
                direction,
                "cognitive pressure",
                "admit the truth",
                "buffered disclosure",
                "compiled instruction");

            CharacterEmotionalDebugInfo enriched = original.WithStatus(9, 14);

            Assert.Same(direction, enriched.Direction);
            Assert.Equal(9, enriched.HungerForIntimacy);
            Assert.Equal(14, enriched.TerrorOfRejection);
            Assert.Equal(original.CognitiveSubtext, enriched.CognitiveSubtext);
            Assert.Equal(original.TransitionTarget, enriched.TransitionTarget);
            Assert.Equal(original.TransitionStyle, enriched.TransitionStyle);
            Assert.Equal(original.CompiledPromptInstruction, enriched.CompiledPromptInstruction);
        }
    }
}
