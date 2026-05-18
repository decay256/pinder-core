using System.Text.Json.Serialization;

namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Post-resolution verdict for a <see cref="RollCheckResult"/>, after every
    /// in-engine override (shadow-corruption demotion etc.) has been applied.
    ///
    /// Distinct from <see cref="RollCheckResult.IsSuccess"/>, which reflects the
    /// pre-shadow-corruption outcome (back-compat, see #927).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RollVerdict
    {
        /// <summary>The check ultimately succeeded.</summary>
        Success = 0,

        /// <summary>The check ultimately failed (possibly via shadow-corruption demotion).</summary>
        Miss = 1,
    }
}
