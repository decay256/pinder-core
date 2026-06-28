using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.LlmAdapters
{
    public partial class GameDefinition
    {
        /// <summary>
        /// Parse a YAML string into a GameDefinition.
        /// Throws FormatException if YAML is invalid or missing required keys.
        /// Throws ArgumentNullException if yamlContent is null.
        /// </summary>
        public static GameDefinition LoadFrom(string yamlContent)
        {
            if (yamlContent == null)
                throw new ArgumentNullException(nameof(yamlContent));

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
            var horninessTimeModifiers = CatalogParser.ParseHorninessTimeModifiers(htmObj);

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
                activeTrapInterestPenalty: activeTrapInterestPenalty
            );
        }
    }
}
