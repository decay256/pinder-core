namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Provides a utility function for normalizing various string representations
    /// of game statistics found in LLM outputs to a standardized StatType enum name.
    /// </summary>
    internal static class StatNameNormalizer
    {
        /// <summary>
        /// Normalizes LLM stat names like "SELF_AWARENESS" to C# enum names like "SelfAwareness".
        /// </summary>
        public static string NormalizeStatName(string raw)
        {
            if (string.Equals(raw, "SELF_AWARENESS", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "SELFAWARENESS", System.StringComparison.OrdinalIgnoreCase))
            {
                return "SelfAwareness";
            }
            return raw;
        }
    }
}
