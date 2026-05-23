using System;
using System.Collections.Generic;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Static parser class that extracts dictionary-to-struct parsing for
    /// nested rule/modifier structures and catalogs (such as i18n and consequences).
    /// </summary>
    public static class CatalogParser
    {
        /// <summary>
        /// Parses a dictionary object into a <see cref="DeliveryRules"/> instance.
        /// </summary>
        public static DeliveryRules? ParseDeliveryRules(object? obj)
        {
            if (obj is Dictionary<object, object> dict)
            {
                string DrGet(string key)
                {
                    if (dict.TryGetValue(key, out var v) && v != null)
                        return v.ToString();
                    return "";
                }
                return new DeliveryRules(
                    clean: DrGet("clean"),
                    strong: DrGet("strong"),
                    critical: DrGet("critical"),
                    exceptional: DrGet("exceptional"),
                    test: DrGet("test"),
                    registerInstruction: DrGet("register_instruction"),
                    mediumRule: DrGet("medium_rule"));
            }
            return null;
        }

        /// <summary>
        /// Parses a dictionary object into a <see cref="DramaticCraft"/> instance.
        /// </summary>
        public static DramaticCraft? ParseDramaticCraft(object? obj)
        {
            if (obj is Dictionary<object, object> dict)
            {
                string DcGet(string key)
                {
                    if (dict.TryGetValue(key, out var v) && v != null)
                        return v.ToString();
                    return "";
                }
                return new DramaticCraft(
                    goal: DcGet("goal"),
                    opponentWant: DcGet("opponent_want"),
                    revelationBudget: DcGet("revelation_budget"),
                    directnessDial: DcGet("directness_dial"),
                    failureCost: DcGet("failure_cost"),
                    earningTheClose: DcGet("earning_the_close"));
            }
            return null;
        }

        /// <summary>
        /// Parses a dictionary object into a <see cref="HorninessTimeModifiers"/> instance.
        /// </summary>
        public static HorninessTimeModifiers ParseHorninessTimeModifiers(object? obj)
        {
            if (obj == null)
                throw new InvalidOperationException("game-definition.yaml is missing required key: horniness_time_modifiers");

            if (!(obj is Dictionary<object, object> htmDict))
                throw new InvalidOperationException("game-definition.yaml is missing required key: horniness_time_modifiers");

            int ParseHtmInt(string key)
            {
                if (!htmDict.TryGetValue(key, out var v) || v == null)
                    throw new InvalidOperationException($"game-definition.yaml horniness_time_modifiers is missing required sub-key: {key}");
                if (!int.TryParse(v.ToString(), out int result))
                    throw new InvalidOperationException($"game-definition.yaml horniness_time_modifiers.{key} must be an integer");
                return result;
            }

            return new HorninessTimeModifiers(
                morning: ParseHtmInt("morning"),
                afternoon: ParseHtmInt("afternoon"),
                evening: ParseHtmInt("evening"),
                overnight: ParseHtmInt("overnight"));
        }

        /// <summary>
        /// Instantiates a <see cref="ConsequenceCatalog"/> wrapper over an existing <see cref="I18nCatalog"/>.
        /// </summary>
        public static ConsequenceCatalog ParseConsequenceCatalog(I18nCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            return new ConsequenceCatalog(catalog);
        }

        /// <summary>
        /// Loads and parses an <see cref="I18nCatalog"/> from the specified directory and locale.
        /// </summary>
        public static I18nCatalog ParseI18nCatalog(string i18nRoot, string locale = "en")
        {
            if (i18nRoot == null)
                throw new ArgumentNullException(nameof(i18nRoot));
            return I18nCatalog.LoadFromDirectory(i18nRoot, locale);
        }
    }
}
