using System;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Data carrier for game-level creative direction.
    /// Parsed from YAML or provided via hardcoded defaults.
    /// </summary>
    public partial class GameDefinition
    {
        /// <summary>Game name (e.g. "Pinder").</summary>
        public string Name { get; }

        /// <summary>
        /// The complete, pre-assembled Game Master base system prompt (#1153).
        /// This single field carries the entire static GM-base prose that used to
        /// be split across vision / world / doctrine / dramatic-craft / friction /
        /// curiosity / arc / probing sections. It is emitted verbatim as the
        /// shared, session-invariant prefix ahead of the per-character spec block.
        /// </summary>
        public string GameMasterPrompt { get; }

        /// <summary>Player character role description.</summary>
        public string PlayerAvatarRoleDescription { get; }

        /// <summary>Datee character role description.</summary>
        public string DateeRoleDescription { get; }

        /// <summary>Two-stage improvement prompt — appended after initial generation to trigger self-critique and rewrite.</summary>
        public string ImprovementPrompt { get; }

        /// <summary>Steering question prompt template. Placeholders: {player_name}, {datee_name}, {delivered_message}.</summary>
        public string SteeringPrompt { get; }

        /// <summary>Global DC bias applied to all rolls. 0 = standard difficulty. Positive = easier.</summary>
        public int GlobalDcBias { get; }

        /// <summary>DC bias applied to shadow checks. 0 = standard difficulty. Positive = easier/safer (lower trigger chance), negative = harder/more dangerous.</summary>
        public int ShadowDcBias { get; }

        /// <summary>DC bias applied to horniness checks. 0 = standard difficulty. Positive = easier/safer (lower trigger chance), negative = harder/more dangerous.</summary>
        public int HorninessDcBias { get; }

        /// <summary>
        /// When false (default), no archetype content is injected into any prompt surface.
        /// </summary>
        public bool ArchetypesEnabled { get; }

        /// <summary>Maximum number of turns per session. Default 30 if not set in YAML.</summary>
        public int MaxTurns { get; }

        /// <summary>Maximum number of dialogue options per turn. Default 3 if not set in YAML.</summary>
        public int MaxDialogueOptions { get; }

        /// <summary>Maximum words allowed for a single delivered message. Default 80 if not set in YAML.</summary>
        public int MaxDeliveryWords { get; }

        /// <summary>Time-of-day horniness modifiers loaded from game-definition.yaml.</summary>
        public HorninessTimeModifiers HorninessTimeModifiers { get; }

        public GameDefinition(
            string name,
            string gameMasterPrompt,
            string playerAvatarRoleDescription,
            string dateeRoleDescription,
            string improvementPrompt = null,
            string steeringPrompt = null,
            HorninessTimeModifiers horninessTimeModifiers = null,
            int globalDcBias = 0,
            int shadowDcBias = 0,
            int horninessDcBias = 0,
            bool archetypesEnabled = false,
            int maxTurns = 30,
            int maxDialogueOptions = 3,
            int maxDeliveryWords = 80)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            GameMasterPrompt = gameMasterPrompt ?? throw new ArgumentNullException(nameof(gameMasterPrompt));
            PlayerAvatarRoleDescription = playerAvatarRoleDescription ?? throw new ArgumentNullException(nameof(playerAvatarRoleDescription));
            DateeRoleDescription = dateeRoleDescription ?? throw new ArgumentNullException(nameof(dateeRoleDescription));
            ImprovementPrompt = improvementPrompt ?? "";
            SteeringPrompt = steeringPrompt ?? "";
            HorninessTimeModifiers = horninessTimeModifiers;
            GlobalDcBias = globalDcBias;
            ShadowDcBias = shadowDcBias;
            HorninessDcBias = horninessDcBias;
            ArchetypesEnabled = archetypesEnabled;
            MaxTurns = maxTurns > 0 ? maxTurns : 30;
            MaxDialogueOptions = maxDialogueOptions > 0 ? maxDialogueOptions : 3;
            MaxDeliveryWords = maxDeliveryWords > 0 ? maxDeliveryWords : 80;
        }
    }
}
