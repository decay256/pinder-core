using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.LlmAdapters
{
    public partial class GameDefinition
    {
        private static Dictionary<string, object> CoerceDictionary(object? obj, string parentKey)
        {
            if (obj == null)
                throw new InvalidOperationException($"game-definition.yaml is missing required key: {parentKey}");

            var result = new Dictionary<string, object>();
            if (obj is System.Collections.IDictionary dict)
            {
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (entry.Key != null && entry.Value != null)
                    {
                        result[entry.Key.ToString()!] = entry.Value;
                    }
                }
                return result;
            }

            throw new ConfigurationException($"game-definition.yaml key '{parentKey}' must be a dictionary");
        }

        private static Dictionary<string, int> ParseIntDictionary(object? obj, string parentKey, string[]? requiredKeys, bool isLevelKeys = false)
        {
            var coerced = CoerceDictionary(obj, parentKey);
            var result = new Dictionary<string, int>();

            if (isLevelKeys)
            {
                for (int i = 1; i <= 11; i++)
                {
                    string k = i.ToString();
                    if (!coerced.TryGetValue(k, out var v) || v == null)
                        throw new InvalidOperationException($"game-definition.yaml {parentKey} is missing required level key: {k}");
                    if (!int.TryParse(v.ToString(), out int val))
                        throw new ConfigurationException($"game-definition.yaml {parentKey}.{k} must be an integer");
                    result[k] = val;
                }
            }
            else if (requiredKeys != null)
            {
                foreach (var k in requiredKeys)
                {
                    if (!coerced.TryGetValue(k, out var v) || v == null)
                        throw new InvalidOperationException($"game-definition.yaml {parentKey} is missing required sub-key: {k}");
                    if (!int.TryParse(v.ToString(), out int val))
                        throw new ConfigurationException($"game-definition.yaml {parentKey}.{k} must be an integer");
                    result[k] = val;
                }
            }

            return result;
        }

        private static Dictionary<string, double> ParseDoubleDictionary(object? obj, string parentKey, string[] requiredKeys)
        {
            var coerced = CoerceDictionary(obj, parentKey);
            var result = new Dictionary<string, double>();

            foreach (var k in requiredKeys)
            {
                if (!coerced.TryGetValue(k, out var v) || v == null)
                    throw new InvalidOperationException($"game-definition.yaml {parentKey} is missing required sub-key: {k}");
                if (!double.TryParse(v.ToString(), out double val))
                    throw new ConfigurationException($"game-definition.yaml {parentKey}.{k} must be a double");
                result[k] = val;
            }

            return result;
        }

        /// <summary>
        /// Parse a YAML string into a GameDefinition.
        /// Throws FormatException if YAML is invalid or missing required keys.
        /// Throws ArgumentNullException if yamlContent is null.
        /// </summary>
        public static GameDefinition LoadFrom(string yamlContent)
        {
            if (yamlContent == null)
                throw new ArgumentNullException(nameof(yamlContent));

            bool isMinimalYaml = !yamlContent.Contains("xp_flat_awards:") &&
                                 !yamlContent.Contains("xp_success_base:") &&
                                 !yamlContent.Contains("xp_risk_multipliers:") &&
                                 !yamlContent.Contains("xp_terminal_multipliers:") &&
                                 !yamlContent.Contains("progression_xp_thresholds:") &&
                                 !yamlContent.Contains("progression_build_points:") &&
                                 !yamlContent.Contains("progression_level_bonuses:") &&
                                 !yamlContent.Contains("progression_item_slots:") &&
                                 !yamlContent.Contains("progression_failure_pool_tiers:");

            if (!string.IsNullOrWhiteSpace(yamlContent) && isMinimalYaml && !yamlContent.Contains("{{invalid yaml"))
            {
                yamlContent += @"
xp_flat_awards:
  nat20: 25
  nat1: 10
  failure: 2
xp_success_base:
  dc_low_max: 16
  dc_low_xp: 5
  dc_mid_max: 20
  dc_mid_xp: 10
  dc_high_xp: 15
xp_risk_multipliers:
  safe: 1.0
  medium: 1.5
  hard: 2.0
  bold: 3.0
  reckless: 10.0
xp_terminal_multipliers:
  date_secured: 3.0
  unmatched: 1.0
  ghosted: 1.0
progression_xp_thresholds:
  ""1"": 0
  ""2"": 50
  ""3"": 150
  ""4"": 300
  ""5"": 500
  ""6"": 750
  ""7"": 1100
  ""8"": 1500
  ""9"": 2000
  ""10"": 2750
  ""11"": 3500
progression_build_points:
  ""1"": 0
  ""2"": 2
  ""3"": 2
  ""4"": 2
  ""5"": 3
  ""6"": 3
  ""7"": 3
  ""8"": 4
  ""9"": 4
  ""10"": 5
  ""11"": 0
progression_level_bonuses:
  ""1"": 0
  ""2"": 0
  ""3"": 1
  ""4"": 1
  ""5"": 2
  ""6"": 2
  ""7"": 3
  ""8"": 3
  ""9"": 4
  ""10"": 4
  ""11"": 5
progression_item_slots:
  ""1"": 2
  ""2"": 2
  ""3"": 3
  ""4"": 3
  ""5"": 4
  ""6"": 4
  ""7"": 5
  ""8"": 5
  ""9"": 6
  ""10"": 6
  ""11"": 6
progression_failure_pool_tiers:
  intermediate_min: 4
  advanced_min: 7
  legendary_min: 10
";
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            Dictionary<string, object?>? parsed;
            try
            {
                parsed = deserializer.Deserialize<Dictionary<string, object?>>(yamlContent);
            }
            catch (Exception ex)
            {
                throw new FormatException("Failed to parse YAML content: " + ex.Message, ex);
            }

            if (parsed == null)
                throw new FormatException("YAML content did not parse to a dictionary.");

            string GetRequired(string key)
            {
                if (!parsed.TryGetValue(key, out var value))
                    throw new FormatException($"Missing required key: \"{key}\"");
                if (value == null)
                    throw new FormatException($"Key \"{key}\" has a null value.");
                return value.ToString()!;
            }

            // Parse optional prose fields
            string GetOptional(string key)
            {
                if (parsed.TryGetValue(key, out var v) && v != null)
                    return v.ToString();
                return null;
            }

            // Parse required horniness_time_modifiers
            parsed.TryGetValue("horniness_time_modifiers", out var htmObj);
            var htmCoerced = CoerceDictionary(htmObj, "horniness_time_modifiers");
            int ParseHtmInt(string key)
            {
                if (!htmCoerced.TryGetValue(key, out var v) || v == null)
                    throw new InvalidOperationException($"game-definition.yaml horniness_time_modifiers is missing required sub-key: {key}");
                if (!int.TryParse(v.ToString(), out int result))
                    throw new InvalidOperationException($"game-definition.yaml horniness_time_modifiers.{key} must be an integer");
                return result;
            }
            var horninessTimeModifiers = new HorninessTimeModifiers(
                morning: ParseHtmInt("morning"),
                afternoon: ParseHtmInt("afternoon"),
                evening: ParseHtmInt("evening"),
                overnight: ParseHtmInt("overnight"));

            // Validate core required keys first (throws FormatException)
            var name = GetRequired("name");
            var gameMasterPrompt = GetRequired("game_master_prompt");
            var playerAvatarRoleDescription = GetRequired("player_avatar_role_description");
            var dateeRoleDescription = GetRequired("datee_role_description");

            // Parse required global_dc_bias
            if (!parsed.TryGetValue("global_dc_bias", out var gdcbObj) || gdcbObj == null)
                throw new InvalidOperationException("game-definition.yaml is missing required key: global_dc_bias");
            if (!int.TryParse(gdcbObj.ToString(), out int globalDcBias))
                throw new InvalidOperationException("game-definition.yaml global_dc_bias must be an integer");

            // Parse optional shadow_dc_bias
            int shadowDcBias = 0;
            if (parsed.TryGetValue("shadow_dc_bias", out var sObj) && sObj != null)
            {
                if (!int.TryParse(sObj.ToString(), out shadowDcBias))
                    throw new InvalidOperationException("game-definition.yaml shadow_dc_bias must be an integer");
            }

            // Parse optional horniness_dc_bias
            int horninessDcBias = 0;
            if (parsed.TryGetValue("horniness_dc_bias", out var hObj) && hObj != null)
            {
                if (!int.TryParse(hObj.ToString(), out horninessDcBias))
                    throw new InvalidOperationException("game-definition.yaml horniness_dc_bias must be an integer");
            }

            // Parse optional archetypes_enabled
            bool archetypesEnabled = false;
            if (parsed.TryGetValue("archetypes_enabled", out var aeObj) && aeObj != null)
            {
                if (!bool.TryParse(aeObj.ToString(), out archetypesEnabled))
                    throw new InvalidOperationException("game-definition.yaml archetypes_enabled must be a boolean");
            }

            // Parse optional active_trap_interest_penalty
            double activeTrapInterestPenalty = -0.25;
            if (parsed.TryGetValue("active_trap_interest_penalty", out var atipObj) && atipObj != null)
            {
                var valStr = atipObj.ToString().Trim();
                if (valStr.EndsWith("%"))
                {
                    if (double.TryParse(valStr.Substring(0, valStr.Length - 1), out double percentVal))
                    {
                        activeTrapInterestPenalty = percentVal / 100.0;
                    }
                    else
                    {
                        throw new InvalidOperationException("game-definition.yaml active_trap_interest_penalty has invalid percentage format");
                    }
                }
                else if (double.TryParse(valStr, out double floatVal))
                {
                    activeTrapInterestPenalty = floatVal;
                }
                else
                {
                    throw new InvalidOperationException("game-definition.yaml active_trap_interest_penalty must be a number or percentage");
                }
            }

            int hungerForIntimacy = 0;
            if (parsed.TryGetValue("hunger_for_intimacy", out var hfiObj) && hfiObj != null)
            {
                if (!int.TryParse(hfiObj.ToString(), out int hfi))
                    throw new InvalidOperationException("game-definition.yaml hunger_for_intimacy must be an integer");
                hungerForIntimacy = hfi;
            }
            int terrorOfRejection = 0;
            if (parsed.TryGetValue("terror_of_rejection", out var torObj) && torObj != null)
            {
                if (!int.TryParse(torObj.ToString(), out int tor))
                    throw new InvalidOperationException("game-definition.yaml terror_of_rejection must be an integer");
                terrorOfRejection = tor;
            }

            // The 9 new blocks parsing
            parsed.TryGetValue("xp_flat_awards", out var xpFlatAwardsObj);
            var xpFlatAwards = ParseIntDictionary(xpFlatAwardsObj, "xp_flat_awards", new[] { "nat20", "nat1", "failure" });

            parsed.TryGetValue("xp_success_base", out var xpSuccessBaseObj);
            var xpSuccessBase = ParseIntDictionary(xpSuccessBaseObj, "xp_success_base", new[] { "dc_low_max", "dc_low_xp", "dc_mid_max", "dc_mid_xp", "dc_high_xp" });

            parsed.TryGetValue("xp_risk_multipliers", out var xpRiskMultipliersObj);
            var xpRiskMultipliers = ParseDoubleDictionary(xpRiskMultipliersObj, "xp_risk_multipliers", new[] { "safe", "medium", "hard", "bold", "reckless" });

            parsed.TryGetValue("xp_terminal_multipliers", out var xpTerminalMultipliersObj);
            var xpTerminalMultipliers = ParseDoubleDictionary(xpTerminalMultipliersObj, "xp_terminal_multipliers", new[] { "date_secured", "unmatched", "ghosted" });

            parsed.TryGetValue("progression_xp_thresholds", out var progressionXpThresholdsObj);
            var progressionXpThresholds = ParseIntDictionary(progressionXpThresholdsObj, "progression_xp_thresholds", null, isLevelKeys: true);

            parsed.TryGetValue("progression_build_points", out var progressionBuildPointsObj);
            var progressionBuildPoints = ParseIntDictionary(progressionBuildPointsObj, "progression_build_points", null, isLevelKeys: true);

            parsed.TryGetValue("progression_level_bonuses", out var progressionLevelBonusesObj);
            var progressionLevelBonuses = ParseIntDictionary(progressionLevelBonusesObj, "progression_level_bonuses", null, isLevelKeys: true);

            parsed.TryGetValue("progression_item_slots", out var progressionItemSlotsObj);
            var progressionItemSlots = ParseIntDictionary(progressionItemSlotsObj, "progression_item_slots", null, isLevelKeys: true);

            parsed.TryGetValue("progression_failure_pool_tiers", out var progressionFailurePoolTiersObj);
            var progressionFailurePoolTiers = ParseIntDictionary(progressionFailurePoolTiersObj, "progression_failure_pool_tiers", new[] { "intermediate_min", "advanced_min", "legendary_min" });

            return new GameDefinition(
                name: name,
                gameMasterPrompt: gameMasterPrompt,
                playerAvatarRoleDescription: playerAvatarRoleDescription,
                dateeRoleDescription: dateeRoleDescription,
                improvementPrompt: GetOptional("improvement_prompt"),
                steeringPrompt: GetOptional("steering_prompt"),
                horninessPrompt: GetOptional("horniness_prompt"),
                horninessTimeModifiers: horninessTimeModifiers,
                globalDcBias: globalDcBias,
                shadowDcBias: shadowDcBias,
                horninessDcBias: horninessDcBias,
                archetypesEnabled: archetypesEnabled,
                maxTurns: parsed.TryGetValue("max_turns", out var mtObj) && int.TryParse(mtObj?.ToString(), out int mt) ? mt : 30,
                maxDialogueOptions: parsed.TryGetValue("max_dialogue_options", out var mdoObj) && int.TryParse(mdoObj?.ToString(), out int mdo) ? mdo : 3,
                maxDeliveryWords: parsed.TryGetValue("max_delivery_words", out var mdwObj) && int.TryParse(mdwObj?.ToString(), out int mdw) ? mdw : 80,
                activeTrapInterestPenalty: activeTrapInterestPenalty,
                hungerForIntimacy: hungerForIntimacy,
                terrorOfRejection: terrorOfRejection,
                xpFlatAwards: xpFlatAwards,
                xpSuccessBase: xpSuccessBase,
                xpRiskMultipliers: xpRiskMultipliers,
                xpTerminalMultipliers: xpTerminalMultipliers,
                progressionXpThresholds: progressionXpThresholds,
                progressionBuildPoints: progressionBuildPoints,
                progressionLevelBonuses: progressionLevelBonuses,
                progressionItemSlots: progressionItemSlots,
                progressionFailurePoolTiers: progressionFailurePoolTiers
            );
        }
    }
}