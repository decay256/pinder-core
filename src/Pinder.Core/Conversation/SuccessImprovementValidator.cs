using System;

namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Validates success improvement model responses to ensure they do not leak meta/control text (see #1243).
    /// </summary>
    public static class SuccessImprovementValidator
    {
        private static readonly string[] CaseInsensitiveMarkers = new[]
        {
            "INVALID_ENGINE_STATE",
            "I need to analyze",
            "I need ENGINE_STATE",
            "generate OPTIONS"
        };

        private static readonly string[] OrdinalMarkers = new[]
        {
            "<ENGINE_STATE>",
            "OPTION_",
            "[STAT:"
        };

        public static bool IsRejected(string? response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            foreach (var marker in CaseInsensitiveMarkers)
            {
                if (response.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            foreach (var marker in OrdinalMarkers)
            {
                if (response.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }
    }
}
