using System;

namespace Pinder.LlmAdapters.Anthropic
{
    /// <summary>
    /// Centralized registry and mapper for Anthropic model IDs.
    /// This is the single source of truth for internal-to-API model ID mapping.
    /// </summary>
    public static class AnthropicModelIds
    {
        /// <summary>
        /// Default model used when no model is explicitly specified.
        /// </summary>
        public const string DefaultModel = "claude-sonnet-4-20250514";

        /// <summary>
        /// Maps an internal model specification string (which may include a provider prefix
        /// and/or a thinking suffix) to the exact, dashed model ID required by the Anthropic API.
        /// Unknown models are passed through with dots replaced by dashes.
        /// </summary>
        /// <param name="internalSpec">The internal model specification string.</param>
        /// <returns>The official Anthropic API model ID.</returns>
        public static string ToApiId(string internalSpec)
        {
            if (string.IsNullOrEmpty(internalSpec))
            {
                return internalSpec;
            }

            string processed = internalSpec;

            // 1. Strip the "anthropic/" provider prefix (case-insensitive)
            if (processed.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase))
            {
                processed = processed.Substring("anthropic/".Length);
            }

            // 2. Strip the "-thinking-{low,mid,high}" suffix (case-insensitive)
            if (processed.EndsWith("-thinking-low", StringComparison.OrdinalIgnoreCase))
            {
                processed = processed.Substring(0, processed.Length - "-thinking-low".Length);
            }
            else if (processed.EndsWith("-thinking-mid", StringComparison.OrdinalIgnoreCase))
            {
                processed = processed.Substring(0, processed.Length - "-thinking-mid".Length);
            }
            else if (processed.EndsWith("-thinking-high", StringComparison.OrdinalIgnoreCase))
            {
                processed = processed.Substring(0, processed.Length - "-thinking-high".Length);
            }

            // 3. Convert dotted alias to dashed Anthropic API ID
            switch (processed.ToLowerInvariant())
            {
                case "claude-opus-4.8":
                    return "claude-opus-4-8";
                case "claude-opus-4.7":
                    return "claude-opus-4-7";
                case "claude-sonnet-4.6":
                    return "claude-sonnet-4-6";
                case "claude-sonnet-4-20250514":
                    return "claude-sonnet-4-20250514";
                default:
                    // Fallback: replace any dots with dashes
                    return processed.Replace('.', '-');
            }
        }
    }
}
