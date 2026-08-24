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
                "strong",
                "fear of being dismissed",
                "reads the moment as requiring a clear boundary",
                "wants to challenge the other person",
                "resists turning the message into an attack",
                "Writing from anger, the avatar becomes direct without turning the message into an attack."));

            string prompt = SessionDocumentBuilder.BuildDialogueOptionsPrompt(context);
            string debugInstruction = SessionDocumentBuilder.BuildDialogueOptionsEngineStateInstruction(context);

            Assert.Contains("AVATAR EMOTIONAL WRITING DIRECTION", prompt);
            Assert.Contains("Primary emotion: anger", prompt);
            Assert.Contains("Writing from anger", prompt);
            Assert.Contains("Primary emotion: anger", debugInstruction);
        }
    }
}
