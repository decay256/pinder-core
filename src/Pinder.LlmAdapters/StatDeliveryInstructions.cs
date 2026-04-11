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
                if (root == null || !root.TryGetValue("delivery_instructions", out var diObj))
                    return new StatDeliveryInstructions(null);

                var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                if (diObj is Dictionary<object, object> statMap)
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

                return new StatDeliveryInstructions(result);
            }
            catch
            {
                return new StatDeliveryInstructions(null);
            }
        }
    }
}
