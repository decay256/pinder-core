using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;
using Pinder.Core.Interfaces;
using Pinder.Core.Progression;
using Pinder.Core.Rolls;

namespace Pinder.LlmAdapters
{
    public class ConfigurationException : Exception
    {
        public ConfigurationException(string message) : base(message) { }
        public ConfigurationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Data carrier for game-level creative direction.
    /// Parsed from YAML or provided via hardcoded defaults.
    /// </summary>
    public partial class GameDefinition : IRuleResolver
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

        /// <summary>Prompt header and character-data fence names used by the session system prompt builder.</summary>
        public CharacterPromptStructure CharacterPromptStructure { get; }

        /// <summary>Two-stage improvement prompt — appended after initial generation to trigger self-critique and rewrite.</summary>
        public string ImprovementPrompt { get; }

        /// <summary>Steering question prompt template. Placeholders: {player_name}, {datee_name}, {delivered_message}.</summary>
        public string SteeringPrompt { get; }

        public string HorninessPrompt { get; }

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

        /// <summary>Maximum number of turns per session.</summary>
        public int MaxTurns { get; }

        /// <summary>Maximum number of dialogue options per turn.</summary>
        public int MaxDialogueOptions { get; }

        /// <summary>Maximum words allowed for a single delivered message.</summary>
        public int MaxDeliveryWords { get; }

        /// <summary>Time-of-day horniness modifiers loaded from game-definition.yaml.</summary>
        public HorninessTimeModifiers HorninessTimeModifiers { get; }

        /// <summary>Interest penalty multiplier when a trap is active.</summary>
        public double ActiveTrapInterestPenalty { get; }

        public int HungerForIntimacy { get; }

        public int TerrorOfRejection { get; }

        // The 9 new properties
        public IReadOnlyDictionary<string, int> XpFlatAwards { get; }
        public IReadOnlyDictionary<string, int> XpSuccessBase { get; }
        public IReadOnlyDictionary<string, double> XpRiskMultipliers { get; }
        public IReadOnlyDictionary<string, double> XpTerminalMultipliers { get; }
        public IReadOnlyDictionary<string, int> ProgressionXpThresholds { get; }
        public IReadOnlyDictionary<string, int> ProgressionBuildPoints { get; }
        public IReadOnlyDictionary<string, int> ProgressionLevelBonuses { get; }
        public IReadOnlyDictionary<string, int> ProgressionItemSlots { get; }
        public IReadOnlyDictionary<string, int> ProgressionFailurePoolTiers { get; }

        public GameDefinition(
            string name,
            string gameMasterPrompt,
            string playerAvatarRoleDescription,
            string dateeRoleDescription,
            string improvementPrompt = null,
            string steeringPrompt = null,
            string horninessPrompt = null,
            HorninessTimeModifiers horninessTimeModifiers = null,
            int globalDcBias = 0,
            int shadowDcBias = 0,
            int horninessDcBias = 0,
            bool archetypesEnabled = false,
            int maxTurns = 30,
            int maxDialogueOptions = 3,
            int maxDeliveryWords = 80,
            double activeTrapInterestPenalty = -0.25,
            int hungerForIntimacy = 0,
            int terrorOfRejection = 0,
            IReadOnlyDictionary<string, int>? xpFlatAwards = null,
            IReadOnlyDictionary<string, int>? xpSuccessBase = null,
            IReadOnlyDictionary<string, double>? xpRiskMultipliers = null,
            IReadOnlyDictionary<string, double>? xpTerminalMultipliers = null,
            IReadOnlyDictionary<string, int>? progressionXpThresholds = null,
            IReadOnlyDictionary<string, int>? progressionBuildPoints = null,
            IReadOnlyDictionary<string, int>? progressionLevelBonuses = null,
            IReadOnlyDictionary<string, int>? progressionItemSlots = null,
            IReadOnlyDictionary<string, int>? progressionFailurePoolTiers = null,
            CharacterPromptStructure? characterPromptStructure = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            GameMasterPrompt = gameMasterPrompt ?? throw new ArgumentNullException(nameof(gameMasterPrompt));
            PlayerAvatarRoleDescription = playerAvatarRoleDescription ?? throw new ArgumentNullException(nameof(playerAvatarRoleDescription));
            DateeRoleDescription = dateeRoleDescription ?? throw new ArgumentNullException(nameof(dateeRoleDescription));
            CharacterPromptStructure = characterPromptStructure ?? CharacterPromptStructure.PinderDefaults;
            ImprovementPrompt = improvementPrompt ?? "";
            SteeringPrompt = steeringPrompt ?? "";
            HorninessPrompt = horninessPrompt ?? "";
            HorninessTimeModifiers = horninessTimeModifiers;
            GlobalDcBias = globalDcBias;
            ShadowDcBias = shadowDcBias;
            HorninessDcBias = horninessDcBias;
            ArchetypesEnabled = archetypesEnabled;
            MaxTurns = maxTurns > 0 ? maxTurns : 30;
            MaxDialogueOptions = maxDialogueOptions > 0 ? maxDialogueOptions : 3;
            MaxDeliveryWords = maxDeliveryWords > 0 ? maxDeliveryWords : 80;
            ActiveTrapInterestPenalty = activeTrapInterestPenalty;
            HungerForIntimacy = hungerForIntimacy;
            TerrorOfRejection = terrorOfRejection;

            XpFlatAwards = xpFlatAwards ?? new Dictionary<string, int>();
            XpSuccessBase = xpSuccessBase ?? new Dictionary<string, int>();
            XpRiskMultipliers = xpRiskMultipliers ?? new Dictionary<string, double>();
            XpTerminalMultipliers = xpTerminalMultipliers ?? new Dictionary<string, double>();
            ProgressionXpThresholds = progressionXpThresholds ?? new Dictionary<string, int>();
            ProgressionBuildPoints = progressionBuildPoints ?? new Dictionary<string, int>();
            ProgressionLevelBonuses = progressionLevelBonuses ?? new Dictionary<string, int>();
            ProgressionItemSlots = progressionItemSlots ?? new Dictionary<string, int>();
            ProgressionFailurePoolTiers = progressionFailurePoolTiers ?? new Dictionary<string, int>();
        }

        // IRuleResolver Implementation
        public int? GetFailureInterestDelta(int missMargin, int naturalRoll) => null;
        public int? GetSuccessInterestDelta(int beatMargin, int naturalRoll) => null;
        public InterestState? GetInterestState(int interest) => null;
        public int? GetShadowThresholdLevel(int shadowValue) => null;
        public int? GetMomentumBonus(int streak) => null;

        public double? GetRiskTierXpMultiplier(RiskTier riskTier)
        {
            if (XpRiskMultipliers == null)
                throw new ConfigurationException("Risk multipliers configuration is missing.");

            string key = riskTier switch
            {
                RiskTier.Safe => "safe",
                RiskTier.Medium => "medium",
                RiskTier.Hard => "hard",
                RiskTier.Bold => "bold",
                RiskTier.Reckless => "reckless",
                _ => throw new ArgumentOutOfRangeException(nameof(riskTier))
            };

            if (XpRiskMultipliers.TryGetValue(key, out double val)) return val;
            throw new KeyNotFoundException($"Missing risk multiplier for tier {riskTier}");
        }

        public double? GetTerminalOutcomeMultiplier(GameOutcome outcome)
        {
            if (XpTerminalMultipliers == null)
                throw new ConfigurationException("Terminal multipliers configuration is missing.");

            string key = outcome switch
            {
                GameOutcome.DateSecured => "date_secured",
                GameOutcome.Unmatched => "unmatched",
                GameOutcome.Ghosted => "ghosted",
                _ => throw new ArgumentOutOfRangeException(nameof(outcome))
            };

            if (XpTerminalMultipliers.TryGetValue(key, out double val)) return val;
            throw new KeyNotFoundException($"Missing terminal multiplier for outcome {outcome}");
        }

        public int? GetSuccessBaseXp(int dc)
        {
            if (XpSuccessBase == null)
                throw new ConfigurationException("Success base XP configuration is missing.");

            if (!XpSuccessBase.TryGetValue("dc_low_xp", out int dcLowXp))
                throw new KeyNotFoundException("Missing dc_low_xp in xp_success_base config.");
            if (!XpSuccessBase.TryGetValue("dc_mid_xp", out int dcMidXp))
                throw new KeyNotFoundException("Missing dc_mid_xp in xp_success_base config.");
            if (!XpSuccessBase.TryGetValue("dc_high_xp", out int dcHighXp))
                throw new KeyNotFoundException("Missing dc_high_xp in xp_success_base config.");
            var thresholds = GetSuccessDcLabelThresholds().Value;

            if (dc <= thresholds.LowMax) return dcLowXp;
            if (dc <= thresholds.MidMax) return dcMidXp;
            return dcHighXp;
        }

        public SuccessDcLabelThresholds? GetSuccessDcLabelThresholds()
        {
            if (XpSuccessBase == null)
                throw new ConfigurationException("Success base XP configuration is missing.");

            if (!XpSuccessBase.TryGetValue("dc_low_max", out int dcLowMax))
                throw new KeyNotFoundException("Missing dc_low_max in xp_success_base config.");
            if (!XpSuccessBase.TryGetValue("dc_mid_max", out int dcMidMax))
                throw new KeyNotFoundException("Missing dc_mid_max in xp_success_base config.");

            try
            {
                return new SuccessDcLabelThresholds(dcLowMax, dcMidMax);
            }
            catch (ArgumentException ex)
            {
                throw new ConfigurationException("Invalid xp_success_base config: dc_low_max must be less than or equal to dc_mid_max.", ex);
            }
        }

        public int? GetFlatXpAward(string awardType)
        {
            if (XpFlatAwards == null)
                throw new ConfigurationException("Flat awards configuration is missing.");

            string key = awardType.ToLowerInvariant();
            if (!XpFlatAwards.TryGetValue(key, out int value))
                throw new KeyNotFoundException($"Missing flat award config for '{awardType}'.");

            return value;
        }

        public int? GetXpThresholdForLevel(int level)
        {
            if (ProgressionXpThresholds == null)
                throw new ConfigurationException("Progression XP thresholds configuration is missing.");

            string key = level.ToString();
            if (!ProgressionXpThresholds.TryGetValue(key, out int value))
            {
                if (IsPastConfiguredProgressionLevel(ProgressionXpThresholds, level))
                    return null;
                throw new KeyNotFoundException($"Missing progression XP threshold for level {level}.");
            }

            return value;
        }

        public int? GetLevelRollBonus(int level)
        {
            if (ProgressionLevelBonuses == null)
                throw new ConfigurationException("Progression level bonuses configuration is missing.");

            string key = level.ToString();
            if (!ProgressionLevelBonuses.TryGetValue(key, out int value))
                throw new KeyNotFoundException($"Missing progression level bonus for level {level}.");

            return value;
        }

        public int? GetBuildPointsForLevel(int level)
        {
            if (ProgressionBuildPoints == null)
                throw new ConfigurationException("Progression build points configuration is missing.");

            string key = level.ToString();
            if (!ProgressionBuildPoints.TryGetValue(key, out int value))
                throw new KeyNotFoundException($"Missing progression build points for level {level}.");

            return value;
        }

        public int? GetItemSlotsForLevel(int level)
        {
            if (ProgressionItemSlots == null)
                throw new ConfigurationException("Progression item slots configuration is missing.");

            string key = level.ToString();
            if (!ProgressionItemSlots.TryGetValue(key, out int value))
                throw new KeyNotFoundException($"Missing progression item slots for level {level}.");

            return value;
        }

        public int? GetFailurePoolTierMinLevel(string tierName)
        {
            if (ProgressionFailurePoolTiers == null)
                throw new ConfigurationException("Progression failure pool tiers configuration is missing.");

            if (!ProgressionFailurePoolTiers.TryGetValue(tierName, out int value))
                throw new KeyNotFoundException($"Missing failure pool tier min level config for '{tierName}'.");

            return value;
        }

        /// <summary>
        /// Parsed game-definition data is authoritative. Missing rule values must
        /// fail the load or lookup instead of falling back to embedded defaults.
        /// </summary>
        public bool AllowDefaultFallback => false;

        private static bool IsPastConfiguredProgressionLevel(IReadOnlyDictionary<string, int> table, int level)
        {
            int maxLevel = 0;
            foreach (var key in table.Keys)
            {
                if (int.TryParse(key, out int parsed) && parsed > maxLevel)
                    maxLevel = parsed;
            }
            return maxLevel > 0 && level > maxLevel;
        }
    }
}
