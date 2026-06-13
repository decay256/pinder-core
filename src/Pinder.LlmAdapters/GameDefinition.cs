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

        /// <summary>Creative brief: what the game is, tone, goal.</summary>
        public string Vision { get; }

        /// <summary>World setting: texting psychology, medium rules.</summary>
        public string WorldDescription { get; }

        /// <summary>Player character role description.</summary>
        public string PlayerAvatarRoleDescription { get; }

        /// <summary>Datee character role description.</summary>
        public string DateeRoleDescription { get; }

        /// <summary>Combined narrative doctrine: meta contract, writing rules, texting
        /// psychology, and revelation-over-statement — always assembled together.</summary>
        public string NarrativeDoctrine { get; }

        /// <summary>Datee friction / resistance framing.</summary>
        public string DateeFriction { get; }

        /// <summary>Datee curiosity / reciprocal questions direction.</summary>
        public string DateeCuriosity { get; }

        /// <summary>Conversation arc / topic progression guidance.</summary>
        public string ConversationArcProgression { get; }

        /// <summary>Player options probing directive — biographical follow-up instruction.</summary>
        public string PlayerAvatarProbing { get; }

        /// <summary>Two-stage improvement prompt — appended after initial generation to trigger self-critique and rewrite.</summary>
        public string ImprovementPrompt { get; }

        /// <summary>Steering question prompt template. Placeholders: {player_name}, {datee_name}, {delivered_message}.</summary>
        public string SteeringPrompt { get; }

        /// <summary>Configurable delivery prompt rules, or null for hardcoded defaults.</summary>
        public DeliveryRules DeliveryRules { get; }

        /// <summary>Configurable dramatic craft rules, or null for hardcoded defaults.</summary>
        public DramaticCraft DramaticCraft { get; }

        /// <summary>Global DC bias applied to all rolls. 0 = standard difficulty. Positive = harder.</summary>
        public int GlobalDcBias { get; }

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
            string vision,
            string worldDescription,
            string playerAvatarRoleDescription,
            string dateeRoleDescription,
            string narrativeDoctrine,
            DeliveryRules deliveryRules = null,
            DramaticCraft dramaticCraft = null,
            string dateeFriction = null,
            string dateeCuriosity = null,
            string conversationArcProgression = null,
            string playerAvatarProbing = null,
            string improvementPrompt = null,
            string steeringPrompt = null,
            HorninessTimeModifiers horninessTimeModifiers = null,
            int globalDcBias = 0,
            int maxTurns = 30,
            int maxDialogueOptions = 3,
            int maxDeliveryWords = 80)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Vision = vision ?? throw new ArgumentNullException(nameof(vision));
            WorldDescription = worldDescription ?? throw new ArgumentNullException(nameof(worldDescription));
            PlayerAvatarRoleDescription = playerAvatarRoleDescription ?? throw new ArgumentNullException(nameof(playerAvatarRoleDescription));
            DateeRoleDescription = dateeRoleDescription ?? throw new ArgumentNullException(nameof(dateeRoleDescription));
            NarrativeDoctrine = narrativeDoctrine ?? throw new ArgumentNullException(nameof(narrativeDoctrine));
            DateeFriction = dateeFriction ?? "";
            DateeCuriosity = dateeCuriosity ?? "";
            ConversationArcProgression = conversationArcProgression ?? "";
            PlayerAvatarProbing = playerAvatarProbing ?? "";
            ImprovementPrompt = improvementPrompt ?? "";
            SteeringPrompt = steeringPrompt ?? "";
            DeliveryRules = deliveryRules;
            DramaticCraft = dramaticCraft;
            HorninessTimeModifiers = horninessTimeModifiers;
            GlobalDcBias = globalDcBias;
            MaxTurns = maxTurns > 0 ? maxTurns : 30;
            MaxDialogueOptions = maxDialogueOptions > 0 ? maxDialogueOptions : 3;
            MaxDeliveryWords = maxDeliveryWords > 0 ? maxDeliveryWords : 80;
        }
    }
}
