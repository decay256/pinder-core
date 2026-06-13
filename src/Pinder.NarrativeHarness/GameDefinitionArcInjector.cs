using Pinder.LlmAdapters;

namespace Pinder.Tools.NarrativeHarness
{
    /// <summary>
    /// Constructs a fresh, immutable <see cref="GameDefinition"/> that copies the
    /// loaded base definition but overrides ONLY
    /// <c>conversationArcProgression</c> with the harness's per-turn arc text.
    /// All other production fields are preserved so the real prompt builder
    /// renders exactly what it would in a live session, plus our arc slot.
    /// </summary>
    public static class GameDefinitionArcInjector
    {
        public static GameDefinition WithArc(GameDefinition baseDef, string arcText)
        {
            return new GameDefinition(
                name: baseDef.Name,
                vision: baseDef.Vision,
                worldDescription: baseDef.WorldDescription,
                playerAvatarRoleDescription: baseDef.PlayerAvatarRoleDescription,
                dateeRoleDescription: baseDef.DateeRoleDescription,
                narrativeDoctrine: baseDef.NarrativeDoctrine,
                deliveryRules: baseDef.DeliveryRules,
                dramaticCraft: baseDef.DramaticCraft,
                dateeFriction: baseDef.DateeFriction,
                dateeCuriosity: baseDef.DateeCuriosity,
                conversationArcProgression: arcText, // ← the only override
                playerAvatarProbing: baseDef.PlayerAvatarProbing,
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
