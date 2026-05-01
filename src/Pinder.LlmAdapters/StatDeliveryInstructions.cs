using System;
using System.Collections.Generic;
using Pinder.Core.Stats;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pinder.LlmAdapters
{
    /// <summary>
    /// Per-stat, per-tier delivery instructions loaded from delivery-instructions.yaml.
    /// Provides specific LLM prompts for each stat × outcome combination.
    /// Falls back to null when a key is missing — callers should use hardcoded defaults.
    /// </summary>
    public sealed class StatDeliveryInstructions
    {
        private readonly Dictionary<string, Dictionary<string, string>> _instructions;

        private StatDeliveryInstructions(Dictionary<string, Dictionary<string, string>> instructions)
        {
            _instructions = instructions ?? new Dictionary<string, Dictionary<string, string>>();
        }

        /// <summary>
        /// Returns the instruction for a given stat and tier key, or null if not found.
        /// Tier keys: clean | strong | critical | exceptional | nat20 | fumble | misfire | trope_trap | catastrophe | nat1
        /// </summary>
        public string Get(StatType stat, string tierKey)
        {
            string statKey = StatKey(stat);
            if (_instructions.TryGetValue(statKey, out var tiers) &&
                tiers.TryGetValue(tierKey, out var text) &&
                !string.IsNullOrWhiteSpace(text))
                return text;
            return null;
        }

        /// <summary>
        /// Resolves the success tier key from beat margin and nat20 flag.
        /// </summary>
        public static string SuccessTierKey(int beatDcBy, bool isNat20)
        {
            if (isNat20) return "nat20";
            if (beatDcBy >= 15) return "exceptional";
            if (beatDcBy >= 10) return "critical";
            if (beatDcBy >= 5)  return "strong";
            return "clean";
        }

        /// <summary>
        /// Resolves the failure tier key from FailureTier enum.
        /// </summary>
        public static string FailureTierKey(Pinder.Core.Rolls.FailureTier tier)
        {
            switch (tier)
            {
                case Pinder.Core.Rolls.FailureTier.Fumble:     return "fumble";
                case Pinder.Core.Rolls.FailureTier.Misfire:    return "misfire";
                case Pinder.Core.Rolls.FailureTier.TropeTrap:  return "trope_trap";
                case Pinder.Core.Rolls.FailureTier.Catastrophe: return "catastrophe";
                case Pinder.Core.Rolls.FailureTier.Legendary:  return "nat1";
                default: return "fumble";
            }
        }

        /// <summary>
        /// Returns the stat-specific failure instruction for the given stat and failure tier, or null if not found.
        /// Convenience wrapper around Get() + FailureTierKey().
        /// </summary>
        public string GetStatFailureInstruction(Pinder.Core.Stats.StatType stat, Pinder.Core.Rolls.FailureTier tier)
        {
            string tierKey = FailureTierKey(tier);
            return Get(stat, tierKey);
        }

        /// <summary>
        /// Returns the horniness overlay instruction for the given failure tier, or null if not found.
        /// The horniness_overlay section in YAML has tiers: fumble, misfire, trope_trap, catastrophe.
        /// </summary>
        public string GetHorninessOverlayInstruction(Pinder.Core.Rolls.FailureTier tier)
        {
            string tierKey = FailureTierKey(tier);
            if (_instructions.TryGetValue("horniness_overlay", out var tiers) &&
                tiers.TryGetValue(tierKey, out var text) &&
                !string.IsNullOrWhiteSpace(text))
                return text;
            return null;
        }

        /// <summary>
        /// Returns the shadow corruption instruction for the given shadow stat and failure tier, or null if not found.
        /// The shadow_corruption section in YAML has subsections per shadow, each with tiers:
        /// fumble, misfire, trope_trap, catastrophe.
        /// </summary>
        public string GetShadowCorruptionInstruction(Pinder.Core.Stats.ShadowStatType shadow, Pinder.Core.Rolls.FailureTier tier)
        {
            string shadowKey = ShadowKey(shadow);
            string tierKey = FailureTierKey(tier);

            // shadow_corruption is parsed as composite keys: "shadow_corruption.{shadowName}"
            string compositeKey = "shadow_corruption." + shadowKey;
            if (_instructions.TryGetValue(compositeKey, out var shadowTiers) &&
                shadowTiers.TryGetValue(tierKey, out var text) &&
                !string.IsNullOrWhiteSpace(text))
                return text;
            return null;
        }

        private static string ShadowKey(Pinder.Core.Stats.ShadowStatType shadow)
        {
            switch (shadow)
            {
                case Pinder.Core.Stats.ShadowStatType.Madness:       return "madness";
                case Pinder.Core.Stats.ShadowStatType.Despair:       return "despair";
                case Pinder.Core.Stats.ShadowStatType.Denial:        return "denial";
                case Pinder.Core.Stats.ShadowStatType.Fixation:      return "fixation";
                case Pinder.Core.Stats.ShadowStatType.Dread:         return "dread";
                case Pinder.Core.Stats.ShadowStatType.Overthinking:  return "overthinking";
                default: return shadow.ToString().ToLowerInvariant();
            }
        }

        private static string StatKey(StatType stat)
        {
            switch (stat)
            {
                case StatType.Charm:         return "charm";
                case StatType.Rizz:          return "rizz";
                case StatType.Honesty:       return "honesty";
                case StatType.Chaos:         return "chaos";
                case StatType.Wit:           return "wit";
                case StatType.SelfAwareness: return "sa";
                default:                     return stat.ToString().ToLowerInvariant();
            }
        }

        /// <summary>
        /// Parses delivery-instructions.yaml into a StatDeliveryInstructions instance.
        /// Returns an empty instance (all lookups return null) on parse failure.
        /// </summary>
        public static StatDeliveryInstructions LoadFrom(string yamlContent)
        {
            if (string.IsNullOrWhiteSpace(yamlContent))
                return new StatDeliveryInstructions(null);

            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();

                var root = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);
                if (root == null)
                    return new StatDeliveryInstructions(null);

                var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                // Parse delivery_instructions (flat stat → tier → text)
                if (root.TryGetValue("delivery_instructions", out var diObj) &&
                    diObj is Dictionary<object, object> statMap)
                {
                    foreach (var statEntry in statMap)
                    {
                        string statKey = statEntry.Key?.ToString() ?? "";
                        var tierDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        if (statEntry.Value is Dictionary<object, object> tierMap)
                        {
                            foreach (var tierEntry in tierMap)
                            {
                                string tierKey = tierEntry.Key?.ToString() ?? "";
                                string text = tierEntry.Value?.ToString() ?? "";
                                if (!string.IsNullOrWhiteSpace(tierKey))
                                    tierDict[tierKey] = text;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(statKey))
                            result[statKey] = tierDict;
                    }
                }

                // Parse shadow_corruption (nested: shadowName → tier → text)
                // Stored as composite keys "shadow_corruption.{shadowName}" for lookup.
                if (root.TryGetValue("shadow_corruption", out var scObj) &&
                    scObj is Dictionary<object, object> shadowMap)
                {
                    foreach (var shadowEntry in shadowMap)
                    {
                        string shadowName = shadowEntry.Key?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(shadowName)) continue;

                        var tierDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        if (shadowEntry.Value is Dictionary<object, object> tierMap)
                        {
                            foreach (var tierEntry in tierMap)
                            {
                                string tierKey = tierEntry.Key?.ToString() ?? "";
                                string text = tierEntry.Value?.ToString() ?? "";
                                if (!string.IsNullOrWhiteSpace(tierKey) && tierKey != "description")
                                    tierDict[tierKey] = text;
                            }
                        }

                        result["shadow_corruption." + shadowName] = tierDict;
                    }
                }

                // Post-process horniness_overlay: prepend shared _genre_framing preamble to each
                // tier instruction so the philosophy text reaches the LLM on every call.
                // _genre_framing is a single source-of-truth key (leading underscore) that the
                // strongly-typed caller methods never expose directly; we fold it in here at load
                // time and then remove it so it doesn't appear as a tier.
                //
                // Catastrophe tier additionally receives a one-line tier-specific reinforcement
                // appended after the preamble+instruction composite. Implemented here (engine-side)
                // rather than duplicated in yaml, so both the preamble and the catastrophe extra
                // live in one place and evolve together. Choice: loader constant (not inline yaml,
                // not per-tier yaml map) — keeps yaml DRY and makes the reinforcement visible
                // during C# code review alongside the prepend logic.
                const string CatastropheReinforcement =
                    "The structure is a normal Tinder question. The content is the joke. The character is utterly unaware.";

                if (result.TryGetValue("horniness_overlay", out var horninessMap) &&
                    horninessMap.TryGetValue("_genre_framing", out var genreFraming) &&
                    !string.IsNullOrWhiteSpace(genreFraming))
                {
                    horninessMap.Remove("_genre_framing");
                    string preamble = genreFraming.Trim();
                    string[] horningTiers = new[] { "fumble", "misfire", "trope_trap", "catastrophe" };
                    foreach (var t in horningTiers)
                    {
                        if (horninessMap.TryGetValue(t, out var existing))
                        {
                            string composed = preamble + "\n\n" + existing.TrimStart();
                            if (t == "catastrophe")
                                composed += "\n\n" + CatastropheReinforcement;
                            horninessMap[t] = composed;
                        }
                    }
                }

                return new StatDeliveryInstructions(result);
            }
            catch
            {
                return new StatDeliveryInstructions(null);
            }
        }
    }
}
