using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pinder.Core.Diagnostics.AgentJournals
{
    public static class AgentJournalJson
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        public static string Serialize<T>(T value)
            => JsonSerializer.Serialize(value, Options);

        public static T Deserialize<T>(string json)
            => JsonSerializer.Deserialize<T>(json, Options);

        public static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
            return options;
        }
    }
}
