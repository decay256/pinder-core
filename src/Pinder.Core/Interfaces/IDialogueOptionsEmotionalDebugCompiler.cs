using Pinder.Core.Conversation;

namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Optional adapter capability that projects the exact configured prompt instruction
    /// used to turn emotional state into dialogue options.
    /// </summary>
    public interface IDialogueOptionsEmotionalDebugCompiler
    {
        CharacterEmotionalDebugInfo CompileDialogueOptionsEmotionalDebug(DialogueContext context);
    }
}
