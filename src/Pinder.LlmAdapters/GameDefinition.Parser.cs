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

            DeliveryRules deliveryRules = null;
            if (parsed.TryGetValue("delivery_rules", out var drObj))
            {
                deliveryRules = CatalogParser.ParseDeliveryRules(drObj);
            }

            DramaticCraft dramaticCraft = null;
            if (parsed.TryGetValue("dramatic_craft", out var dcObj))
            {
                dramaticCraft = CatalogParser.ParseDramaticCraft(dcObj);
            }

            // Parse optional prose fields
            string GetOptional(string key)
            {
                if (parsed.TryGetValue(key, out var v) && v != null)
                    return v.ToString();
                return null;
            }
            // conversation_arc is a dict with a "progression" key
            string conversationArcProgression = null;
            if (parsed.TryGetValue("conversation_arc", out var caObj) && caObj is Dictionary<object, object> caDict)
            {
                if (caDict.TryGetValue("progression", out var caV) && caV != null)
                    conversationArcProgression = caV.ToString();
            }

            // Parse required horniness_time_modifiers
            parsed.TryGetValue("horniness_time_modifiers", out var htmObj);
            var horninessTimeModifiers = CatalogParser.ParseHorninessTimeModifiers(htmObj);

            // Validate core required keys first (throws FormatException)
            var name = GetRequired("name");
            var vision = GetRequired("vision");
            var worldDescription = GetRequired("world_description");
            var playerRoleDescription = GetRequired("player_role_description");
            var opponentRoleDescription = GetRequired("opponent_role_description");
            var narrativeDoctrine = GetRequired("narrative_doctrine");

            // Parse required global_dc_bias
            if (!parsed.TryGetValue("global_dc_bias", out var gdcbObj) || gdcbObj == null)
                throw new InvalidOperationException("game-definition.yaml is missing required key: global_dc_bias");
            if (!int.TryParse(gdcbObj.ToString(), out int globalDcBias))
                throw new InvalidOperationException("game-definition.yaml global_dc_bias must be an integer");

            return new GameDefinition(
                name: name,
                vision: vision,
                worldDescription: worldDescription,
                playerRoleDescription: playerRoleDescription,
                opponentRoleDescription: opponentRoleDescription,
                narrativeDoctrine: narrativeDoctrine,
                deliveryRules: deliveryRules,
                dramaticCraft: dramaticCraft,
                opponentFriction: GetOptional("opponent_friction"),
                opponentCuriosity: GetOptional("opponent_curiosity"),
                conversationArcProgression: conversationArcProgression,
                playerProbing: GetOptional("player_probing"),
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
