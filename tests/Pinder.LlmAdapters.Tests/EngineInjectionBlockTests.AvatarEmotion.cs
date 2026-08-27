using Pinder.Core.Conversation;
using Xunit;

namespace Pinder.LlmAdapters.Tests
{
    public partial class EngineInjectionBlockTests
    {
        [Fact]
        public void DialogueOptionsPrompt_IncludesAvatarPrimaryEmotionAndResponsePosture()
        {
            DialogueContext context = MakeDialogueContext();
            context.ApplyAvatarEmotionalDirection(new CharacterEmotionalDirection(
                "anger",
                "fear",
                "controlled",
                5,
                "escalating",
                "fear of being dismissed",
                "reads the moment as requiring a clear boundary",
                "wants to challenge the other person",
                "resists turning the message into an attack",
                "Writing from anger, the avatar becomes direct without turning the message into an attack."));

            string prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);
            string debugInstruction = SessionDocumentBuilder.BuildDialogueOptionsEngineStateInstruction(context);

            Assert.Contains("AVATAR EMOTIONAL WRITING DIRECTION", prompt);
            Assert.Contains("Primary emotion: anger", prompt);
            Assert.Contains("Secondary emotion: fear", prompt);
            Assert.Contains("Regulatory state: controlled", prompt);
            Assert.Contains("Activation: 5", prompt);
            Assert.Contains("Trajectory: escalating", prompt);
            Assert.Contains("Core threat/desire: fear of being dismissed", prompt);
            Assert.Contains("Interpretation: reads the moment as requiring a clear boundary", prompt);
            Assert.Contains("Impulse: wants to challenge the other person", prompt);
            Assert.Contains("Restraint: resists turning the message into an attack", prompt);
            Assert.Contains("Response posture: Writing from anger", prompt);
            Assert.Contains("Primary emotion: anger", debugInstruction);
            Assert.DoesNotContain("{", debugInstruction);
        }
    }
}
