using Pinder.LlmAdapters;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Constructs a fresh, immutable <see cref="GameDefinition"/> that copies the
    /// loaded base definition but injects the harness's per-turn arc text into the
    /// GM base prompt (#1153).
    ///
    /// Before #1153 the arc text overrode the dedicated
    /// <c>conversationArcProgression</c> field, which the prompt builder rendered
    /// as a <c>== CONVERSATION ARC ==</c> section at the tail of the GM base.
    /// The GM base is now a single <see cref="GameDefinition.GameMasterPrompt"/>
    /// field, so we preserve that observable behavior by appending the same
    /// <c>== CONVERSATION ARC ==</c> section to the GM prompt. All other production
    /// fields are preserved so the real prompt builder renders exactly what it
    /// would in a live session, plus our arc slot.
    /// </summary>
    public static class GameDefinitionArcInjector
    {
        public static GameDefinition WithArc(GameDefinition baseDef, string arcText)
        {
            string gmWithArc = baseDef.GameMasterPrompt;
            if (!string.IsNullOrWhiteSpace(arcText))
            {
                gmWithArc = baseDef.GameMasterPrompt.TrimEnd()
                    + "\n\n== CONVERSATION ARC ==\n\n"
                    + arcText.TrimEnd();
            }

            return new GameDefinition(
                name: baseDef.Name,
                gameMasterPrompt: gmWithArc, // ← arc injected into the GM base
                playerAvatarRoleDescription: baseDef.PlayerAvatarRoleDescription,
                dateeRoleDescription: baseDef.DateeRoleDescription,
                improvementPrompt: baseDef.ImprovementPrompt,
                steeringPrompt: baseDef.SteeringPrompt,
                horninessTimeModifiers: baseDef.HorninessTimeModifiers,
                globalDcBias: baseDef.GlobalDcBias,
                maxTurns: baseDef.MaxTurns,
                maxDialogueOptions: baseDef.MaxDialogueOptions,
                maxDeliveryWords: baseDef.MaxDeliveryWords);
        }
    }
}
