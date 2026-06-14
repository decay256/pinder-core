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

            string GetRequiredWithFallback(string newKey, string oldKey)
            {
                if (parsed.TryGetValue(newKey, out var val))
                {
                    if (val == null) throw new FormatException($"Key \"{newKey}\" has a null value.");
                    return val.ToString()!;
                }
                if (parsed.TryGetValue(oldKey, out val))
                {
                    if (val == null) throw new FormatException($"Key \"{oldKey}\" has a null value.");
                    return val.ToString()!;
                }
                throw new FormatException($"Missing required key: \"{newKey}\"");
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
            var playerAvatarRoleDescription = GetRequiredWithFallback("player_avatar_role_description", "player_role_description");
            var dateeRoleDescription = GetRequired("datee_role_description");

            // Parse required global_dc_bias
            if (!parsed.TryGetValue("global_dc_bias", out var gdcbObj) || gdcbObj == null)
                throw new InvalidOperationException("game-definition.yaml is missing required key: global_dc_bias");
            if (!int.TryParse(gdcbObj.ToString(), out int globalDcBias))
                throw new InvalidOperationException("game-definition.yaml global_dc_bias must be an integer");

            return new GameDefinition(
                name: name,
                gameMasterPrompt: gameMasterPrompt,
                playerAvatarRoleDescription: playerAvatarRoleDescription,
                dateeRoleDescription: dateeRoleDescription,
                improvementPrompt: GetOptional("improvement_prompt"),
                steeringPrompt: GetOptional("steering_prompt"),
                horninessTimeModifiers: horninessTimeModifiers,
                globalDcBias: globalDcBias,
                maxTurns: parsed.TryGetValue("max_turns", out var mtObj) && int.TryParse(mtObj?.ToString(), out int mt) ? mt : 30,
                maxDialogueOptions: parsed.TryGetValue("max_dialogue_options", out var mdoObj) && int.TryParse(mdoObj?.ToString(), out int mdo) ? mdo : 3,
                maxDeliveryWords: parsed.TryGetValue("max_delivery_words", out var mdwObj) && int.TryParse(mdwObj?.ToString(), out int mdw) ? mdw : 80
            );
        }
    }
}
