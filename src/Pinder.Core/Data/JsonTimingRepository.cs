using System;
using System.Collections.Generic;
using Pinder.Core.Conversation;

namespace Pinder.Core.Data
{
    /// <summary>
    /// Loads timing profiles from a JSON string. Follows the same pattern as JsonItemRepository.
    /// </summary>
    public sealed class JsonTimingRepository
    {
        private readonly Dictionary<string, TimingProfile> _profiles =
            new Dictionary<string, TimingProfile>(StringComparer.Ordinal);

        /// <param name="json">Full JSON string — contents of response-profiles.json.</param>
        public JsonTimingRepository(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            var root = JsonParser.Parse(json);
            if (!(root is JsonArray arr))
                throw new FormatException("Expected top-level JSON array for timing profiles.");

            foreach (var element in arr.Items)
            {
                if (!(element is JsonObject obj)) continue;
                var (id, profile) = ParseProfile(obj);
                _profiles[id] = profile;
            }
        }

        /// <summary>Returns the TimingProfile for a given profile ID, or null if not found.</summary>
        public TimingProfile? GetProfile(string profileId)
        {
            _profiles.TryGetValue(profileId, out var profile);
            return profile;
        }

        /// <summary>Returns all loaded profiles.</summary>
        public IEnumerable<TimingProfile> GetAll() => _profiles.Values;

        private static (string id, TimingProfile profile) ParseProfile(JsonObject obj)
        {
            string id = obj.GetString("id");
            if (string.IsNullOrEmpty(id))
                throw new FormatException("Timing profile missing required field 'id'.");

            int baseDelay = obj.GetInt("baseDelayMinutes");
            if (baseDelay == 0 && !HasKey(obj, "baseDelayMinutes"))
                throw new FormatException($"Timing profile '{id}' missing required field 'baseDelayMinutes'.");

            float variance = obj.GetFloat("varianceMultiplier");
            float drySpell = obj.GetFloat("drySpellProbability");
            string receipt = obj.GetString("readReceipt", "neutral");

            var profile = new TimingProfile(baseDelay, variance, drySpell, receipt);
            return (id, profile);
        }

        private static bool HasKey(JsonObject obj, string key)
        {
            return obj.Properties.ContainsKey(key);
        }
    }
}
